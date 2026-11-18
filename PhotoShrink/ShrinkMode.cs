namespace PhotoShrink;

/// <summary>What happens to the original photo.</summary>
internal enum ShrinkMode
{
    /// <summary>The shrunk photo takes the original's place; the original goes to the Recycle Bin.</summary>
    RecycleBin,

    /// <summary>The shrunk photo takes the original's place; the original moves to an "Originals" subfolder.</summary>
    OriginalsFolder,

    /// <summary>The original is left alone; a shrunk copy "name (1600).jpg" is saved next to it.</summary>
    SaveCopy,
}

internal static class ShrinkModeText
{
    public const string OriginalsFolderName = "Originals";

    /// <summary>Label for the settings window.</summary>
    public static string Label(ShrinkMode mode) => mode switch
    {
        ShrinkMode.RecycleBin => "Replace the photo, send the original to the Recycle Bin",
        ShrinkMode.OriginalsFolder => $"Replace the photo, move the original to an \"{OriginalsFolderName}\" folder",
        _ => "Save a shrunk copy, leave the original alone",
    };

    /// <summary>One sentence for the confirmation dialog.</summary>
    public static string Confirm(ShrinkMode mode, int maxSize) => mode switch
    {
        ShrinkMode.RecycleBin =>
            "Each photo is replaced by its shrunk version. The original goes to the Recycle Bin.",
        ShrinkMode.OriginalsFolder =>
            $"Each photo is replaced by its shrunk version. The original moves to an \"{OriginalsFolderName}\" folder next to it.",
        _ =>
            $"Originals are left alone. Shrunk copies are saved next to them as \"name ({maxSize}).jpg\".",
    };

    /// <summary>Where the originals went, for the summary.</summary>
    public static string Summary(ShrinkMode mode, int maxSize) => mode switch
    {
        ShrinkMode.RecycleBin =>
            "Originals: in the Recycle Bin. Click Undo to put them back (or restore them from the Recycle Bin).",
        ShrinkMode.OriginalsFolder =>
            $"Originals: in an \"{OriginalsFolderName}\" folder next to the photos. Click Undo to put them back.",
        _ =>
            $"Originals: untouched. Shrunk copies were saved as \"name ({maxSize}).ext\". Undo deletes the copies.",
    };

    public static bool TryParse(string s, out ShrinkMode mode)
    {
        switch (s.ToLowerInvariant())
        {
            case "recycle": case "recyclebin": mode = ShrinkMode.RecycleBin; return true;
            case "originals": case "originalsfolder": mode = ShrinkMode.OriginalsFolder; return true;
            case "copy": case "savecopy": mode = ShrinkMode.SaveCopy; return true;
            default: mode = ShrinkMode.RecycleBin; return false;
        }
    }
}
