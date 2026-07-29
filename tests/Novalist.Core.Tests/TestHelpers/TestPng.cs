using System.IO.Compression;

namespace Novalist.Core.Tests.TestHelpers;

/// <summary>
/// Builds a real PNG of any size, valid enough for a decoder to open.
///
/// A hand-written header is enough for code that only reads dimensions, but
/// the PDF writer decodes the file - so a test that wants to see an image
/// actually drawn needs the pixels, the zlib stream and the CRCs to be right.
/// </summary>
internal static class TestPng
{
    /// <summary>An opaque black PNG of the given size.</summary>
    public static byte[] Create(int width, int height)
    {
        var raw = new byte[height * (width * 4 + 1)];  // one filter byte per row
        using var pixels = new MemoryStream();
        using (var deflate = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type: RGBA

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", pixels.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) typed[i] = (byte)type[i];
        data.CopyTo(typed, 4);
        stream.Write(typed);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(typed)));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
