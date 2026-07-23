using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>A cached AI-generated Wiki summary for one entity.</summary>
public sealed class WikiArticleCacheEntry
{
    /// <summary>The generated summary prose.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>When it was generated (ISO 8601 UTC).</summary>
    public string GeneratedAt { get; set; } = string.Empty;

    /// <summary>Hash of the deterministic dossier the summary was generated from;
    /// used to flag the summary stale when the entity's data changes.</summary>
    public string InputHash { get; set; } = string.Empty;
}

/// <summary>
/// Persists AI-generated Wiki summaries per entity under
/// <c>.novalist/wiki/{entityId}.json</c>, so they survive across sessions and
/// are only regenerated on demand. The stored <see cref="WikiArticleCacheEntry.InputHash"/>
/// lets the reader flag a summary as out of date when the entity's underlying
/// data has changed.
/// </summary>
public sealed class WikiArticleCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProjectService _projects;
    private readonly IFileService _files;

    public WikiArticleCache(IProjectService projects, IFileService files)
    {
        _projects = projects;
        _files = files;
    }

    /// <summary>Stable hash of the dossier the summary is generated from.</summary>
    public static string ComputeInputHash(string dossier)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dossier)));

    /// <summary>Reads the cached entry for an entity, or null when there is no
    /// project, no cached file, or the file is unreadable.</summary>
    public async Task<WikiArticleCacheEntry?> ReadAsync(string entityId)
    {
        var dir = CacheDir();
        if (dir == null) return null;
        var path = _files.CombinePath(dir, $"{entityId}.json");
        if (!await _files.ExistsAsync(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<WikiArticleCacheEntry>(
                await _files.ReadTextAsync(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes (or replaces) the cached entry for an entity. No-op when no
    /// project is loaded.</summary>
    public async Task WriteAsync(string entityId, WikiArticleCacheEntry entry)
    {
        var dir = CacheDir();
        if (dir == null) return;
        await _files.CreateDirectoryAsync(dir);
        await _files.WriteTextAsync(
            _files.CombinePath(dir, $"{entityId}.json"),
            JsonSerializer.Serialize(entry, JsonOptions));
    }

    private string? CacheDir()
    {
        var root = _projects.ProjectRoot;
        return string.IsNullOrEmpty(root) ? null : _files.CombinePath(root, ".novalist", "wiki");
    }
}
