using Novalist.Core.Services;

namespace Novalist.Backend.Rpc;

/// <summary>Downloads, names, and stages remote images before importing them into the project.</summary>
internal sealed class EntityImageImporter
{
    private readonly EntityService _entities;
    private readonly HttpClient _http;

    internal EntityImageImporter(EntityService entities, HttpClient http)
    {
        _entities = entities;
        _http = http;
    }

    private static readonly string[] ImageFileExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"];

    /// <summary>Downloads an image from a remote URL (via the injected
    /// <see cref="HttpClient"/> so tests stub the transport) and imports it into
    /// the entity image folder, returning the project-relative path and the
    /// derived file name. A failed request surfaces as a clean error.</summary>
    internal async Task<(string Relative, string FileName)> ImportAsync(string url)
    {
        byte[] bytes;
        try
        {
            using var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Could not download the image from the given URL.", ex);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "nl-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = DeriveImageFileName(url);
        var tempPath = Path.Combine(tempDir, fileName);
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            var relative = await _entities.ImportImageAsync(tempPath);
            return (relative, fileName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>Derives a safe, image-extensioned file name from a download URL,
    /// falling back to <c>image.png</c> when the URL carries no usable segment.</summary>
    private static string DeriveImageFileName(string url)
    {
        var name = "image";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.LastOrDefault(s => s.Trim('/').Length > 0)?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment))
                name = Path.GetFileName(Uri.UnescapeDataString(segment).Replace('\\', '/'));
        }
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return ImageFileExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())
            ? name
            : name + ".png";
    }
}
