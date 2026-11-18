using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoShrink;

/// <summary>User settings, stored in %APPDATA%\PhotoShrink\settings.json.</summary>
internal sealed class Settings
{
    public const int DefaultJpegQuality = 85;
    public const int DefaultQuietPeriodMs = 1500;

    /// <summary>JPEG quality, 1–100.</summary>
    public int JpegQuality { get; set; } = DefaultJpegQuality;

    /// <summary>
    /// How long the first instance waits with no new files arriving before it starts.
    /// Explorer starts one process per selected file; this gathers them into one batch.
    /// </summary>
    public int QuietPeriodMs { get; set; } = DefaultQuietPeriodMs;

    /// <summary>What happens to the original photo.</summary>
    public ShrinkMode Mode { get; set; } = ShrinkMode.RecycleBin;

    /// <summary>Show the "Shrink N photos?" dialog before starting.</summary>
    public bool ConfirmBeforeShrinking { get; set; } = true;

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoShrink");

    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                    return s.Normalized();
            }
        }
        catch
        {
            // A damaged settings file must never stop shrinking; fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Normalized(), JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private Settings Normalized()
    {
        JpegQuality = Math.Clamp(JpegQuality, 1, 100);
        QuietPeriodMs = Math.Clamp(QuietPeriodMs, 250, 10000);
        if (!Enum.IsDefined(Mode)) Mode = ShrinkMode.RecycleBin;
        return this;
    }
}
