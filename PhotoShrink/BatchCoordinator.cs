using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace PhotoShrink;

/// <summary>
/// Gathers the files from the many processes Explorer starts (one per selected file)
/// into a single batch.
///
/// The first process for a given size takes a named mutex and listens on a named pipe.
/// Every later process for that size sends its path(s) over the pipe, waits for an
/// "OK" acknowledgement, and exits. When no new path has arrived for the quiet period,
/// the first process stops listening, releases the mutex, and processes the batch.
///
/// A process that arrives after the window closed gets "NO" (or cannot connect) and
/// retries; once the mutex is free it becomes the leader of a new batch. No path is
/// ever dropped: a follower only exits after it has received "OK".
///
/// Names include the size, so a 1200 px batch and a 1600 px batch started at the same
/// time never mix. The pipe name also includes the Windows session id so two users
/// on one machine don't collide.
/// </summary>
internal sealed class BatchCoordinator : IDisposable
{
    private const int ListenerCount = 4;
    private const int ConnectTimeoutMs = 1000;
    private const int AckTimeoutMs = 5000;
    private const int JoinDeadlineMs = 60000;
    private const int MaxCollectMs = 120000;

    private readonly int _maxSize;
    private readonly object _lock = new();
    private readonly List<string> _paths = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _listeners = new();

    private Mutex? _mutex;
    private bool _accepting;
    private long _lastArrivalTicks;

    public BatchCoordinator(int maxSize)
    {
        _maxSize = maxSize;
        int session = Process.GetCurrentProcess().SessionId;
        MutexName = $@"Local\PhotoShrink-{maxSize}";
        PipeName = $"PhotoShrink-{maxSize}-s{session}";
    }

    public string MutexName { get; }
    public string PipeName { get; }

    /// <summary>Number of paths collected so far (leader only).</summary>
    public int Count { get { lock (_lock) return _paths.Count; } }

    /// <summary>
    /// Returns true if this process is the leader (and should show UI and process the batch),
    /// false if the files were handed to another process and this one should exit.
    /// </summary>
    public bool JoinOrLead(IReadOnlyList<string> files)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < JoinDeadlineMs)
        {
            if (TryTakeMutex())
            {
                StartListening(files);
                return true;
            }
            if (TrySend(files))
                return false;
            Thread.Sleep(50);
        }

        // Something is badly stuck (e.g. a hung leader). Rather than lose the files,
        // process them on our own without the mutex.
        lock (_lock) foreach (var f in files) AddPath(f);
        return true;
    }

    /// <summary>
    /// Waits until no new path has arrived for <paramref name="quietMs"/>, then stops
    /// accepting, closes the pipe, releases the mutex, and returns the batch.
    /// Must be called on the thread that called <see cref="JoinOrLead"/> (mutex affinity);
    /// WinForms' async continuations guarantee that when awaited from the UI thread.
    /// </summary>
    public async Task<IReadOnlyList<string>> CollectAsync(int quietMs, IProgress<int>? progress = null)
    {
        var total = Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(100);
            long last = Interlocked.Read(ref _lastArrivalTicks);
            double quiet = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
            progress?.Report(Count);
            if (quiet >= quietMs || total.ElapsedMilliseconds >= MaxCollectMs)
                break;
        }
        return StopAndRelease();
    }

    private IReadOnlyList<string> StopAndRelease()
    {
        lock (_lock) _accepting = false;
        _cts.Cancel();
        try { Task.WaitAll(_listeners.ToArray(), 2000); } catch { /* cancelled */ }
        ReleaseMutex();
        lock (_lock) return _paths.ToList();
    }

    public void Dispose()
    {
        lock (_lock) _accepting = false;
        _cts.Cancel();
        ReleaseMutex();
    }

    // ---------------------------------------------------------------- leader side

    private bool TryTakeMutex()
    {
        var m = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = m.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // previous leader crashed; we own it now
        }
        if (!owned)
        {
            m.Dispose();
            return false;
        }
        _mutex = m;
        return true;
    }

    private void ReleaseMutex()
    {
        var m = Interlocked.Exchange(ref _mutex, null);
        if (m == null) return;
        try { m.ReleaseMutex(); } catch (ApplicationException) { /* not owned by this thread */ }
        m.Dispose();
    }

    private void StartListening(IReadOnlyList<string> ownFiles)
    {
        lock (_lock)
        {
            _accepting = true;
            foreach (var f in ownFiles) AddPath(f);
        }
        for (int i = 0; i < ListenerCount; i++)
            _listeners.Add(Task.Run(() => ListenLoopAsync(_cts.Token)));
    }

    private void AddPath(string path)
    {
        if (_seen.Add(path))
            _paths.Add(path);
        Interlocked.Exchange(ref _lastArrivalTicks, Stopwatch.GetTimestamp());
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                await Task.Delay(20, CancellationToken.None);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                return;
            }
            catch (IOException)
            {
                server.Dispose();
                continue;
            }

            // Handle the client inline: messages are tiny, and four loops give enough concurrency.
            await HandleClientAsync(server);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                using var timeout = new CancellationTokenSource(AckTimeoutMs);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true };

                var received = new List<string>();
                while (true)
                {
                    string? line = await reader.ReadLineAsync(timeout.Token);
                    if (line == null) return;          // client vanished before finishing
                    if (line.Length == 0) break;       // end of message
                    received.Add(line);
                }

                bool accepted;
                lock (_lock)
                {
                    accepted = _accepting;
                    if (accepted)
                        foreach (var p in received) AddPath(p);
                }
                await writer.WriteLineAsync(accepted ? "OK" : "NO");
                try { server.WaitForPipeDrain(); } catch (IOException) { }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Client will retry if it didn't get "OK".
            }
        }
    }

    // ---------------------------------------------------------------- follower side

    private bool TrySend(IReadOnlyList<string> files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMs);

            var sb = new StringBuilder();
            foreach (var f in files) sb.Append(f).Append('\n');
            sb.Append('\n');
            byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            client.Write(bytes, 0, bytes.Length);
            client.Flush();

            using var reader = new StreamReader(client, new UTF8Encoding(false), false, 256, leaveOpen: true);
            using var timeout = new CancellationTokenSource(AckTimeoutMs);
            string? ack = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            return ack == "OK";
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException
                                       or UnauthorizedAccessException or ObjectDisposedException)
        {
            return false;
        }
    }
}
