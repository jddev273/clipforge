using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClipForge.Core.Native;

/// <summary>
/// Clipboard read/write via raw Win32. Used by the listener (read) and paste (write).
/// All methods are best-effort: they return false / throw ClipboardBusyException-safe
/// results instead of crashing the host on a locked clipboard.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsClipboard
{
    private static uint _htmlFormatId;

    private static uint HtmlFormatId =>
        _htmlFormatId != 0 ? _htmlFormatId : (_htmlFormatId = NativeMethods.RegisterClipboardFormatW("HTML Format"));

    // ---------------- read ----------------

    public static bool TryGetText(out string? text)
    {
        string? result = null;
        bool ok = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT)
            && WithClipboardOpen(() =>
            {
                var h = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                if (h == IntPtr.Zero) return false;
                var p = NativeMethods.GlobalLock(h);
                if (p == IntPtr.Zero) return false;
                try
                {
                    result = Marshal.PtrToStringUni(p);
                    return !string.IsNullOrEmpty(result);
                }
                finally { NativeMethods.GlobalUnlock(h); }
            });
        text = result;
        return ok;
    }

    public static bool TryGetHtml(out string? fragment)
    {
        fragment = null;
        uint fmt = HtmlFormatId;
        string? result = null;
        bool ok = fmt != 0
            && NativeMethods.IsClipboardFormatAvailable(fmt)
            && WithClipboardOpen(() =>
            {
                var h = NativeMethods.GetClipboardData(fmt);
                if (h == IntPtr.Zero) return false;
                var p = NativeMethods.GlobalLock(h);
                if (p == IntPtr.Zero) return false;
                try
                {
                    int size = (int)NativeMethods.GlobalSize(h);
                    var bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, size);
                    result = ExtractHtmlFragment(bytes);
                    return result is not null;
                }
                finally { NativeMethods.GlobalUnlock(h); }
            });
        fragment = result;
        return ok;
    }

    public static bool TryGetFileList(out string[] paths)
    {
        paths = Array.Empty<string>();
        string[]? result = null;
        bool ok = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP) && WithClipboardOpen(() =>
        {
            var h = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
            if (h == IntPtr.Zero) return false;
            uint count = NativeMethods.DragQueryFileW(h, 0xFFFFFFFF, null, 0);
            if (count == 0) return false;
            var list = new List<string>((int)count);
            var sb = new StringBuilder(1024);
            for (uint i = 0; i < count; i++)
            {
                sb.Clear();
                if (NativeMethods.DragQueryFileW(h, i, sb, (uint)sb.Capacity) > 0)
                    list.Add(sb.ToString());
            }
            result = list.ToArray();
            return result.Length > 0;
        });
        if (result is not null) paths = result;
        return ok;
    }

    public static bool TryGetImage(out byte[]? pngBytes, out int width, out int height)
    {
        pngBytes = null; width = 0; height = 0;
        byte[]? result = null;
        int w = 0, h = 0;
        bool ok = WithClipboardOpen(() =>
        {
            IntPtr data = IntPtr.Zero;
            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIBV5))
                data = NativeMethods.GetClipboardData(NativeMethods.CF_DIBV5);
            else if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB))
                data = NativeMethods.GetClipboardData(NativeMethods.CF_DIB);
            if (data == IntPtr.Zero) return false;
            var p = NativeMethods.GlobalLock(data);
            if (p == IntPtr.Zero) return false;
            try
            {
                using var bmp = DibToBitmap(p, out w, out h);
                if (bmp is null) return false;
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                result = ms.ToArray();
                return true;
            }
            finally { NativeMethods.GlobalUnlock(data); }
        });
        pngBytes = result; width = w; height = h;
        return ok;
    }

    /// <summary>Priority probe: files &gt; image &gt; html &gt; text.</summary>
    public static ClipType? AvailableType()
    {
        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP)) return ClipType.Files;
        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIBV5)
            || NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB)) return ClipType.Image;
        if (HtmlFormatId != 0 && NativeMethods.IsClipboardFormatAvailable(HtmlFormatId)) return ClipType.Html;
        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT)) return ClipType.Text;
        return null;
    }

    // ---------------- write ----------------

    public static bool TrySetText(string text)
    {
        try
        {
            return WithClipboardOpen(() =>
            {
                NativeMethods.EmptyClipboard();
                var bytes = Encoding.Unicode.GetBytes(text + '\0');
                var h = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (h == IntPtr.Zero) return false;
                var p = NativeMethods.GlobalLock(h);
                if (p == IntPtr.Zero) { NativeMethods.GlobalFree(h); return false; }
                Marshal.Copy(bytes, 0, p, bytes.Length);
                NativeMethods.GlobalUnlock(h);
                return NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, h) != IntPtr.Zero;
                // NOTE: ownership of h transfers to the clipboard; do not free.
            });
        }
        catch { return false; }
    }

    public static bool TrySetFileList(string[] paths)
    {
        try
        {
            return WithClipboardOpen(() =>
            {
                NativeMethods.EmptyClipboard();
                // DROPFILES (20 bytes) + UTF-16 paths, each null-terminated, final double null.
                var pathBytes = new List<byte>();
                foreach (var path in paths)
                {
                    pathBytes.AddRange(Encoding.Unicode.GetBytes(path));
                    pathBytes.AddRange(new byte[] { 0, 0 });
                }
                pathBytes.AddRange(new byte[] { 0, 0 });
                int total = 20 + pathBytes.Count;
                var h = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)total);
                if (h == IntPtr.Zero) return false;
                var p = NativeMethods.GlobalLock(h);
                if (p == IntPtr.Zero) { NativeMethods.GlobalFree(h); return false; }
                Marshal.WriteInt32(p, 0, 20);      // pFiles
                Marshal.WriteInt32(p, 4, 0);       // pt.x
                Marshal.WriteInt32(p, 8, 0);       // pt.y
                Marshal.WriteInt32(p, 12, 0);      // fNC
                Marshal.WriteInt32(p, 16, 1);      // fWide = TRUE
                Marshal.Copy(pathBytes.ToArray(), 0, p + 20, pathBytes.Count);
                NativeMethods.GlobalUnlock(h);
                return NativeMethods.SetClipboardData(NativeMethods.CF_HDROP, h) != IntPtr.Zero;
            });
        }
        catch { return false; }
    }

    public static bool TrySetImage(byte[] pngBytes)
    {
        try
        {
            using var src = new Bitmap(new MemoryStream(pngBytes));
            using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.DrawImage(src, 0, 0, src.Width, src.Height);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride; // 4-byte aligned for 32bpp
                int rowBytes = bmp.Width * 4;
                int total = 40 + stride * bmp.Height;
                var h = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)total);
                if (h == IntPtr.Zero) return false;
                var p = NativeMethods.GlobalLock(h);
                if (p == IntPtr.Zero) { NativeMethods.GlobalFree(h); return false; }
                try
                {
                    Marshal.WriteInt32(p, 0, 40);                    // biSize
                    Marshal.WriteInt32(p, 4, bmp.Width);             // biWidth
                    Marshal.WriteInt32(p, 8, bmp.Height);            // biHeight (>0 = bottom-up)
                    Marshal.WriteInt16(p, 12, 1);                    // biPlanes
                    Marshal.WriteInt16(p, 14, 32);                   // biBitCount
                    Marshal.WriteInt32(p, 16, 0);                    // biCompression = BI_RGB
                    Marshal.WriteInt32(p, 20, stride * bmp.Height);  // biSizeImage
                    var row = new byte[rowBytes];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        // DIB is bottom-up: last bitmap row first.
                        Marshal.Copy(data.Scan0 + (bmp.Height - 1 - y) * stride, row, 0, rowBytes);
                        Marshal.Copy(row, 0, p + 40 + y * stride, rowBytes);
                    }
                }
                finally { NativeMethods.GlobalUnlock(h); }

                return WithClipboardOpen(() =>
                {
                    NativeMethods.EmptyClipboard();
                    return NativeMethods.SetClipboardData(NativeMethods.CF_DIB, h) != IntPtr.Zero;
                });
            }
            finally { bmp.UnlockBits(data); }
        }
        catch { return false; }
    }

    // ---------------- internals ----------------

    private static bool WithClipboardOpen(Func<bool> action)
    {
        // Another app may hold the clipboard briefly; retry a few times.
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(30);
                    continue;
                }
                try { return action(); }
                finally { NativeMethods.CloseClipboard(); }
            }
            catch { return false; }
        }
        return false;
    }

    private static string? ExtractHtmlFragment(byte[] bytes)
    {
        // "HTML Format" is a UTF-8 header with byte offsets: StartFragment:/EndFragment:.
        string header = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        int start = ParseOffset(header, "StartFragment:");
        int end = ParseOffset(header, "EndFragment:");
        if (start < 0 || end <= start || end > bytes.Length) return null;
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    private static int ParseOffset(string header, string key)
    {
        int i = header.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return -1;
        i += key.Length;
        int j = i;
        while (j < header.Length && char.IsDigit(header[j])) j++;
        return int.TryParse(header[i..j], out int v) ? v : -1;
    }

    private static Bitmap? DibToBitmap(IntPtr p, out int width, out int height)
    {
        width = 0; height = 0;
        try
        {
            int headerSize = Marshal.ReadInt32(p, 0);
            if (headerSize < 40) return null;
            width = Marshal.ReadInt32(p, 4);
            int rawHeight = Marshal.ReadInt32(p, 8);
            height = Math.Abs(rawHeight);
            short bitCount = Marshal.ReadInt16(p, 14);
            int compression = Marshal.ReadInt32(p, 16);
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;

            int pixelOffset = headerSize + (compression == 3 ? 12 : 0); // BI_BITFIELDS masks
            int stride = ((width * bitCount + 31) / 32) * 4;
            bool bottomUp = rawHeight > 0;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[stride];
                var out32 = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    int srcY = bottomUp ? height - 1 - y : y;
                    Marshal.Copy(p + pixelOffset + srcY * stride, row, 0, stride);
                    switch (bitCount)
                    {
                        case 32:
                            Buffer.BlockCopy(row, 0, out32, 0, width * 4);
                            break;
                        case 24:
                            for (int x = 0; x < width; x++)
                            {
                                out32[x * 4] = row[x * 3];
                                out32[x * 4 + 1] = row[x * 3 + 1];
                                out32[x * 4 + 2] = row[x * 3 + 2];
                                out32[x * 4 + 3] = 255;
                            }
                            break;
                        default:
                            return null; // unsupported depth for MVP
                    }
                    Marshal.Copy(out32, 0, data.Scan0 + y * data.Stride, width * 4);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
        catch { return null; }
    }
}
