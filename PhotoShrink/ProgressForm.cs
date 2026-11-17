using System.Diagnostics;

namespace PhotoShrink;

/// <summary>
/// Small window shown by the leader process: first while it gathers the files from the
/// other processes Explorer started, then while it shrinks them. Opens the summary at the end.
/// </summary>
internal sealed class ProgressForm : Form
{
    private readonly BatchCoordinator _coordinator;
    private readonly int _maxSize;
    private readonly Settings _settings;
    private readonly string? _reportPath;

    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 24 };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22, Style = ProgressBarStyle.Marquee };
    private readonly Label _file = new() { AutoSize = false, Dock = DockStyle.Top, Height = 24, AutoEllipsis = true };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
    private volatile bool _cancelRequested;
    private bool _finished;

    public ProgressForm(BatchCoordinator coordinator, int maxSize, Settings settings, string? reportPath)
    {
        _coordinator = coordinator;
        _maxSize = maxSize;
        _settings = settings;
        _reportPath = reportPath;

        Text = $"PhotoShrink — {maxSize} px";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 130);
        Padding = new Padding(12);
        Font = SystemFonts.MessageBoxFont ?? Font;

        var buttonRow = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        _cancel.Location = new Point(buttonRow.Width - _cancel.Width, 4);
        buttonRow.Controls.Add(_cancel);
        buttonRow.Resize += (_, _) => _cancel.Left = buttonRow.ClientSize.Width - _cancel.Width;
        _cancel.Click += (_, _) => { _cancelRequested = true; _cancel.Enabled = false; _status.Text = "Cancelling…"; };

        // Dock order: last added is laid out first for Top docking.
        Controls.Add(_file);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6 });
        Controls.Add(_bar);
        Controls.Add(_status);
        Controls.Add(buttonRow);

        _status.Text = "Gathering the selected files…";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished && e.CloseReason == CloseReason.UserClosing)
        {
            // Closing the window while working means "cancel"; finish the current file(s) first.
            _cancelRequested = true;
            _cancel.Enabled = false;
            _status.Text = "Cancelling…";
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            _finished = true;
            MessageBox.Show(this, "PhotoShrink hit an unexpected error:\n\n" + ex, "PhotoShrink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task RunAsync()
    {
        var sw = Stopwatch.StartNew();

        // Runs on the UI thread, which is the thread that owns the mutex.
        var gathered = new Progress<int>(n => _status.Text = $"Gathering the selected files… {n}");
        IReadOnlyList<string> files = await _coordinator.CollectAsync(_settings.QuietPeriodMs, gathered);

        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Maximum = Math.Max(1, files.Count);
        _bar.Value = 0;
        _status.Text = $"Shrinking 0 of {files.Count}…";

        var results = new ShrinkResult[files.Count];
        int done = 0;
        var progress = new Progress<string>(name =>
        {
            _bar.Value = Math.Min(_bar.Maximum, done);
            _status.Text = $"Shrinking {done} of {files.Count}…";
            _file.Text = name;
        });
        IProgress<string> report = progress;

        int quality = _settings.JpegQuality;
        int parallel = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        await Task.Run(() =>
        {
            Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
            {
                if (_cancelRequested)
                {
                    results[i] = new ShrinkResult
                    {
                        Path = files[i], Status = ShrinkStatus.SkippedCancelled, Reason = "cancelled",
                    };
                }
                else
                {
                    results[i] = ImageShrinker.Shrink(files[i], _maxSize, quality);
                }
                Interlocked.Increment(ref done);
                report.Report(Path.GetFileName(files[i]));
            });
        });

        _finished = true;
        string text = Summary.BuildText(results, _maxSize, sw.Elapsed);

        if (_reportPath != null)
        {
            Summary.AppendReport(_reportPath, results, _maxSize, text);
            Close();
            return;
        }

        var summary = new SummaryForm(text, results);
        summary.FormClosed += (_, _) => Close();
        Hide();
        summary.Show();
        summary.Activate();
    }
}
