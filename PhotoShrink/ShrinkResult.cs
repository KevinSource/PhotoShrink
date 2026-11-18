namespace PhotoShrink;

internal enum ShrinkStatus
{
    Shrunk,
    SkippedAlreadySmall,
    SkippedGif,
    SkippedNotSmaller,
    SkippedUnsupported,
    SkippedCancelled,
    Failed,
}

/// <summary>Where the original ended up after a successful shrink.</summary>
internal enum OriginalDisposition
{
    None,
    RecycleBin,
    OriginalsFolder,
    KeptInPlace, // SaveCopy mode
}

internal sealed class ShrinkResult
{
    public required string Path { get; init; }
    public ShrinkStatus Status { get; set; }
    public string? Reason { get; set; }
    public long OldBytes { get; set; }
    public long NewBytes { get; set; }
    public int OldWidth { get; set; }
    public int OldHeight { get; set; }
    public int NewWidth { get; set; }
    public int NewHeight { get; set; }

    /// <summary>EXIF orientation value that was applied (1 = none).</summary>
    public int Orientation { get; set; } = 1;

    /// <summary>Where the original went.</summary>
    public OriginalDisposition Disposition { get; set; }

    /// <summary>The shrunk file: the original's path, or the copy's path in SaveCopy mode.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Last-write time of the shrunk file as we left it; Undo won't touch it if it changed.</summary>
    public DateTime OutputWriteTimeUtc { get; set; }

    /// <summary>The original's new location: the file in the Originals folder, or its $R file in the Recycle Bin.</summary>
    public string? OriginalBackupPath { get; set; }

    /// <summary>The Recycle Bin's $I info file for the original (removed on Undo).</summary>
    public string? RecycleInfoPath { get; set; }

    /// <summary>Recycle Bin mode, but the drive has none, so the original went to the Originals folder.</summary>
    public bool FellBackToOriginalsFolder { get; set; }

    /// <summary>Anything unusual worth showing under the file in the summary.</summary>
    public string? Note { get; set; }

    public bool IsSkipped => Status is ShrinkStatus.SkippedAlreadySmall or ShrinkStatus.SkippedGif
        or ShrinkStatus.SkippedNotSmaller or ShrinkStatus.SkippedUnsupported or ShrinkStatus.SkippedCancelled;
}

internal sealed class UndoResult
{
    public required string Path { get; init; }
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}
