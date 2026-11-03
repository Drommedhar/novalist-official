using PdfSharp.Drawing;
using SkiaSharp;

namespace Novalist.Core.Services;

/// <summary>Normalizes decoded images to a stream owned for the PDF image's lifetime.</summary>
internal sealed class PdfImage : IDisposable
{
    private readonly MemoryStream stream;

    public PdfImage(string path)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new InvalidDataException("The PDF image could not be decoded.");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("The PDF image could not be decoded.");
        // Keep photographs compressed and preserve alpha for every other format.
        var format = codec.EncodedFormat == SKEncodedImageFormat.Jpeg
            ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        using var encoded = bitmap.Encode(format, 75);
        stream = new MemoryStream(encoded.ToArray());
        Image = XImage.FromStream(stream);
    }

    internal XImage Image { get; }

    public void Dispose()
    {
        Image.Dispose();
        stream.Dispose();
    }
}
