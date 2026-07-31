using System.Text.Json.Serialization;

namespace Novalist.Core.Services;

/// <summary>One earlier state of a Codex entry.</summary>
/// <param name="Id">The file it lives in, without its extension.</param>
/// <param name="SavedAt">When the state it holds was replaced.</param>
public sealed record EntityRevision(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("savedAt")] DateTime SavedAt,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes);

/// <summary>
/// What a Codex entry said before it was last written.
///
/// Snapshots covered scenes and nothing else, so overwriting a character sheet
/// had no answer inside the app: the wrong eye colour typed over the right one
/// was gone, and the documented remedy was a backup of the whole project.
///
/// A revision is taken at the moment of overwriting and holds the state being
/// replaced, which is the state a restore wants. Taking it after the write
/// would record what is already on screen and be worth nothing.
/// </summary>
public sealed class EntityHistory
{
    /// <summary>
    /// Revisions kept per entry. An entry is a small JSON file, so this is
    /// cheap; it exists so a project edited for a year does not accumulate
    /// thousands of them per character.
    /// </summary>
    public const int KeepPerEntity = 25;

    private readonly IProjectService _projects;

    public EntityHistory(IProjectService projects)
    {
        _projects = projects;
    }

    /// <summary>
    /// Records the state being replaced. Does nothing when there is nothing to
    /// replace, or when the new state is the same as the old - a save that
    /// changes nothing is not a revision.
    /// </summary>
    public async Task RecordAsync(string entityId, string previousJson, string nextJson)
    {
        if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrEmpty(previousJson)) return;
        if (string.Equals(previousJson, nextJson, StringComparison.Ordinal)) return;

        var dir = DirectoryFor(entityId);
        if (dir == null) return;
        Directory.CreateDirectory(dir);

        // Sortable, unique, and readable in a file listing.
        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json";
        await File.WriteAllTextAsync(Path.Combine(dir, name), previousJson);
        Prune(dir);
    }

    /// <summary>Revisions for an entry, newest first.</summary>
    public IReadOnlyList<EntityRevision> List(string entityId)
    {
        var dir = DirectoryFor(entityId);
        if (dir == null || !Directory.Exists(dir)) return [];

        return [.. Directory.EnumerateFiles(dir, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new EntityRevision(
                Path.GetFileNameWithoutExtension(f.Name),
                Parse(Path.GetFileNameWithoutExtension(f.Name)),
                f.Length))];
    }

    /// <summary>
    /// The stored state of one revision, or null when it is not there. The
    /// caller writes it back, because only it knows what kind of entry this is.
    /// </summary>
    public async Task<string?> ReadAsync(string entityId, string revisionId)
    {
        var dir = DirectoryFor(entityId);
        if (dir == null) return null;
        // A revision id is a file name, and a caller could hand over anything.
        if (revisionId.Contains('/') || revisionId.Contains('\\') || revisionId.Contains("..")) return null;

        var path = Path.Combine(dir, $"{revisionId}.json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    private static void Prune(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(KeepPerEntity)
            .ToList();
        foreach (var stale in files)
        {
            try { File.Delete(stale); }
            catch (IOException) { /* Something else has it; it goes next time. */ }
        }
    }

    /// <summary>
    /// Beside the scene snapshots, under the book's snapshot folder, so one
    /// setting moves all of a book's history and one folder can be ignored by
    /// Git if that is what somebody wants.
    /// </summary>
    private string? DirectoryFor(string entityId)
    {
        var book = _projects.ActiveBook;
        var root = _projects.ActiveDraftRoot ?? _projects.ActiveBookRoot;
        if (book == null || root == null) return null;
        return Path.Combine(root, book.SnapshotFolder, "Entities", entityId);
    }

    /// <summary>
    /// The timestamp back out of the file name. A file somebody renamed by hand
    /// still lists, with no date rather than an exception.
    /// </summary>
    private static DateTime Parse(string name)
        => DateTime.TryParseExact(name, "yyyyMMdd-HHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : default;
}
