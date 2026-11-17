using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PhotoShrink;

internal static class Format
{
    public static string Bytes(long b)
    {
        var c = CultureInfo.InvariantCulture;
        if (b >= 1024L * 1024 * 1024) return (b / (1024.0 * 1024 * 1024)).ToString("0.00", c) + " GB";
        if (b >= 1024L * 1024) return (b / (1024.0 * 1024)).ToString("0.0", c) + " MB";
        if (b >= 1024) return (b / 1024.0).ToString("0", c) + " KB";
        return b + " bytes";
    }

    public static string Dims(int w, int h) => $"{w}x{h}";
}

/// <summary>Builds the end-of-batch summary text and the optional machine-readable report.</summary>
internal static class Summary
{
    public static string BuildText(IReadOnlyList<ShrinkResult> results, int maxSize, TimeSpan elapsed)
    {
        var shrunk = results.Where(r => r.Status == ShrinkStatus.Shrunk).ToList();
        var skipped = results.Where(r => r.IsSkipped).ToList();
        var failed = results.Where(r => r.Status == ShrinkStatus.Failed).ToList();
        long saved = shrunk.Sum(r => r.OldBytes - r.NewBytes);

        // Show bare file names when everything came from one folder, full paths otherwise.
        var folders = results.Select(r => Path.GetDirectoryName(r.Path) ?? "")
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool oneFolder = folders.Count == 1;
        string Name(ShrinkResult r) => oneFolder ? Path.GetFileName(r.Path) : r.Path;
        int nameWidth = Math.Min(48, results.Select(r => Name(r).Length).DefaultIfEmpty(10).Max());
        string Pad(string s) => s.Length > nameWidth ? s[..(nameWidth - 1)] + "…" : s.PadRight(nameWidth);

        var sb = new StringBuilder();
        sb.AppendLine($"Shrink to {maxSize} px — {results.Count} file(s) in {elapsed.TotalSeconds:0.0} s");
        if (oneFolder) sb.AppendLine($"Folder: {folders[0]}");
        sb.AppendLine();
        sb.AppendLine($"Shrunk: {shrunk.Count}    Skipped: {skipped.Count}    Failed: {failed.Count}    Saved: {Format.Bytes(saved)}");
        sb.AppendLine();

        if (shrunk.Count > 0)
        {
            sb.AppendLine($"SHRUNK ({shrunk.Count})");
            foreach (var r in shrunk)
            {
                string rot = r.Orientation != 1 ? "  (rotated upright)" : "";
                sb.AppendLine($"  {Pad(Name(r))}  {Format.Bytes(r.OldBytes),9} -> {Format.Bytes(r.NewBytes),-9}" +
                              $"  {Format.Dims(r.OldWidth, r.OldHeight),11} -> {Format.Dims(r.NewWidth, r.NewHeight)}{rot}");
            }
            long oldTotal = shrunk.Sum(r => r.OldBytes), newTotal = shrunk.Sum(r => r.NewBytes);
            sb.AppendLine($"  Total: {Format.Bytes(oldTotal)} -> {Format.Bytes(newTotal)}, saved {Format.Bytes(saved)}" +
                          (oldTotal > 0 ? $" ({(double)saved / oldTotal:P0})" : ""));
            sb.AppendLine();
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine($"SKIPPED ({skipped.Count}) — left untouched");
            foreach (var r in skipped)
            {
                string dims = r.OldWidth > 0 ? $" ({Format.Dims(r.OldWidth, r.OldHeight)})" : "";
                sb.AppendLine($"  {Pad(Name(r))}  {r.Reason}{dims}");
            }
            sb.AppendLine();
        }

        if (failed.Count > 0)
        {
            sb.AppendLine($"FAILED ({failed.Count}) — left untouched");
            foreach (var r in failed)
                sb.AppendLine($"  {Pad(Name(r))}  {r.Reason}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Appends one JSON line describing the batch (used by the test kit).</summary>
    public static void AppendReport(string reportPath, IReadOnlyList<ShrinkResult> results, int maxSize, string text)
    {
        var line = JsonSerializer.Serialize(new
        {
            maxSize,
            processId = Environment.ProcessId,
            count = results.Count,
            results = results.Select(r => new
            {
                r.Path, Status = r.Status.ToString(), r.Reason, r.OldBytes, r.NewBytes,
                r.OldWidth, r.OldHeight, r.NewWidth, r.NewHeight, r.Orientation,
            }),
            summary = text,
        });

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(reportPath, FileMode.Append, FileAccess.Write, FileShare.None);
                using var w = new StreamWriter(fs, new UTF8Encoding(false));
                w.WriteLine(line);
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(100);
            }
        }
    }
}
