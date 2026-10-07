using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

/// <summary>Relocates only projects previously opened inside this app's Documents directory.</summary>
public sealed class OwnedProjectPaths(string documents, string storePath)
{
    private sealed record Entry(string RelativePath, string ProjectId);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task RecordAsync(string projectPath)
    {
        if (!Contained(documents, projectPath)) return;
        var id = ReadId(projectPath);
        if (string.IsNullOrEmpty(id)) return;
        var entries = Load();
        entries[projectPath] = new Entry(Path.GetRelativePath(documents, projectPath), id);
        await new FileService().WriteTextAsync(storePath, JsonSerializer.Serialize(entries, Json));
    }

    public string? Resolve(string storedPath)
    {
        if (!Load().TryGetValue(storedPath, out var entry)
            || entry is not { RelativePath.Length: > 0, ProjectId.Length: > 0 }) return null;
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(documents, entry.RelativePath));
            if (!Contained(documents, candidate) || string.Equals(candidate, storedPath, StringComparison.Ordinal)) return null;
            return string.Equals(ReadId(candidate), entry.ProjectId, StringComparison.Ordinal) ? candidate : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private Dictionary<string, Entry> Load()
    {
        try
        {
            return File.Exists(storePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(storePath)) ?? []
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string? ReadId(string projectPath)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(projectPath, ".novalist", "project.json"));
            return JsonSerializer.Deserialize<ProjectMetadata>(json)?.Id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool Contained(string root, string path)
        => !string.IsNullOrEmpty(root) && Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
}
