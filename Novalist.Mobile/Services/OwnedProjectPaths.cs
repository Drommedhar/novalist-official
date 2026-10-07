using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Mobile.Services;

/// <summary>Relocates only projects previously opened inside this app's Documents directory.</summary>
public sealed class OwnedProjectPaths(string documents, string storePath)
{
    private sealed record Entry(string RelativePath, string ProjectId);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly object RegistryGate = new();

    public Task RecordAsync(string projectPath) => Task.Run(() =>
    {
        if (!Contained(documents, projectPath)) return;
        var id = ReadId(projectPath);
        if (string.IsNullOrEmpty(id)) return;
        lock (RegistryGate)
        {
            var entries = Load();
            entries[projectPath] = new Entry(Path.GetRelativePath(documents, projectPath), id);
            Save(entries);
        }
    });

    public string? Resolve(string storedPath)
    {
        lock (RegistryGate)
        {
            var entries = Load();
            if (!entries.TryGetValue(storedPath, out var entry)
                || entry is not { RelativePath.Length: > 0, ProjectId.Length: > 0 }) return null;
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(documents, entry.RelativePath));
                if (!Contained(documents, candidate) || string.Equals(candidate, storedPath, StringComparison.Ordinal)
                    || !string.Equals(ReadId(candidate), entry.ProjectId, StringComparison.Ordinal)) return null;
                // Recents can publish this path without opening the book. Persist its
                // exact authorization first, so another container move can resolve it.
                if (!entries.TryGetValue(candidate, out var existing) || existing != entry)
                {
                    entries[candidate] = entry;
                    Save(entries);
                }
                return candidate;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private void Save(Dictionary<string, Entry> entries)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(storePath))
            ?? throw new IOException("The owned-project registry must name a file.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".novalist-owned-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, entries, Json);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(storePath)) File.Replace(temporary, storePath, null);
            else File.Move(temporary, storePath);
        }
        finally { File.Delete(temporary); }
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
