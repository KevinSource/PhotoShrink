using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhotoShrink;

/// <summary>Shrinks one photo in place. Never throws: every outcome is a <see cref="ShrinkResult"/>.</summary>
internal static class ImageShrinker
{
    private const int OrientationTag = 0x0112;

    /// <summary>The shrunk file must be at least this much smaller (fraction of the original size).</summary>
    public const double MinSavings = 0.10;

    private static readonly HashSet<string> KnownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif" };

    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public static ShrinkResult Shrink(string path, int maxSize, int jpegQuality)
    {
        var r = new ShrinkResult { Path = path };
        string? tempPath = null;
        try
        {
            if (Directory.Exists(path))
                return Skip(r, ShrinkStatus.SkippedUnsupported, "is a folder, not a file");
            if (!File.Exists(path))
                return Fail(r, "file not found");

            string ext = Path.GetExtension(path);
            if (!KnownExtensions.Contains(ext))
                return Skip(r, ShrinkStatus.SkippedUnsupported, $"unsupported file type ({ext})");

            var info = new FileInfo(path);
            r.OldBytes = info.Length;
            DateTime createdUtc = info.CreationTimeUtc;
            DateTime modifiedUtc = info.LastWriteTimeUtc;

            // Read into memory so the original file isn't locked while we work.
            byte[] original = File.ReadAllBytes(path);
            using var srcStream = new MemoryStream(original, writable: false);
            Image src;
            try
            {
                src = Image.FromStream(srcStream, useEmbeddedColorManagement: true, validateImageData: true);
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
            {
                return Fail(r, "not a valid image (damaged or unsupported)");
            }

            using (src)
            {
                Guid fmt = src.RawFormat.Guid;
                bool isJpeg = fmt == ImageFormat.Jpeg.Guid;
                bool isPng = fmt == ImageFormat.Png.Guid;
                bool isGif = fmt == ImageFormat.Gif.Guid;

                if (isGif || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    r.OldWidth = src.Width; r.OldHeight = src.Height;
                    return Skip(r, ShrinkStatus.SkippedGif, "GIF (resizing would lose animation)");
                }
                if (!isJpeg && !isPng)
                    return Skip(r, ShrinkStatus.SkippedUnsupported,
                        $"content is not JPEG or PNG despite the {ext} name");

                // Turn phone photos stored sideways upright, then measure.
                r.Orientation = ReadOrientation(src);
                RotateFlipType rf = ToRotateFlip(r.Orientation);
                if (rf != RotateFlipType.RotateNoneFlipNone)
                    src.RotateFlip(rf);

                r.OldWidth = src.Width;
                r.OldHeight = src.Height;
                int longEdge = Math.Max(src.Width, src.Height);
                if (longEdge <= maxSize)
                    return Skip(r, ShrinkStatus.SkippedAlreadySmall, $"already within {maxSize} px");

                double scale = (double)maxSize / longEdge;
                int newW = Math.Max(1, (int)Math.Round(src.Width * scale));
                int newH = Math.Max(1, (int)Math.Round(src.Height * scale));

                byte[] encoded;
                using (Bitmap dst = Redraw(src, newW, newH, keepAlpha: isPng))
                using (var ms = new MemoryStream())
                {
                    if (isJpeg)
                    {
                        using var ep = new EncoderParameters(1);
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(jpegQuality, 1, 100));
                        dst.Save(ms, JpegCodec, ep);
                    }
                    else
                    {
                        dst.Save(ms, ImageFormat.Png);
                    }
                    encoded = ms.ToArray();
                }

                r.NewWidth = newW;
                r.NewHeight = newH;
                r.NewBytes = encoded.Length;

                if (encoded.Length > r.OldBytes * (1.0 - MinSavings))
                {
                    r.NewBytes = encoded.Length;
                    return Skip(r, ShrinkStatus.SkippedNotSmaller,
                        $"result would not be at least {MinSavings:P0} smaller ({Format.Bytes(r.OldBytes)} → {Format.Bytes(encoded.Length)})");
                }

                // Write to a temp file in the same folder (same volume, so the swap is a rename).
                string dir = Path.GetDirectoryName(path)!;
                tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.photoshrink.tmp");
                using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(encoded, 0, encoded.Length);
                    fs.Flush(flushToDisk: true);
                }

                // Read the temp file back from disk and make sure it's a good image of the right size.
                VerifyReadable(tempPath, newW, newH);

                ReplaceOriginal(tempPath, path);
                tempPath = null; // consumed

                // Keep dates so photo apps still sort the file where it was.
                File.SetCreationTimeUtc(path, createdUtc);
                File.SetLastWriteTimeUtc(path, modifiedUtc);

                r.NewBytes = new FileInfo(path).Length;
                r.Status = ShrinkStatus.Shrunk;
                return r;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(r, "access denied (read-only file or no permission)");
        }
        catch (IOException ex)
        {
            return Fail(r, ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(r, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    private static Bitmap Redraw(Image src, int w, int h, bool keepAlpha)
    {
        var dst = new Bitmap(w, h, keepAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        dst.SetResolution(src.HorizontalResolution > 0 ? src.HorizontalResolution : 96,
                          src.VerticalResolution > 0 ? src.VerticalResolution : 96);
        using var g = Graphics.FromImage(dst);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (keepAlpha) g.Clear(Color.Transparent);

        // TileFlipXY stops the bicubic filter from pulling a faint dark/transparent fringe in at the edges.
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        return dst;
    }

    private static void VerifyReadable(string file, int expectW, int expectH)
    {
        byte[] bytes = File.ReadAllBytes(file);
        using var ms = new MemoryStream(bytes, writable: false);
        try
        {
            using var img = Image.FromStream(ms, false, validateImageData: true);
            if (img.Width != expectW || img.Height != expectH)
                throw new IOException($"verification failed: temp file is {img.Width}x{img.Height}, expected {expectW}x{expectH}");
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new IOException("verification failed: the shrunk temp file could not be read back as an image");
        }
    }

    /// <summary>
    /// Swaps the temp file in for the original. Both are in the same folder, so this is a rename;
    /// the original is replaced whole or not at all, never half-written.
    /// </summary>
    private static void ReplaceOriginal(string tempPath, string path)
    {
        try
        {
            // ReplaceFile keeps the original's ACLs, attributes and alternate streams.
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (IOException) when (File.Exists(tempPath) && File.Exists(path))
        {
            // Some file systems (FAT/exFAT, some network shares) don't support ReplaceFile.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }

    private static int ReadOrientation(Image img)
    {
        if (!img.PropertyIdList.Contains(OrientationTag))
            return 1;
        try
        {
            var item = img.GetPropertyItem(OrientationTag);
            if (item?.Value == null || item.Value.Length < 2)
                return 1;
            // SHORT; GDI+ hands us the value in the machine's byte order.
            int v = BitConverter.ToUInt16(item.Value, 0);
            return v is >= 1 and <= 8 ? v : 1;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>EXIF orientation (1–8) → the RotateFlip that makes the image upright.</summary>
    internal static RotateFlipType ToRotateFlip(int orientation) => orientation switch
    {
        2 => RotateFlipType.RotateNoneFlipX,
        3 => RotateFlipType.Rotate180FlipNone,
        4 => RotateFlipType.Rotate180FlipX,
        5 => RotateFlipType.Rotate90FlipX,
        6 => RotateFlipType.Rotate90FlipNone,
        7 => RotateFlipType.Rotate270FlipX,
        8 => RotateFlipType.Rotate270FlipNone,
        _ => RotateFlipType.RotateNoneFlipNone,
    };

    private static ShrinkResult Skip(ShrinkResult r, ShrinkStatus s, string reason)
    {
        r.Status = s;
        r.Reason = reason;
        return r;
    }

    private static ShrinkResult Fail(ShrinkResult r, string reason)
    {
        r.Status = ShrinkStatus.Failed;
        r.Reason = reason;
        return r;
    }
}
