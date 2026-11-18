using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PhotoShrink.TestKit;

/// <summary>
/// End-to-end tests for PhotoShrink.exe. Nothing is registered in the shell: the tests start
/// PhotoShrink.exe directly, one process per file, exactly the way Explorer does for a
/// classic context-menu verb, and use --report so no summary window has to be closed.
///
///   PhotoShrink.TestKit.exe &lt;path to PhotoShrink.exe&gt; &lt;work folder&gt;
///
/// Writes everything it prints to &lt;work folder&gt;\results.txt. Exit code 0 = all checks passed.
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();
    private static int _failures;
    private static int _checks;

    private static readonly DateTime Created = new(2019, 3, 4, 10, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Modified = new(2020, 5, 6, 11, 22, 33, DateTimeKind.Local);
    private static readonly int[] Sizes = { 1200, 1600, 2400 };

    private static string _exe = "";
    private static string _work = "";

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: PhotoShrink.TestKit <PhotoShrink.exe> <work folder>");
            return 2;
        }
        _exe = Path.GetFullPath(args[0]);
        _work = Path.GetFullPath(args[1]);
        if (!File.Exists(_exe))
        {
            Console.WriteLine("Not found: " + _exe);
            return 2;
        }

        if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true);
        Directory.CreateDirectory(_work);

        W($"PhotoShrink end-to-end test  {DateTime.Now:yyyy-MM-dd HH:mm}");
        W($"exe:  {_exe}");
        W($"work: {_work}");
        W();

        string originals = Path.Combine(_work, "originals");
        MakeTestImages(originals);

        foreach (int size in Sizes)
            RunMixedSet(originals, size);

        RunOrientationMatrix();
        RunManyFiles(55, 1200);
        RunTwoBatches(interleaved: true);
        RunTwoBatches(interleaved: false);
        RunSendTo(originals, 1600);
        RunRecycleBinWithUndo(originals, 1600);
        RunOriginalsFolderUndo(originals, 2400);
        RunSaveCopy(originals, 1600);
        RunSaveCopyUndo(originals, 1200);

        W();
        W($"==== {_checks - _failures} of {_checks} checks passed, {_failures} failed ====");
        File.WriteAllText(Path.Combine(_work, "results.txt"), Log.ToString());
        return _failures == 0 ? 0 : 1;
    }

    // ================================================================ test cases

    private static void RunMixedSet(string originals, int size)
    {
        W($"#### Mixed set at {size} px — one process per file (Explorer style)");
        string dir = FreshCopy(originals, $"run-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        var before = files.ToDictionary(f => f, Inspect);
        string report = Path.Combine(_work, $"report-{size}.jsonl");

        var sw = Stopwatch.StartNew();
        WaitAll(LaunchPerFile(size, report, files));
        var batches = ReadReport(report);
        W($"  {files.Count} processes finished in {sw.Elapsed.TotalSeconds:0.0} s; batches reported: {batches.Count}");
        Check(batches.Count == 1, "exactly one batch (one result window)");
        if (batches.Count == 0) return;
        var b = batches[0];
        Check(b.MaxSize == size, $"batch size is {size}");
        Check(b.Results.Count == files.Count, $"batch has all {files.Count} files");

        PrintTable(files, before, b);
        CheckOutcomes(originals, files, before, b, size);
        CheckNoTempFiles(dir);

        W();
        W("  Summary window text:");
        foreach (var line in b.Summary.Split('\n'))
            W("  | " + line.TrimEnd('\r'));
        W();
    }

    private static void CheckOutcomes(string originals, List<string> files, Dictionary<string, Info> before, Batch b, int size)
    {
        foreach (string f in files)
        {
            string name = Path.GetFileName(f);
            var r = b.Results.FirstOrDefault(x => x.Path.Equals(f, StringComparison.OrdinalIgnoreCase));
            if (r == null) { Check(false, $"{name}: present in report"); continue; }
            var after = Inspect(f);
            var orig = Path.Combine(originals, name);

            switch (name)
            {
                case "landscape.jpg":
                    Check(r.Status == "Shrunk", $"{name}: shrunk");
                    Check(after.Width == size && after.Height == (int)Math.Round(4000.0 * size / 6000), $"{name}: {after.Width}x{after.Height} has long edge {size}");
                    Check(after.Format == "Jpeg", $"{name}: still JPEG");
                    CheckDates(f, name);
                    CheckBackup(r, orig, name);
                    break;
                case "portrait_exif6.jpg":
                    Check(r.Status == "Shrunk", $"{name}: shrunk");
                    Check(after.Height == size && after.Width == (int)Math.Round(3000.0 * size / 4000), $"{name}: portrait {after.Width}x{after.Height} after rotation");
                    Check(after.Orientation == 0, $"{name}: no orientation tag left (pixels are upright)");
                    Check(IsUpright(f), $"{name}: red TOP bar is at the top, arrow points up");
                    CheckDates(f, name);
                    CheckBackup(r, orig, name);
                    break;
                case "transparent.png":
                    if (r.Status == "Shrunk")
                    {
                        Check(after.Format == "Png", $"{name}: still PNG");
                        Check(Math.Max(after.Width, after.Height) == size, $"{name}: long edge {size}");
                        Check(CornerIsTransparent(f), $"{name}: transparency kept (corner alpha = 0, centre opaque)");
                        CheckDates(f, name);
                        CheckBackup(r, orig, name);
                    }
                    else
                    {
                        Check(r.Status == "SkippedNotSmaller", $"{name}: shrunk, or skipped as not ≥10% smaller (got {r.Status})");
                        Check(SameBytes(f, orig), $"{name}: untouched");
                    }
                    break;
                case "still.gif":
                    Check(r.Status == "SkippedGif", $"{name}: skipped as GIF");
                    Check(SameBytes(f, orig), $"{name}: untouched");
                    break;
                case "small.jpg":
                    Check(r.Status == "SkippedAlreadySmall", $"{name}: skipped as already small");
                    Check(SameBytes(f, orig), $"{name}: untouched");
                    break;
                case "corrupt.jpg":
                    Check(r.Status == "Failed", $"{name}: reported as failed ({r.Reason})");
                    Check(SameBytes(f, orig), $"{name}: untouched");
                    break;
                case "other.bmp":
                    Check(r.Status == "SkippedUnsupported", $"{name}: skipped as unsupported type");
                    Check(SameBytes(f, orig), $"{name}: untouched");
                    break;
            }
        }
    }

    private static void RunOrientationMatrix()
    {
        W("#### EXIF orientation 1–8 (coloured quadrants, must all come out upright)");
        string dir = Path.Combine(_work, "orientation");
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        for (int o = 1; o <= 8; o++)
        {
            using var upright = Quadrants(1600, 1200, seed: o);
            using var stored = (Bitmap)upright.Clone();
            stored.RotateFlip(InverseOf(o));
            SetOrientation(stored, o);
            string f = Path.Combine(dir, $"orient{o}.jpg");
            SaveJpeg(stored, f, 95);
            files.Add(f);
        }
        foreach (var f in files) W($"  stored {Path.GetFileName(f)}: {Inspect(f)}");

        string report = Path.Combine(_work, "report-orientation.jsonl");
        WaitAll(LaunchPerFile(1200, report, files));
        var batches = ReadReport(report);
        Check(batches.Count == 1, "one batch");
        foreach (var f in files)
        {
            var a = Inspect(f);
            string q = QuadrantColours(f);
            Check(a.Width == 1200 && a.Height == 900 && q == "R G / B Y",
                $"{Path.GetFileName(f)} → {a.Width}x{a.Height}, quadrants {q} (want 1200x900, R G / B Y)");
        }
        W();
    }

    private static void RunManyFiles(int count, int size)
    {
        W($"#### {count} files selected at once, {size} px — {count} processes");
        string dir = Path.Combine(_work, "many");
        Directory.CreateDirectory(dir);
        string seedFile = Path.Combine(dir, "photo_000.jpg");
        using (var bmp = Photo(2400, 1600, seed: 99, label: "MANY"))
            SaveJpeg(bmp, seedFile, 92);
        var files = new List<string> { seedFile };
        for (int i = 1; i < count; i++)
        {
            string f = Path.Combine(dir, $"photo_{i:000}.jpg");
            File.Copy(seedFile, f);
            files.Add(f);
        }

        string report = Path.Combine(_work, "report-many.jsonl");
        var sw = Stopwatch.StartNew();
        WaitAll(LaunchPerFile(size, report, files));
        var batches = ReadReport(report);
        W($"  {count} processes finished in {sw.Elapsed.TotalSeconds:0.0} s; batches: {batches.Count}" +
          (batches.Count > 0 ? $", files in first batch: {batches[0].Results.Count}" : ""));
        Check(batches.Count == 1, "exactly one batch");
        Check(batches.Sum(x => x.Results.Count) == count, $"all {count} files reported once");
        Check(batches.SelectMany(x => x.Results).All(r => r.Status == "Shrunk"), "all shrunk");
        Check(files.All(f => Inspect(f).Width == size), $"all files now {size} px wide");
        if (batches.Count > 0)
        {
            var lines = batches[0].Summary.Split('\n');
            W("  Summary window (first lines):");
            foreach (var l in lines.Take(8)) W("  | " + l.TrimEnd('\r'));
            W("  | …");
        }
        W();
    }

    private static void RunTwoBatches(bool interleaved)
    {
        string how = interleaved ? "interleaved" : "back to back";
        W($"#### Two batches at different sizes started {how}: 25 files @1200 and 25 files @1600");
        string tag = interleaved ? "x" : "y";
        string dirA = Path.Combine(_work, $"two-{tag}-1200");
        string dirB = Path.Combine(_work, $"two-{tag}-1600");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        string seedFile = Path.Combine(_work, $"two-{tag}-seed.jpg");
        using (var bmp = Photo(3000, 2000, seed: 7, label: "TWO"))
            SaveJpeg(bmp, seedFile, 92);
        var a = Enumerable.Range(0, 25).Select(i => Path.Combine(dirA, $"a_{i:00}.jpg")).ToList();
        var b = Enumerable.Range(0, 25).Select(i => Path.Combine(dirB, $"b_{i:00}.jpg")).ToList();
        foreach (var f in a.Concat(b)) File.Copy(seedFile, f);

        string repA = Path.Combine(_work, $"report-two-{tag}-1200.jsonl");
        string repB = Path.Combine(_work, $"report-two-{tag}-1600.jsonl");
        var procs = new List<Process>();
        if (interleaved)
        {
            for (int i = 0; i < 25; i++)
            {
                procs.AddRange(LaunchPerFile(1200, repA, new[] { a[i] }));
                procs.AddRange(LaunchPerFile(1600, repB, new[] { b[i] }));
            }
        }
        else
        {
            procs.AddRange(LaunchPerFile(1200, repA, a));
            procs.AddRange(LaunchPerFile(1600, repB, b));
        }
        WaitAll(procs);

        var ba = ReadReport(repA);
        var bb = ReadReport(repB);
        W($"  1200 batches: {ba.Count} ({string.Join(", ", ba.Select(x => x.Results.Count + " files"))}); " +
          $"1600 batches: {bb.Count} ({string.Join(", ", bb.Select(x => x.Results.Count + " files"))})");
        Check(ba.Count == 1 && ba[0].MaxSize == 1200, "one 1200 px batch");
        Check(bb.Count == 1 && bb[0].MaxSize == 1600, "one 1600 px batch");
        Check(ba.SelectMany(x => x.Results).Select(r => r.Path).OrderBy(p => p).SequenceEqual(a.OrderBy(p => p), StringComparer.OrdinalIgnoreCase),
            "1200 batch holds exactly the 25 '1200' files");
        Check(bb.SelectMany(x => x.Results).Select(r => r.Path).OrderBy(p => p).SequenceEqual(b.OrderBy(p => p), StringComparer.OrdinalIgnoreCase),
            "1600 batch holds exactly the 25 '1600' files");
        Check(a.All(f => Inspect(f).Width == 1200), "all '1200' files are 1200 px wide");
        Check(b.All(f => Inspect(f).Width == 1600), "all '1600' files are 1600 px wide");
        W();
    }

    private static void RunSendTo(string originals, int size)
    {
        W($"#### Send To style: one process with all files at {size} px");
        string dir = FreshCopy(originals, $"sendto-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        var before = files.ToDictionary(f => f, Inspect);
        string report = Path.Combine(_work, "report-sendto.jsonl");
        WaitAll(LaunchOne(size, report, files));
        var batches = ReadReport(report);
        Check(batches.Count == 1 && batches[0].Results.Count == files.Count, $"one batch with all {files.Count} files");
        if (batches.Count == 1)
        {
            PrintTable(files, before, batches[0]);
            CheckOutcomes(originals, files, before, batches[0], size);
        }
        W();
    }

    // ---------------------------------------------------------------- modes and undo

    private static void RunRecycleBinWithUndo(string originals, int size)
    {
        W($"#### Recycle Bin mode at {size} px, then Undo");
        string dir = FreshCopy(originals, $"recycle-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        string report = Path.Combine(_work, "report-recycle.jsonl");
        WaitAll(LaunchPerFile(size, report, files, "recycle", testUndo: true));
        var batches = ReadReport(report);
        var undo = ReadUndo(report);
        Check(batches.Count == 1, "one batch");
        if (batches.Count != 1) return;
        var shrunk = batches[0].Results.Where(r => r.Status == "Shrunk").ToList();
        W($"  shrunk {shrunk.Count}; originals sent to:");
        foreach (var r in shrunk)
            W($"    {Path.GetFileName(r.Path),-20} {r.Disposition,-16} {r.BackupPath}");
        Check(shrunk.Count >= 2, "at least the two JPEGs were shrunk");
        Check(shrunk.All(r => r.Disposition == "RecycleBin"), "every original went to the Recycle Bin (no fallback on this drive)");
        Check(shrunk.All(r => r.BackupPath != null && r.InfoPath != null &&
                              Path.GetFileName(r.BackupPath).StartsWith("$R") && Path.GetFileName(r.InfoPath).StartsWith("$I")),
            "each original was found in the Recycle Bin ($R data + $I info file)");
        PrintSummary(batches[0]);
        Check(undo.Count == shrunk.Count && undo.All(u => u.Ok), $"undo: {undo.Count(u => u.Ok)} of {shrunk.Count} put back");
        foreach (var u in undo) W($"    undo {Path.GetFileName(u.Path),-20} {(u.Ok ? "ok" : "FAIL")}  {u.Message}");
        Check(files.All(f => SameBytes(f, Path.Combine(originals, Path.GetFileName(f)))), "after undo every file is byte-identical to the original");
        Check(shrunk.All(r => !File.Exists(r.BackupPath!) && !File.Exists(r.InfoPath!)), "the Recycle Bin entries are gone (nothing left behind)");
        CheckDatesAll(files);
        CheckNoTempFiles(dir);
        W();
    }

    private static void RunOriginalsFolderUndo(string originals, int size)
    {
        W($"#### Originals-folder mode at {size} px, then Undo");
        string dir = FreshCopy(originals, $"originals-undo-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        string report = Path.Combine(_work, "report-originals-undo.jsonl");
        WaitAll(LaunchPerFile(size, report, files, "originals", testUndo: true));
        var batches = ReadReport(report);
        var undo = ReadUndo(report);
        int shrunk = batches.Count == 1 ? batches[0].Results.Count(r => r.Status == "Shrunk") : -1;
        Check(batches.Count == 1 && shrunk >= 2, $"one batch, {shrunk} shrunk");
        Check(undo.Count == shrunk && undo.All(u => u.Ok), $"undo: {undo.Count(u => u.Ok)} of {shrunk} put back");
        Check(files.All(f => SameBytes(f, Path.Combine(originals, Path.GetFileName(f)))), "after undo every file is byte-identical to the original");
        Check(!Directory.Exists(Path.Combine(dir, "Originals")), "the emptied Originals folder was removed");
        CheckDatesAll(files);
        CheckNoTempFiles(dir);
        W();
    }

    private static void RunSaveCopy(string originals, int size)
    {
        W($"#### Save-a-copy mode at {size} px");
        string dir = FreshCopy(originals, $"copy-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        string report = Path.Combine(_work, "report-copy.jsonl");
        WaitAll(LaunchPerFile(size, report, files, "copy"));
        var batches = ReadReport(report);
        Check(batches.Count == 1, "one batch");
        if (batches.Count != 1) return;
        Check(files.All(f => SameBytes(f, Path.Combine(originals, Path.GetFileName(f)))), "every original is byte-identical (untouched)");
        var shrunk = batches[0].Results.Where(r => r.Status == "Shrunk").ToList();
        W($"  {"copy",-28} {"size/dims",-24}");
        foreach (var r in shrunk)
        {
            string copy = r.OutputPath ?? "";
            var info = File.Exists(copy) ? Inspect(copy) : new Info(0, 0, 0, 0, "missing");
            W($"  {Path.GetFileName(copy),-28} {info.Short(),-24}");
            string expect = Path.GetFileNameWithoutExtension(r.Path) + $" ({size})" + Path.GetExtension(r.Path);
            Check(r.Disposition == "KeptInPlace" && Path.GetFileName(copy) == expect && Math.Max(info.Width, info.Height) == size,
                $"{expect}: saved beside the original, long edge {size}");
            CheckDates(copy, Path.GetFileName(copy));
        }
        var portraitCopy = Path.Combine(dir, $"portrait_exif6 ({size}).jpg");
        Check(File.Exists(portraitCopy) && IsUpright(portraitCopy), "portrait copy is upright");
        int extra = Directory.GetFiles(dir).Length - files.Count;
        Check(extra == shrunk.Count, $"only the {shrunk.Count} shrunk copies were added (found {extra} new files)");
        PrintSummary(batches[0]);
        W();
    }

    private static void RunSaveCopyUndo(string originals, int size)
    {
        W($"#### Save-a-copy mode at {size} px, then Undo");
        string dir = FreshCopy(originals, $"copy-undo-{size}");
        var files = Directory.GetFiles(dir).OrderBy(f => f).ToList();
        string report = Path.Combine(_work, "report-copy-undo.jsonl");
        WaitAll(LaunchPerFile(size, report, files, "copy", testUndo: true));
        var batches = ReadReport(report);
        var undo = ReadUndo(report);
        int shrunk = batches.Count == 1 ? batches[0].Results.Count(r => r.Status == "Shrunk") : -1;
        Check(undo.Count == shrunk && shrunk >= 2 && undo.All(u => u.Ok), $"undo: {undo.Count(u => u.Ok)} of {shrunk} copies deleted");
        Check(Directory.GetFiles(dir).Length == files.Count, "folder is back to the original files only");
        Check(files.All(f => SameBytes(f, Path.Combine(originals, Path.GetFileName(f)))), "originals untouched");
        W();
    }

    private static void CheckDatesAll(List<string> files)
    {
        bool ok = files.All(f => Math.Abs((File.GetLastWriteTime(f) - Modified).TotalSeconds) < 2);
        Check(ok, "modified dates are the originals' again");
    }

    private static void PrintSummary(Batch b)
    {
        W("  Summary window text:");
        foreach (var line in b.Summary.Split('\n'))
            W("  | " + line.TrimEnd('\r'));
    }

    // ================================================================ process helpers

    private static List<Process> LaunchPerFile(int size, string report, IEnumerable<string> files,
        string mode = "originals", bool testUndo = false)
    {
        var list = new List<Process>();
        foreach (var f in files)
            list.AddRange(LaunchOne(size, report, new[] { f }, mode, testUndo));
        return list;
    }

    private static List<Process> LaunchOne(int size, string report, IEnumerable<string> files,
        string mode = "originals", bool testUndo = false)
    {
        var psi = new ProcessStartInfo(_exe) { UseShellExecute = false };
        psi.ArgumentList.Add("--max");
        psi.ArgumentList.Add(size.ToString());
        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add(mode);
        if (testUndo) psi.ArgumentList.Add("--test-undo");
        psi.ArgumentList.Add("--report");
        psi.ArgumentList.Add(report);
        foreach (var f in files) psi.ArgumentList.Add(f);
        return new List<Process> { Process.Start(psi)! };
    }

    private static void WaitAll(List<Process> procs)
    {
        var deadline = DateTime.UtcNow.AddMinutes(4);
        foreach (var p in procs)
        {
            int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!p.WaitForExit(left))
            {
                Check(false, $"process {p.Id} still running after 4 minutes");
                try { p.Kill(); } catch { }
            }
            p.Dispose();
        }
    }

    // ================================================================ report

    private sealed record Res(string Path, string Status, string? Reason, long OldBytes, long NewBytes,
        string Disposition, string? OutputPath, string? BackupPath, string? InfoPath);
    private sealed record Batch(int MaxSize, string Mode, List<Res> Results, string Summary);
    private sealed record UndoRes(string Path, bool Ok, string Message);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<UndoRes> ReadUndo(string path)
    {
        var list = new List<UndoRes>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path).Where(l => l.Trim().Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("undo", out _)) continue;
            foreach (var e in doc.RootElement.GetProperty("results").EnumerateArray())
                list.Add(new UndoRes(Str(e, "Path")!, e.GetProperty("Ok").GetBoolean(), Str(e, "Message") ?? ""));
        }
        return list;
    }

    private static List<Batch> ReadReport(string path)
    {
        var list = new List<Batch>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path).Where(l => l.Trim().Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("undo", out _)) continue;
            var results = root.GetProperty("results").EnumerateArray().Select(e => new Res(
                Str(e, "Path")!,
                Str(e, "Status")!,
                Str(e, "Reason"),
                e.GetProperty("OldBytes").GetInt64(),
                e.GetProperty("NewBytes").GetInt64(),
                Str(e, "Disposition") ?? "",
                Str(e, "OutputPath"),
                Str(e, "OriginalBackupPath"),
                Str(e, "RecycleInfoPath"))).ToList();
            list.Add(new Batch(root.GetProperty("maxSize").GetInt32(), Str(root, "mode") ?? "",
                results, Str(root, "summary") ?? ""));
        }
        return list;
    }

    private static void PrintTable(List<string> files, Dictionary<string, Info> before, Batch b)
    {
        W($"  {"file",-20} {"before",-24} {"after",-24} result");
        foreach (var f in files)
        {
            var r = b.Results.FirstOrDefault(x => x.Path.Equals(f, StringComparison.OrdinalIgnoreCase));
            var a = Inspect(f);
            string res = r == null ? "(missing)" : r.Status + (r.Reason != null ? " — " + r.Reason : "");
            W($"  {Path.GetFileName(f),-20} {before[f].Short(),-24} {a.Short(),-24} {res}");
        }
    }

    // ================================================================ checks

    private static void W(string s = "")
    {
        Console.WriteLine(s);
        Log.AppendLine(s);
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) _failures++;
        W((ok ? "  ok    " : "  FAIL  ") + what);
    }

    private static void CheckDates(string f, string name)
    {
        var c = File.GetCreationTime(f);
        var m = File.GetLastWriteTime(f);
        Check(Math.Abs((c - Created).TotalSeconds) < 2 && Math.Abs((m - Modified).TotalSeconds) < 2,
            $"{name}: dates kept (created {c:yyyy-MM-dd HH:mm:ss}, modified {m:yyyy-MM-dd HH:mm:ss})");
    }

    private static void CheckBackup(Res r, string orig, string name)
    {
        Check(r.Disposition == "OriginalsFolder" && r.BackupPath != null && File.Exists(r.BackupPath) && SameBytes(r.BackupPath, orig),
            $"{name}: original kept unchanged in {(r.BackupPath != null ? Path.GetFileName(Path.GetDirectoryName(r.BackupPath)) + "\\" : "?")}");
    }

    private static void CheckNoTempFiles(string dir)
    {
        var tmp = Directory.GetFiles(dir, "*.photoshrink*.tmp", SearchOption.TopDirectoryOnly);
        Check(tmp.Length == 0, "no temp files left behind");
    }

    private static bool SameBytes(string a, string b) => File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private sealed record Info(long Bytes, int Width, int Height, int Orientation, string Format)
    {
        public string Short() => Width > 0
            ? $"{FormatBytes(Bytes)} {Width}x{Height}" + (Orientation > 1 ? $" o{Orientation}" : "")
            : $"{FormatBytes(Bytes)} {Format}";
        public override string ToString() => Short() + " " + Format;
    }

    private static Info Inspect(string f)
    {
        byte[] bytes = File.ReadAllBytes(f);
        try
        {
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            int o = 0;
            if (img.PropertyIdList.Contains(0x0112))
                o = BitConverter.ToUInt16(img.GetPropertyItem(0x0112)!.Value!, 0);
            string fmt = img.RawFormat.Guid == ImageFormat.Jpeg.Guid ? "Jpeg"
                : img.RawFormat.Guid == ImageFormat.Png.Guid ? "Png"
                : img.RawFormat.Guid == ImageFormat.Gif.Guid ? "Gif"
                : img.RawFormat.Guid == ImageFormat.Bmp.Guid ? "Bmp" : "Other";
            return new Info(bytes.Length, img.Width, img.Height, o, fmt);
        }
        catch
        {
            return new Info(bytes.Length, 0, 0, 0, "unreadable");
        }
    }

    private static Bitmap Load(string f)
    {
        using var ms = new MemoryStream(File.ReadAllBytes(f));
        using var img = Image.FromStream(ms);
        return new Bitmap(img);
    }

    private static bool IsUpright(string f)
    {
        using var bmp = Load(f);
        // The upright test picture has a red bar across the top 6 % and black arrow tip near the top centre.
        var top = bmp.GetPixel(bmp.Width / 2, (int)(bmp.Height * 0.03));
        var bottom = bmp.GetPixel(bmp.Width / 2, (int)(bmp.Height * 0.97));
        bool redTop = top.R > 180 && top.G < 80 && top.B < 80;
        bool redBottom = bottom.R > 180 && bottom.G < 80 && bottom.B < 80;
        return redTop && !redBottom;
    }

    private static bool CornerIsTransparent(string f)
    {
        using var ms = new MemoryStream(File.ReadAllBytes(f));
        using var img = (Bitmap)Image.FromStream(ms);
        var corner = img.GetPixel(2, 2);
        var centre = img.GetPixel(img.Width / 2, img.Height / 2);
        return corner.A == 0 && centre.A == 255;
    }

    private static string QuadrantColours(string f)
    {
        using var bmp = Load(f);
        string C(int x, int y)
        {
            var c = bmp.GetPixel(x, y);
            if (c.R > 150 && c.G < 100 && c.B < 100) return "R";
            if (c.G > 150 && c.R < 100 && c.B < 100) return "G";
            if (c.B > 150 && c.R < 100 && c.G < 100) return "B";
            if (c.R > 150 && c.G > 150 && c.B < 100) return "Y";
            return "?";
        }
        int w = bmp.Width, h = bmp.Height;
        return $"{C(w / 4, h / 4)} {C(3 * w / 4, h / 4)} / {C(w / 4, 3 * h / 4)} {C(3 * w / 4, 3 * h / 4)}";
    }

    // ================================================================ test images

    private static string FreshCopy(string originals, string name)
    {
        string dir = Path.Combine(_work, name);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(originals))
        {
            string dst = Path.Combine(dir, Path.GetFileName(f));
            File.Copy(f, dst);
            File.SetCreationTime(dst, Created);
            File.SetLastWriteTime(dst, Modified);
        }
        return dir;
    }

    private static void MakeTestImages(string dir)
    {
        Directory.CreateDirectory(dir);
        W("Making test images…");

        using (var bmp = Photo(6000, 4000, seed: 1, label: "LANDSCAPE"))
            SaveJpeg(bmp, Path.Combine(dir, "landscape.jpg"), 95);

        // Portrait photo stored sideways, as phones do: pixels are landscape, EXIF orientation 6 says
        // "rotate 90° clockwise to view".
        using (var upright = UprightPortrait(3000, 4000))
        {
            upright.Save(Path.Combine(_work, "portrait_upright_reference.png"), ImageFormat.Png);
            using var stored = (Bitmap)upright.Clone();
            stored.RotateFlip(InverseOf(6));
            SetOrientation(stored, 6);
            SaveJpeg(stored, Path.Combine(dir, "portrait_exif6.jpg"), 95);
        }

        using (var png = TransparentPng(3000, 2000))
            png.Save(Path.Combine(dir, "transparent.png"), ImageFormat.Png);

        using (var gif = Photo(2000, 1500, seed: 3, label: "GIF", noise: 0))
            gif.Save(Path.Combine(dir, "still.gif"), ImageFormat.Gif);

        using (var small = Photo(800, 600, seed: 4, label: "SMALL"))
            SaveJpeg(small, Path.Combine(dir, "small.jpg"), 90);

        var rnd = new Random(5);
        var junk = new byte[20000];
        rnd.NextBytes(junk);
        junk[0] = 0xFF; junk[1] = 0xD8; junk[2] = 0xFF; junk[3] = 0xE0; // looks like a JPEG, isn't
        File.WriteAllBytes(Path.Combine(dir, "corrupt.jpg"), junk);

        using (var bmp = Photo(2000, 1500, seed: 6, label: "BMP", noise: 0))
            bmp.Save(Path.Combine(dir, "other.bmp"), ImageFormat.Bmp);

        foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
            W($"  {Path.GetFileName(f),-20} {Inspect(f)}");
        W();
    }

    private static Bitmap Photo(int w, int h, int seed, string label, int noise = 10)
    {
        var rnd = new Random(seed);
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.SkyBlue, Color.DarkOliveGreen, 90f))
                g.FillRectangle(sky, 0, 0, w, h);
            for (int i = 0; i < 400; i++)
            {
                using var br = new SolidBrush(Color.FromArgb(110, rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                g.FillEllipse(br, rnd.Next(-w / 10, w), rnd.Next(-h / 10, h), rnd.Next(w / 60, w / 5), rnd.Next(h / 60, h / 5));
            }
            using var font = new Font("Arial", Math.Max(12, h / 12f), FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(label, font, Brushes.White, w * 0.05f, h * 0.4f);
        }
        if (noise > 0) AddNoise(bmp, rnd, noise);
        return bmp;
    }

    private static Bitmap UprightPortrait(int w, int h)
    {
        var bmp = Photo(w, h, seed: 2, label: "");
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Red, 0, 0, w, (int)(h * 0.06));
            using var font = new Font("Arial", h / 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            var fmt = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString("TOP", font, Brushes.White, new RectangleF(0, h * 0.09f, w, h * 0.1f), fmt);
            // Big arrow pointing up.
            float cx = w / 2f;
            var arrow = new[]
            {
                new PointF(cx, h * 0.22f), new PointF(cx + w * 0.25f, h * 0.45f), new PointF(cx + w * 0.09f, h * 0.45f),
                new PointF(cx + w * 0.09f, h * 0.85f), new PointF(cx - w * 0.09f, h * 0.85f),
                new PointF(cx - w * 0.09f, h * 0.45f), new PointF(cx - w * 0.25f, h * 0.45f),
            };
            g.FillPolygon(Brushes.Black, arrow);
            g.DrawString("BOTTOM", new Font("Arial", h / 24f, FontStyle.Bold, GraphicsUnit.Pixel), Brushes.White,
                new RectangleF(0, h * 0.89f, w, h * 0.08f), fmt);
        }
        return bmp;
    }

    private static Bitmap TransparentPng(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rnd = new Random(8);
        for (int i = 0; i < 60; i++)
        {
            using var br = new SolidBrush(Color.FromArgb(rnd.Next(60, 200), rnd.Next(256), rnd.Next(256), rnd.Next(256)));
            int d = rnd.Next(h / 10, h / 3);
            g.FillEllipse(br, rnd.Next(w / 10, w - d - w / 10), rnd.Next(h / 10, h - d - h / 10), d, d);
        }
        // Opaque logo-like block in the centre.
        using var path = new GraphicsPath();
        path.AddEllipse(w / 2 - h / 5, h / 2 - h / 5, 2 * h / 5, 2 * h / 5);
        using var pgb = new PathGradientBrush(path) { CenterColor = Color.Gold, SurroundColors = new[] { Color.OrangeRed } };
        g.FillPath(pgb, path);
        return bmp;
    }

    private static Bitmap Quadrants(int w, int h, int seed)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.FillRectangle(Brushes.Red, 0, 0, w / 2, h / 2);
            g.FillRectangle(Brushes.Lime, w / 2, 0, w - w / 2, h / 2);
            g.FillRectangle(Brushes.Blue, 0, h / 2, w / 2, h - h / 2);
            g.FillRectangle(Brushes.Yellow, w / 2, h / 2, w - w / 2, h - h / 2);
        }
        AddNoise(bmp, new Random(seed), 25);
        return bmp;
    }

    private static void AddNoise(Bitmap bmp, Random rnd, int amount)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int len = data.Stride * data.Height;
            var buf = new byte[len];
            Marshal.Copy(data.Scan0, buf, 0, len);
            for (int i = 0; i < len; i++)
            {
                int v = buf[i] + rnd.Next(-amount, amount + 1);
                buf[i] = (byte)Math.Clamp(v, 0, 255);
            }
            Marshal.Copy(buf, 0, data.Scan0, len);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static void SaveJpeg(Image img, string path, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        img.Save(path, codec, ep);
    }

    private static void SetOrientation(Image img, int orientation)
    {
        // PropertyItem has no public constructor; this is the usual way to make one.
        var pi = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        pi.Id = 0x0112;
        pi.Type = 3; // SHORT
        pi.Len = 2;
        pi.Value = BitConverter.GetBytes((ushort)orientation);
        img.SetPropertyItem(pi);
    }

    /// <summary>The RotateFlip that turns an upright picture into what a camera stores with orientation o.</summary>
    private static RotateFlipType InverseOf(int o)
    {
        RotateFlipType fix = o switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
        // Brute force: find T such that applying T then fix gives back the original.
        using var probe = new Bitmap(3, 2);
        int n = 0;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) probe.SetPixel(x, y, Color.FromArgb(255, 40 * ++n, 0, 0));
        foreach (RotateFlipType t in new[]
                 {
                     RotateFlipType.RotateNoneFlipNone, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate180FlipNone,
                     RotateFlipType.Rotate270FlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate90FlipX,
                     RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate270FlipX,
                 })
        {
            using var b = (Bitmap)probe.Clone();
            b.RotateFlip(t);
            b.RotateFlip(fix);
            bool same = b.Width == 3 && b.Height == 2;
            for (int y = 0; same && y < 2; y++)
                for (int x = 0; same && x < 3; x++)
                    same = b.GetPixel(x, y).ToArgb() == probe.GetPixel(x, y).ToArgb();
            if (same) return t;
        }
        throw new InvalidOperationException("no inverse for orientation " + o);
    }

    private static string FormatBytes(long b) =>
        b >= 1024 * 1024 ? $"{b / 1048576.0:0.0} MB" : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";
}
