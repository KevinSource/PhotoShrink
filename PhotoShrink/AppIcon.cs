namespace PhotoShrink;

/// <summary>The app icon (all sizes), embedded in the assembly so each window can use the right size.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon?> Value = new(() =>
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("PhotoShrink.ico");
            return s == null ? null : new Icon(s);
        }
        catch
        {
            return null; // a missing icon is cosmetic; never fail over it
        }
    });

    /// <summary>Gives <paramref name="form"/> the PhotoShrink icon (title bar and taskbar).</summary>
    public static void Apply(Form form)
    {
        if (Value.Value is { } icon)
            form.Icon = icon;
    }
}
