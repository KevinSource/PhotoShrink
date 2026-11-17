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

    public bool IsSkipped => Status is ShrinkStatus.SkippedAlreadySmall or ShrinkStatus.SkippedGif
        or ShrinkStatus.SkippedNotSmaller or ShrinkStatus.SkippedUnsupported or ShrinkStatus.SkippedCancelled;
}
