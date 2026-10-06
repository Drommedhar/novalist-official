using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportArchive;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    /// <summary>
    /// Word's own drop cap: the initial sits in a framed paragraph the text
    /// wraps around, and the rest of the opener follows as ordinary prose with
    /// its lead-in words in small capitals.
    /// </summary>
    private static string DocxDropCapParagraphs(
        (string Initial, string LeadIn, string Tail) opener, ExportPreset preset)
    {
        var size = (int)Math.Round(preset.BodyFontSizePt * 2 * 3);  // half-points, three lines
        var initial =
            "<w:p><w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\""
            + " w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:line=\"640\" w:lineRule=\"exact\"/>"
            + $"</w:pPr><w:r><w:rPr><w:sz w:val=\"{size}\"/></w:rPr>"
            + $"<w:t xml:space=\"preserve\">{EscapeXml(opener.Initial)}</w:t></w:r></w:p>";

        var lead = opener.LeadIn.Length == 0
            ? string.Empty
            : $"<w:r><w:rPr><w:smallCaps/></w:rPr><w:t xml:space=\"preserve\">{EscapeXml(opener.LeadIn)}</w:t></w:r>";

        return initial
            + $"<w:p>{lead}<w:r><w:t xml:space=\"preserve\">{EscapeXml(opener.Tail)}</w:t></w:r></w:p>";
    }

    private static string DocxImageContentType(string extension) => extension switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        _ => "image/png"
    };

    /// <summary>One relationship per image part, for the document's rels file.</summary>
    private static string ImageRelationshipsXml(List<(string Part, string Absolute)> parts)
        => string.Concat(parts.Select((p, i) =>
            "\n  <Relationship Id=\"rIdImage" + (i + 1) + "\" Type=\""
            + "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
            + "\" Target=\"" + p.Part + "\"/>"));

    /// <summary>
    /// One centred paragraph holding an inline drawing. Word measures in EMUs
    /// (914400 to the inch), so the pixel size is converted at 96dpi and capped
    /// at six inches - an image wider than the page is a broken document rather
    /// than a large picture.
    /// </summary>
    private static string DocxImageParagraph(string path, string relId, string alt)
    {
        var (pixelWidth, pixelHeight) = ImageSize(path);
        var naturalWidth = Math.Max(0.1, pixelWidth / 96.0);
        var widthInches = Math.Min(6.0, naturalWidth);
        var heightInches = pixelHeight / 96.0 * (widthInches / naturalWidth);
        var cx = (long)(widthInches * 914400);
        var cy = (long)(heightInches * 914400);
        var description = EscapeXml(alt);
        var id = Math.Abs(relId.GetHashCode()) % 100000 + 1;

        return "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>"
            + "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
            + " distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
            + $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>"
            + $"<wp:docPr id=\"{id}\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">"
            + "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + $"<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + $" r:embed=\"{relId}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>"
            + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    /// <summary>
    /// An image's pixel size, read from the file's own header. PNG and JPEG
    /// cover what the editor accepts; anything else falls back to a square,
    /// which lays out sensibly even when it is not exact.
    /// </summary>
    internal static (int Width, int Height) ImageSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var signature = reader.ReadBytes(8);

            // PNG: width and height are big-endian ints at byte 16.
            if (signature.Length == 8 && signature[0] == 0x89 && signature[1] == 0x50)
            {
                stream.Position = 16;
                var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                return (w, h);
            }

            // JPEG: walk the segments to the start-of-frame, which carries the size.
            if (signature.Length >= 2 && signature[0] == 0xFF && signature[1] == 0xD8)
            {
                stream.Position = 2;
                while (stream.Position < stream.Length - 8)
                {
                    if (reader.ReadByte() != 0xFF) continue;
                    var marker = reader.ReadByte();
                    var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        reader.ReadByte();
                        var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        return (w, h);
                    }
                    stream.Position += length - 2;
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException
            or ArgumentException or UnauthorizedAccessException)
        {
            // Unreadable is not fatal: the fallback below still lays out.
        }
        return (600, 600);
    }
}
