using Novalist.Core.Services;

namespace Novalist.Backend;

internal sealed class ExportAssetStaging(IFileService files) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "novalist-export-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    public async Task<string> MaterializeAsync(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return path;
        var source = Path.GetFullPath(path);
        if (File.Exists(source)) return source;
        if (_paths.TryGetValue(source, out var staged)) return staged;
        if (!await files.ExistsAsync(source)) return source;
        var bytes = await files.ReadBytesAsync(source);
        var directory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(source));
        await File.WriteAllBytesAsync(destination, bytes);
        _paths.Add(source, destination);
        return destination;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
