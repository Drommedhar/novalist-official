namespace Novalist.Core.Services;

public interface IBatchTextReader
{
    Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}

internal static class FileReadBatch
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IFileService files,
        IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (files is IBatchTextReader batch) return await batch.ReadExistingTextsAsync(paths, cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await files.ExistsAsync(path)) result[path] = await files.ReadTextAsync(path);
        }
        return result;
    }

    public static async Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IFileService files,
        IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (files is IBatchTextReader batch) return await batch.ReadTextsAsync(paths, cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[path] = await files.ReadTextAsync(path);
        }
        return result;
    }
}
