using System.Globalization;

namespace PhotoShrink;

internal sealed class CommandLine
{
    public const int MinSize = 16;
    public const int MaxAllowedSize = 30000;

    public int? MaxSize { get; private set; }
    public bool Install { get; private set; }
    public bool Uninstall { get; private set; }
    public bool Silent { get; private set; }
    public string? ReportPath { get; private set; }

    /// <summary>--mode recycle|originals|copy overrides the saved setting for this run.</summary>
    public ShrinkMode? Mode { get; private set; }

    /// <summary>--yes skips the confirmation dialog.</summary>
    public bool Yes { get; private set; }

    /// <summary>--test-undo (test kit only): undo the batch right after it finishes and report that too.</summary>
    public bool TestUndo { get; private set; }
    public List<string> Files { get; } = new();

    public const string Usage =
        "Usage:\n" +
        "  PhotoShrink.exe --max <pixels> [--mode recycle|originals|copy] [--yes] <file> [file …]\n" +
        "  PhotoShrink.exe --install | --uninstall [--silent]\n" +
        "  PhotoShrink.exe            (settings window)";

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var cl = new CommandLine();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--max":
                case "/max":
                    if (i + 1 >= args.Count)
                        throw new ArgumentException("--max needs a number of pixels.");
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int max)
                        || max < MinSize || max > MaxAllowedSize)
                        throw new ArgumentException($"--max must be a whole number from {MinSize} to {MaxAllowedSize}.");
                    cl.MaxSize = max;
                    break;
                case "--install":
                case "/install":
                    cl.Install = true;
                    break;
                case "--uninstall":
                case "/uninstall":
                    cl.Uninstall = true;
                    break;
                case "--silent":
                case "/silent":
                    cl.Silent = true;
                    break;
                case "--mode":
                    if (i + 1 >= args.Count || !ShrinkModeText.TryParse(args[++i], out var mode))
                        throw new ArgumentException("--mode must be recycle, originals or copy.");
                    cl.Mode = mode;
                    break;
                case "--yes":
                    cl.Yes = true;
                    break;
                case "--test-undo":
                    cl.TestUndo = true;
                    break;
                case "--report":
                    if (i + 1 >= args.Count)
                        throw new ArgumentException("--report needs a file path.");
                    cl.ReportPath = Path.GetFullPath(args[++i]);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Unknown option: " + a);
                    if (a.Length > 0)
                        cl.Files.Add(Path.GetFullPath(a));
                    break;
            }
        }

        if (cl.Install && cl.Uninstall)
            throw new ArgumentException("Use either --install or --uninstall, not both.");
        if (cl.Files.Count > 0 && cl.MaxSize is null && !cl.Install && !cl.Uninstall)
            throw new ArgumentException("Files were given without --max <pixels>.");
        return cl;
    }
}
