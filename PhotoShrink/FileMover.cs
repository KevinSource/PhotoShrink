namespace PhotoShrink;

/// <summary>
/// Puts a verified temp file in place according to the chosen <see cref="ShrinkMode"/>,
/// and undoes that later. All moves stay within one folder or drive, so each is a rename.
/// </summary>
internal static class FileMover
{
    // Serialises the "pick a free name, then move" steps across the parallel workers.
    private static readonly object Gate = new();

    /// <summary>SaveCopy: moves the temp file to "name (1600).ext" (or "name (1600) (2).ext" …). Returns the copy's path.</summary>
    public static string MoveToCopy(string tempPath, string originalPath, int maxSize)
    {
        string dir = Path.GetDirectoryName(originalPath)!;
        string stem = Path.GetFileNameWithoutExtension(originalPath);
        string ext = Path.GetExtension(originalPath);
        lock (Gate)
        {
            string candidate = Path.Combine(dir, $"{stem} ({maxSize}){ext}");
            for (int n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
                candidate = Path.Combine(dir, $"{stem} ({maxSize}) ({n}){ext}");
            File.Move(tempPath, candidate);
            return candidate;
        }
    }

    /// <summary>
    /// OriginalsFolder: moves the original into "Originals\" beside it, then the temp file into the
    /// original's place. If the second step fails, the original is moved back. Returns where the original went.
    /// </summary>
    public static string SwapViaOriginalsFolder(string tempPath, string originalPath)
    {
        string folder = Path.Combine(Path.GetDirectoryName(originalPath)!, ShrinkModeText.OriginalsFolderName);
        lock (Gate)
        {
            Directory.CreateDirectory(folder);
            string dest = UniquePath(Path.Combine(folder, Path.GetFileName(originalPath)));
            File.Move(originalPath, dest);
            try
            {
                File.Move(tempPath, originalPath);
            }
            catch
            {
                File.Move(dest, originalPath);
                throw;
            }
            return dest;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    // ---------------------------------------------------------------- undo

    /// <summary>Reverses every successful shrink in a batch. Never throws.</summary>
    public static List<UndoResult> Undo(IEnumerable<ShrinkResult> results) =>
        results.Where(r => r.Status == ShrinkStatus.Shrunk).Select(UndoOne).ToList();

    private static UndoResult UndoOne(ShrinkResult r)
    {
        var u = new UndoResult { Path = r.Path };
        try
        {
            string output = r.OutputPath ?? r.Path;
            var fi = new FileInfo(output);
            if (!fi.Exists || fi.Length != r.NewBytes || fi.LastWriteTimeUtc != r.OutputWriteTimeUtc)
            {
                u.Message = "the shrunk file was changed, moved or deleted since; left as it is";
                return u;
            }

            switch (r.Disposition)
            {
                case OriginalDisposition.KeptInPlace:
                    File.Delete(output);
                    u.Ok = true;
                    u.Message = "shrunk copy deleted";
                    return u;

                case OriginalDisposition.RecycleBin:
                case OriginalDisposition.OriginalsFolder:
                    bool fromBin = r.Disposition == OriginalDisposition.RecycleBin;
                    string? backup = r.OriginalBackupPath;
                    if (backup == null || !File.Exists(backup))
                    {
                        u.Message = fromBin
                            ? "the original is no longer in the Recycle Bin (restore it by hand if you still can)"
                            : "the original is no longer in the Originals folder";
                        return u;
                    }

                    // Move the shrunk file aside, bring the original back, then drop the shrunk file.
                    string aside = Path.Combine(Path.GetDirectoryName(output)!,
                        $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.photoshrink-undo.tmp");
                    File.Move(output, aside);
                    try
                    {
                        File.Move(backup, r.Path);
                    }
                    catch
                    {
                        File.Move(aside, output);
                        throw;
                    }
                    File.Delete(aside);

                    if (fromBin)
                    {
                        if (r.RecycleInfoPath != null)
                            try { File.Delete(r.RecycleInfoPath); } catch { /* leaves an empty Recycle Bin entry */ }
                    }
                    else
                    {
                        DeleteIfEmpty(Path.GetDirectoryName(backup)!);
                    }
                    u.Ok = true;
                    u.Message = fromBin ? "original restored from the Recycle Bin" : "original moved back from the Originals folder";
                    return u;

                default:
                    u.Message = "nothing to undo";
                    return u;
            }
        }
        catch (Exception ex)
        {
            u.Message = ex.Message;
            return u;
        }
    }

    private static void DeleteIfEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch
        {
            // in use or not empty; leave it
        }
    }
}
