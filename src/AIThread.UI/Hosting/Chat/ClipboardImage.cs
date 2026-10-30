using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AIThread.UI.Hosting.Chat;

// The image on the Windows clipboard for Ctrl+V/Alt+V of aithread chat: the "PNG" format browsers and the Snipping
// Tool put there, else a bitmap (CF_DIBV5/CF_DIB) encoded as PNG, else the first image file copied in Explorer
// (CF_HDROP); and its text, which Ctrl+V pastes when there is no image. Plain Win32, no WinForms.
[SupportedOSPlatform("windows")]
public static class ClipboardImage
{
    private const uint CfDib = 8;
    private const uint CfUnicodeText = 13;
    private const uint CfHdrop = 15;
    private const uint CfDibV5 = 17;

    public sealed record Content(byte[]? Png, string? FilePath);

    // Null when the clipboard holds no image (or another program keeps it open).
    public static Content? Read()
    {
        if (!Open()) return null;
        try
        {
            var png = RegisterClipboardFormat("PNG");
            if (png != 0 && Data(png) is { Length: > 0 } pngBytes) return new Content(pngBytes, null);

            foreach (var format in new[] { CfDibV5, CfDib })
                if (Data(format) is { Length: > 0 } dib && DibToPng(dib) is { } encoded)
                    return new Content(encoded, null);

            if (DroppedFiles().FirstOrDefault(AIThread.Common.Images.PromptImages.LooksLikeImagePath) is { } file)
                return new Content(null, file);
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // The clipboard's text (CF_UNICODETEXT), for Ctrl+V when the terminal passes the key on instead of pasting itself.
    public static string? ReadText()
    {
        if (!Open()) return null;
        try
        {
            if (Data(CfUnicodeText) is not { Length: > 1 } bytes) return null;
            var text = Encoding.Unicode.GetString(bytes);
            var end = text.IndexOf('\0');
            return end >= 0 ? text[..end] : text;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Another program may hold the clipboard for a moment.
    private static bool Open()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0)) return true;
            Thread.Sleep(30);
        }
        return false;
    }

    private static byte[]? Data(uint format)
    {
        if (!IsClipboardFormatAvailable(format)) return null;
        var handle = GetClipboardData(format);
        if (handle == 0) return null;

        var size = (int)GlobalSize(handle);
        var pointer = GlobalLock(handle);
        if (pointer == 0) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static IEnumerable<string> DroppedFiles()
    {
        if (!IsClipboardFormatAvailable(CfHdrop)) yield break;
        var drop = GetClipboardData(CfHdrop);
        if (drop == 0) yield break;

        var count = DragQueryFile(drop, uint.MaxValue, null, 0);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFile(drop, i, null, 0);
            var name = new StringBuilder((int)length + 1);
            if (DragQueryFile(drop, i, name, length + 1) > 0) yield return name.ToString();
        }
    }

    // 24 and 32 bit device-independent bitmaps (BI_RGB, or BI_BITFIELDS with the usual BGRA masks) to an RGBA PNG.
    // A 32 bit bitmap whose alpha is zero everywhere is opaque (most programs leave it unset).
    public static byte[]? DibToPng(byte[] dib)
    {
        if (dib.Length < 40) return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));
        if (width <= 0 || rawHeight == 0 || bits is not (24 or 32) || compression is not (0 or 3)) return null;

        var height = Math.Abs(rawHeight);
        var topDown = rawHeight < 0;
        var offset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + (int)colorsUsed * 4;
        var stride = (width * bits + 31) / 32 * 4;
        if ((long)offset + (long)stride * height > dib.Length) return null;

        var bytesPerPixel = bits / 8;
        var hasAlpha = false;
        if (bits == 32)
            for (var y = 0; y < height && !hasAlpha; y++)
                for (var x = 0; x < width; x++)
                    if (dib[offset + y * stride + x * 4 + 3] != 0)
                    {
                        hasAlpha = true;
                        break;
                    }

        // Each PNG row: filter byte 0, then RGBA.
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var source = offset + (topDown ? y : height - 1 - y) * stride;
            var target = y * (width * 4 + 1) + 1;
            for (var x = 0; x < width; x++)
            {
                var p = source + x * bytesPerPixel;
                raw[target++] = dib[p + 2];
                raw[target++] = dib[p + 1];
                raw[target++] = dib[p];
                raw[target++] = hasAlpha ? dib[p + 3] : (byte)255;
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(png, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(raw);
            WriteChunk(png, "IDAT", compressed.ToArray());
        }

        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);

        var crc = Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        png.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(uint crc, byte[] data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint handle);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? file, uint length);
}
