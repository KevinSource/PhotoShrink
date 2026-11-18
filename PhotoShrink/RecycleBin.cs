using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace PhotoShrink;

/// <summary>A file PhotoShrink sent to the Recycle Bin: its data file ($R…) and info file ($I…).</summary>
internal sealed record RecycledItem(string DataPath, string InfoPath);

/// <summary>
/// Sends originals to the Recycle Bin and finds them again for Undo.
///
/// Sending uses SHFileOperation with FOF_ALLOWUNDO (the documented way to recycle). Windows
/// would silently delete a file permanently on a drive without a Recycle Bin, so we only recycle
/// on local fixed drives where SHQueryRecycleBin succeeds; elsewhere the caller falls back to the
/// Originals folder. FOF_WANTNUKEWARNING makes Windows ask first if a file still can't be recycled
/// (for example, it's bigger than the Recycle Bin).
///
/// To undo, we need to know which item in the Recycle Bin is ours. Windows stores each recycled
/// file as X:\$Recycle.Bin\&lt;user SID&gt;\$Rxxxxxx.ext with a small $Ixxxxxx.ext beside it that holds
/// the original path and deletion time (format v1 since Vista, v2 since Windows 10). Right after
/// recycling we look for the new $I file whose original path matches, and remember both files.
/// Undo moves $R back to the original path and deletes $I, which is what Explorer's "Restore" does.
/// </summary>
internal static class RecycleBin
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, HashSet<string>> KnownInfoFiles = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsAvailableFor(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false; // network share
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            if (new DriveInfo(root).DriveType != DriveType.Fixed) return false;
        }
        catch
        {
            return false;
        }
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(root, ref info) == 0;
    }

    /// <summary>
    /// Sends <paramref name="path"/> to the Recycle Bin. Throws IOException if the file is still there
    /// afterwards. Returns the Recycle Bin entry, or null if it couldn't be identified.
    /// </summary>
    public static RecycledItem? Recycle(string path)
    {
        lock (Gate) // one shell operation at a time; also keeps the $I bookkeeping consistent
        {
            string binDir = BinFolderFor(path);
            HashSet<string> known = Known(binDir);

            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0",   // the marshaller adds the second terminating null
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_SILENT | FOF_NOERRORUI,
            };
            int rc = SHFileOperationW(ref op);
            if (rc != 0 || op.fAnyOperationsAborted || File.Exists(path))
                throw new IOException("the original could not be moved to the Recycle Bin, so nothing was changed");

            foreach (string info in ListInfoFiles(binDir))
            {
                if (!known.Add(Path.GetFileName(info))) continue;
                if (ReadOriginalPath(info) is string original &&
                    string.Equals(original, path, StringComparison.OrdinalIgnoreCase))
                {
                    string data = Path.Combine(binDir, "$R" + Path.GetFileName(info)[2..]);
                    return new RecycledItem(data, info);
                }
            }
            return null;
        }
    }

    private static string BinFolderFor(string path)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        return Path.Combine(Path.GetPathRoot(path)!, "$Recycle.Bin", sid);
    }

    private static HashSet<string> Known(string binDir)
    {
        if (!KnownInfoFiles.TryGetValue(binDir, out var set))
        {
            set = new HashSet<string>(ListInfoFiles(binDir).Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
            KnownInfoFiles[binDir] = set;
        }
        return set;
    }

    private static IEnumerable<string> ListInfoFiles(string binDir)
    {
        try
        {
            return Directory.GetFiles(binDir, "$I*");
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Reads the original full path out of a $I file (format v1 or v2).</summary>
    internal static string? ReadOriginalPath(string infoFile)
    {
        try
        {
            byte[] b = File.ReadAllBytes(infoFile);
            if (b.Length < 28) return null;
            long version = BitConverter.ToInt64(b, 0);
            string raw;
            if (version == 2)
            {
                int chars = BitConverter.ToInt32(b, 24);
                raw = Encoding.Unicode.GetString(b, 28, Math.Min(chars * 2, b.Length - 28));
            }
            else if (version == 1)
            {
                raw = Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24));
            }
            else
            {
                return null;
            }
            int nul = raw.IndexOf('\0');
            return nul >= 0 ? raw[..nul] : raw;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- interop

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    // 64-bit layout (natural alignment). PhotoShrink runs as a 64-bit process on 64-bit Windows.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
}
