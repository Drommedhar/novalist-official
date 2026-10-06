using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed partial class FolderImportService
{
    private async Task ImportImagesAsync(List<EntityImage> images, string sourceFile)
    {
        var scope = EnsureScope();
        foreach (var image in images.ToArray())
        {
            var path = image.Path.Replace('\\', '/');
            // Embedded links are local image references, not arbitrary paths or URLs.
            if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Contains("..")
                || Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp"))
            {
                images.Remove(image);
                continue;
            }
            string? source = null;
            var boundary = Path.GetDirectoryName(_root) ?? _root;
            for (var directory = Path.GetDirectoryName(Path.GetFullPath(sourceFile)); directory != null; directory = Path.GetDirectoryName(directory))
            {
                var candidate = Path.Combine(directory, path);
                if (await files.ExistsAsync(candidate)) { source = candidate; break; }
                if (directory.Equals(boundary, StringComparison.OrdinalIgnoreCase)) break;
            }
            if (source == null)
            {
                // A relocated export can still reference an image already in this book.
                if (await files.ExistsAsync(Path.Combine(scope.BookRoot, path))) image.Path = path;
                else images.Remove(image);
                continue;
            }
            if (!_imagePaths.TryGetValue(source, out var imported))
            {
                imported = Path.Combine(scope.Book.ImageFolder, "Imported", Identity("image", source) + Path.GetExtension(source).ToLowerInvariant()).Replace('\\', '/');
                var destination = Path.Combine(scope.BookRoot, imported);
                if (!await files.ExistsAsync(destination)) await files.WriteBytesAsync(destination, await files.ReadBytesAsync(source));
                _imagePaths[source] = imported;
            }
            image.Path = imported;
        }
    }
}
