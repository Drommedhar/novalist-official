using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed record ImportImageReference(
    [property: JsonPropertyName("imageId")] string ImageId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("alt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Alt = null);
public sealed record ImportImageUpload(string ImageId, string ContentType, int Bytes, bool Reused);
public sealed class ImportImageNotFoundException() : FormatException("Upload every referenced image using POST /v1/images before validating or importing.");

/// <summary>Book-local, content-addressed images. References never read agent-supplied paths.</summary>
internal sealed class ImportImageStore(IProjectService projects, IFileService files)
{
    public const int MaxImageBytes = 32 * 1024 * 1024;
    public const int MaxImagesPerEntry = 100;
    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal)
    {
        ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/gif"] = ".gif",
        ["image/webp"] = ".webp", ["image/bmp"] = ".bmp", ["image/svg+xml"] = ".svg"
    };
    public static IReadOnlyCollection<string> ContentTypes => Formats.Keys;

    public async Task<ImportImageUpload> UploadAsync(byte[] content, string contentType)
    {
        if (content.Length is < 1 or > MaxImageBytes)
            throw new ArgumentException($"Send one image containing 1–{MaxImageBytes} bytes.");
        if (!Formats.TryGetValue(contentType, out var extension) || !Matches(content, contentType))
            throw new ArgumentException("Send PNG, JPEG, GIF, WebP, BMP or SVG image bytes with their matching Content-Type.");
        var id = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var destination = Path.Combine(projects.ActiveBookRoot!, RelativePath(id, extension));
        var reused = await files.ExistsAsync(destination);
        if (!reused)
        {
            // Publish only a complete file, so a failed upload can be retried safely.
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await files.WriteBytesAsync(temporary, content);
                await files.MoveFileAsync(temporary, destination);
            }
            finally
            {
                if (await files.ExistsAsync(temporary)) await files.DeleteFileAsync(temporary);
            }
        }
        return new(id, contentType, content.Length, reused);
    }

    public async Task<List<EntityImage>> ResolveAsync(IReadOnlyList<ImportImageReference> references)
    {
        if (references.Count > MaxImagesPerEntry) throw new ArgumentException($"Attach at most {MaxImagesPerEntry} images per entry.");
        var result = new List<EntityImage>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (reference == null || !FolderImportSchema.Accepts(FolderImportSchema.ImageReferenceSchema(), JsonSerializer.SerializeToNode(reference)))
                throw new ArgumentException("Each image requires an imageId returned by POST /v1/images and a non-empty name; alt is optional.");
            string? path = null;
            foreach (var extension in Formats.Values)
            {
                var candidate = RelativePath(reference.ImageId, extension);
                if (await files.ExistsAsync(Path.Combine(projects.ActiveBookRoot!, candidate))) { path = candidate; break; }
            }
            if (path == null) throw new ImportImageNotFoundException();
            if (paths.Add(path)) result.Add(new EntityImage { Name = reference.Name, Path = path, Alt = reference.Alt ?? string.Empty });
        }
        return result;
    }

    private string RelativePath(string id, string extension)
        => Path.Combine(projects.ActiveBook!.ImageFolder, "Imported", id + extension).Replace('\\', '/');

    private static bool Matches(byte[] content, string contentType)
    {
        var bytes = content.AsSpan();
        switch (contentType)
        {
            case "image/png": return bytes.Length >= 24 && bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes[12..16].SequenceEqual("IHDR"u8);
            case "image/jpeg": return bytes.Length >= 4 && bytes.StartsWith(new byte[] { 255, 216, 255 }) && bytes[^2..].SequenceEqual(new byte[] { 255, 217 });
            case "image/gif": return bytes.Length >= 13 && (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8));
            case "image/webp": return bytes.Length >= 20 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);
            case "image/bmp": return bytes.Length >= 26 && bytes.StartsWith("BM"u8);
            default:
                try
                {
                    using var input = new MemoryStream(content, writable: false);
                    using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxImageBytes });
                    if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
                    while (reader.Read()) { }
                    return true;
                }
                catch (XmlException) { return false; }
        }
    }
}
