using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Sdk.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Stores one analysis record per scene under
/// <c>.novalist/analysis/{sceneId}.json</c> and answers which scenes are stale.
///
/// The host owns the location, the format and the staleness maths; it never
/// produces a record itself — an extension does the analysis and hands the result
/// here. Keeping both sides on one schema means the readers (focus peek, context
/// sidebar, "talk as character") cannot drift from the writer.
///
/// A scene is the unit of generation and of invalidation, so one edited scene
/// rewrites exactly one small file. That matters because this is portable project
/// data rather than a throwaway cache: it lives in the project folder and travels
/// with it, so analysis produced on a machine with model access stays readable on
/// one without.
/// </summary>
public sealed class SceneAnalysisStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProjectService _projects;
    private readonly IFileService _files;

    public SceneAnalysisStore(IProjectService projects, IFileService files)
    {
        _projects = projects;
        _files = files;
    }

    /// <summary>Stable hash of a scene's text, used to tell whether the scene has
    /// changed since it was analysed.</summary>
    public static string ComputeSceneHash(string? sceneText)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sceneText ?? string.Empty)));

    /// <summary>The stored record for a scene, or null when it has never been
    /// analysed (or the file is unreadable).</summary>
    public async Task<SceneAnalysisRecord?> ReadAsync(string sceneId)
    {
        var path = RecordPath(sceneId);
        if (path == null || !await _files.ExistsAsync(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SceneAnalysisRecord>(
                await _files.ReadTextAsync(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null; // corrupt file — treat the scene as never analysed
        }
    }

    /// <summary>Writes the record for a scene, stamping the hash of the text it
    /// was produced from and the current schema version. No-op without a project.</summary>
    public async Task WriteAsync(SceneAnalysisRecord record, string sceneText)
    {
        var dir = AnalysisDir();
        var path = RecordPath(record.SceneId);
        if (dir == null || path == null) return;

        record.SchemaVersion = SceneAnalysisRecord.CurrentSchemaVersion;
        record.SceneContentHash = ComputeSceneHash(sceneText);
        record.GeneratedAt = DateTime.UtcNow.ToString("o");

        await _files.CreateDirectoryAsync(dir);
        await _files.WriteTextAsync(path, JsonSerializer.Serialize(record, JsonOptions));
    }

    /// <summary>
    /// Whether a scene still needs analysing: true when it has no record, when the
    /// text has changed since, or when the record predates the current schema.
    /// </summary>
    public async Task<bool> IsStaleAsync(string sceneId, string sceneText)
    {
        var record = await ReadAsync(sceneId);
        return record == null
               || record.SchemaVersion < SceneAnalysisRecord.CurrentSchemaVersion
               || !string.Equals(record.SceneContentHash, ComputeSceneHash(sceneText), StringComparison.Ordinal);
    }

    /// <summary>Of the supplied scenes, the ids still needing analysis — what lets
    /// a re-run touch only what actually changed.</summary>
    public async Task<IReadOnlyList<string>> GetStaleSceneIdsAsync(
        IReadOnlyList<(string SceneId, string Text)> scenes)
    {
        var stale = new List<string>();
        foreach (var (sceneId, text) in scenes)
            if (await IsStaleAsync(sceneId, text))
                stale.Add(sceneId);
        return stale;
    }

    /// <summary>Forgets a scene's record. No-op when nothing is stored.</summary>
    public async Task ClearAsync(string sceneId)
    {
        var path = RecordPath(sceneId);
        if (path != null && await _files.ExistsAsync(path))
            await _files.DeleteFileAsync(path);
    }

    private string? AnalysisDir()
    {
        var root = _projects.ProjectRoot;
        return string.IsNullOrEmpty(root) ? null : _files.CombinePath(root, ".novalist", "analysis");
    }

    private string? RecordPath(string sceneId)
    {
        var dir = AnalysisDir();
        return dir == null || string.IsNullOrWhiteSpace(sceneId)
            ? null
            : _files.CombinePath(dir, $"{sceneId}.json");
    }
}
