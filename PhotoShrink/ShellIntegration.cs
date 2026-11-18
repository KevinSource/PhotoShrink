using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace PhotoShrink;

/// <summary>
/// Adds/removes the right-click submenu and the Send To shortcuts. Everything is per user
/// (HKCU\Software\Classes and the user's SendTo folder), so no admin rights are needed.
///
/// Registry layout (HKCU\Software\Classes = the per-user half of HKEY_CLASSES_ROOT):
///
///   SystemFileAssociations\.jpg\shell\PhotoShrink            (same for .jpeg .png .gif)
///       MUIVerb          = "Shrink photo"
///       Icon             = "C:\…\PhotoShrink.exe",0
///       SubCommands      = ""                                 (empty = items are in the shell subkey below)
///       MultiSelectModel = "Player"
///     shell\1_1200
///         MUIVerb          = "1200 px"
///         MultiSelectModel = "Player"
///         command\(Default) = "C:\…\PhotoShrink.exe" --max 1200 "%1"
///     shell\2_1600 …
///     shell\3_2400 …
///
/// (Version 1 used ExtendedSubCommandsKey pointing at a shared HKCU key. Explorer showed the
/// "Shrink photo" arrow but an empty flyout, so the items are now nested under each verb.
/// Install/uninstall still remove that old PhotoShrink.Menu key if it's there.)
///
/// SystemFileAssociations applies by file extension no matter which app is the default
/// photo viewer, so the menu survives a change of default app.
/// MultiSelectModel=Player lifts Explorer's 15-item limit for classic verbs; Explorer then
/// starts one process per file, which <see cref="BatchCoordinator"/> merges into one batch.
/// </summary>
internal static class ShellIntegration
{
    public static readonly int[] Sizes = { 1200, 1600, 2400 };
    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".gif" };

    public const string VerbName = "PhotoShrink";
    public const string MenuText = "Shrink photo";
    /// <summary>Shared submenu key used by version 1; only removed now, never written.</summary>
    public const string LegacySubMenuKey = "PhotoShrink.Menu";
    private const string ClassesRoot = @"Software\Classes";

    public static string CurrentExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PhotoShrink.exe");

    public static string SendToFolder => Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    public static string ShortcutPath(int size) => Path.Combine(SendToFolder, $"Shrink photos - {size} px.lnk");

    private static string VerbKeyPath(string ext) => $@"{ClassesRoot}\SystemFileAssociations\{ext}\shell\{VerbName}";

    public static void Install(string exePath)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Program file not found.", exePath);

        // Start from a clean slate so a reinstall from a new folder leaves nothing stale.
        RemoveRegistry();

        foreach (string ext in Extensions)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(VerbKeyPath(ext));
            verb.SetValue("MUIVerb", MenuText);
            verb.SetValue("Icon", $"\"{exePath}\",0");
            verb.SetValue("SubCommands", "");
            verb.SetValue("MultiSelectModel", "Player");
            for (int i = 0; i < Sizes.Length; i++)
            {
                int size = Sizes[i];
                using var item = verb.CreateSubKey($@"shell\{i + 1}_{size}");
                item.SetValue("MUIVerb", $"{size} px");
                item.SetValue("MultiSelectModel", "Player");
                using var cmd = item.CreateSubKey("command");
                cmd.SetValue("", $"\"{exePath}\" --max {size} \"%1\"");
            }
        }

        Directory.CreateDirectory(SendToFolder);
        foreach (int size in Sizes)
            CreateShortcut(ShortcutPath(size), exePath, $"--max {size}",
                $"Shrink the selected photos so the long edge is at most {size} px");

        NotifyShell();
    }

    public static void Uninstall()
    {
        RemoveRegistry();
        foreach (int size in Sizes)
        {
            string lnk = ShortcutPath(size);
            if (File.Exists(lnk)) File.Delete(lnk);
        }
        NotifyShell();
    }

    /// <summary>Returns the exe path the menu currently points to, or null if not installed.</summary>
    public static string? InstalledExePath()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"{VerbKeyPath(Extensions[0])}\shell\1_{Sizes[0]}\command");
        if (cmd?.GetValue("") is not string command) return null;
        // "C:\path\PhotoShrink.exe" --max 1200 "%1"
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 1) return command[1..end];
        }
        return command;
    }

    /// <summary>Lists everything PhotoShrink has registered (for the settings window and tests).</summary>
    public static IEnumerable<string> ListInstalledItems()
    {
        using (var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{LegacySubMenuKey}"))
            if (k != null) yield return @"HKCU\" + ClassesRoot + @"\" + LegacySubMenuKey;
        foreach (string ext in Extensions)
        {
            using var k = Registry.CurrentUser.OpenSubKey(VerbKeyPath(ext));
            if (k != null) yield return @"HKCU\" + VerbKeyPath(ext);
        }
        foreach (int size in Sizes)
            if (File.Exists(ShortcutPath(size))) yield return ShortcutPath(size);
    }

    private static void RemoveRegistry()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{LegacySubMenuKey}", throwOnMissingSubKey: false);
        foreach (string ext in Extensions)
        {
            Registry.CurrentUser.DeleteSubKeyTree(VerbKeyPath(ext), throwOnMissingSubKey: false);
            // Tidy the parent keys too, but only if nothing else lives there.
            DeleteIfEmpty($@"{ClassesRoot}\SystemFileAssociations\{ext}\shell");
            DeleteIfEmpty($@"{ClassesRoot}\SystemFileAssociations\{ext}");
        }
    }

    private static void DeleteIfEmpty(string path)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(path))
        {
            if (k == null || k.SubKeyCount > 0 || k.ValueCount > 0) return;
        }
        try { Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false); } catch { /* in use */ }
    }

    private static void NotifyShell()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const int SHCNF_IDLIST = 0x0000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // ---------------------------------------------------------------- .lnk creation

    private static void CreateShortcut(string lnkPath, string target, string arguments, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
