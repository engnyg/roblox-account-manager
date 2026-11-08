using System.IO.Compression;
using System.Runtime.InteropServices;

namespace RobloxAccountManager.Core.Native;

public readonly record struct Region(int X, int Y, int W, int H);

/// <summary>擷取視窗畫面（PrintWindow + GDI）並編碼成 PNG，無需 System.Drawing。供「追蹤」功能使用。</summary>
public static class ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    private const uint PW_RENDERFULLCONTENT = 2;

    /// <summary>擷取整個視窗為 top-down 的 BGRA 像素；失敗回傳 null。</summary>
    public static (int W, int H, byte[] Bgra)? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return null;

        var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        IntPtr bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (bmp == IntPtr.Zero || bits == IntPtr.Zero) { DeleteDC(dc); return null; }
        var old = SelectObject(dc, bmp);
        try
        {
            if (!PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)) return null;
            var data = new byte[w * h * 4];
            Marshal.Copy(bits, data, 0, data.Length);
            return (w, h, data);
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bmp);
            DeleteDC(dc);
        }
    }

    public static bool TryParseRegion(string? text, out Region region)
    {
        region = default;
        var parts = (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        var n = new int[4];
        for (int i = 0; i < 4; i++) if (!int.TryParse(parts[i], out n[i])) return false;
        if (n[0] < 0 || n[1] < 0 || n[2] <= 0 || n[3] <= 0) return false;
        region = new Region(n[0], n[1], n[2], n[3]);
        return true;
    }

    /// <summary>裁切；超出範圍的部分會被截掉，完全在範圍外回傳 null。</summary>
    public static (int W, int H, byte[] Bgra)? Crop(int srcW, int srcH, byte[] src, Region r)
    {
        int x0 = Math.Min(r.X, srcW), y0 = Math.Min(r.Y, srcH);
        int w = Math.Min(r.W, srcW - x0), h = Math.Min(r.H, srcH - y0);
        if (w <= 0 || h <= 0) return null;
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(src, ((y0 + y) * srcW + x0) * 4, dst, y * w * 4, w * 4);
        return (w, h, dst);
    }

    // ------------------------------------------------------------------ //
    // PNG 編碼（RGB 8-bit，無 alpha）
    // ------------------------------------------------------------------ //

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(t, data));
        s.Write(len);
    }

    public static byte[] EncodePng(int w, int h, byte[] bgra)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit, truecolor
        WriteChunk(ms, "IHDR", ihdr);

        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var line = new byte[1 + w * 3];
                for (int y = 0; y < h; y++)
                {
                    line[0] = 0;
                    int si = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        line[1 + x * 3] = bgra[si + x * 4 + 2];     // R
                        line[2 + x * 3] = bgra[si + x * 4 + 1];     // G
                        line[3 + x * 3] = bgra[si + x * 4];         // B
                    }
                    z.Write(line);
                }
            }
            WriteChunk(ms, "IDAT", raw.ToArray());
        }
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }
}
