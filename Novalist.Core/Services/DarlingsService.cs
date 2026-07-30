using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novalist.Core.Services;

/// <summary>A piece of prose the writer cut but did not want to lose.</summary>
public sealed class Darling
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The prose itself, exactly as it was cut.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Where it came from, so it can be put back somewhere sensible. Empty when
    /// the scene it came from is gone, which does not make the prose worth less.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>What the writer said about it, if anything.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Prose the writer cut and kept.
///
/// Deleted text was recoverable only by opening a snapshot of the whole scene
/// and reading it for the paragraph that used to be there. A paragraph somebody
/// cut because it did not belong in this chapter is not a mistake to undo - it
/// is a piece of writing looking for a different home, and there was nowhere to
/// put it.
///
/// The file is a project sidecar. Cut prose belongs to the project rather than
/// to the machine, so it survives being zipped and travels with the book.
/// </summary>
public class DarlingsService(IProjectService projectService, IFileService fileService)
{
    private const string FileName = "darlings.json";

    /// <summary>
    /// A cap, so a writer who cuts all day does not end up with a file bigger
    /// than the manuscript. The oldest goes first, because the reason to keep
    /// a cut is usually to use it soon.
    /// </summary>
    internal const int MaxKept = 500;

    private readonly IProjectService _projectService = projectService;
    private readonly IFileService _fileService = fileService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string? Path => _projectService.ProjectRoot == null
        ? null
        : _fileService.CombinePath(_projectService.ProjectRoot, ".novalist", FileName);

    /// <summary>Everything kept, newest first.</summary>
    public async Task<List<Darling>> ListAsync()
    {
        var path = Path;
        if (path == null || !await _fileService.ExistsAsync(path)) return [];
        try
        {
            var json = await _fileService.ReadTextAsync(path);
            var all = JsonSerializer.Deserialize<List<Darling>>(json, JsonOptions) ?? [];
            return [.. all.OrderByDescending(d => d.CreatedAt)];
        }
        catch (JsonException)
        {
            // A corrupt sidecar loses the cuts, which is bad. Refusing to open
            // the panel over it loses them and hides that it happened.
            return [];
        }
    }

    /// <summary>
    /// Keeps a piece of cut prose.
    ///
    /// Blank text is not kept: cutting a space and being offered to save it is
    /// how a writer learns to ignore the feature.
    /// </summary>
    public async Task<List<Darling>> KeepAsync(string? text, string? source = null, string? note = null)
    {
        var prose = (text ?? string.Empty).Trim();
        if (prose.Length == 0) return await ListAsync();

        var all = await ListAsync();
        all.Insert(0, new Darling
        {
            Text = prose,
            Source = (source ?? string.Empty).Trim(),
            Note = (note ?? string.Empty).Trim()
        });
        return await SaveAsync(all);
    }

    /// <summary>Rewrites what the writer said about a cut.</summary>
    public async Task<List<Darling>> SetNoteAsync(string id, string? note)
    {
        var all = await ListAsync();
        var found = all.FirstOrDefault(d => d.Id == id);
        if (found == null) return all;
        found.Note = (note ?? string.Empty).Trim();
        return await SaveAsync(all);
    }

    /// <summary>Throws one away for good.</summary>
    public async Task<List<Darling>> RemoveAsync(string id)
    {
        var all = await ListAsync();
        return all.RemoveAll(d => d.Id == id) == 0 ? all : await SaveAsync(all);
    }

    private async Task<List<Darling>> SaveAsync(List<Darling> all)
    {
        var kept = all
            .OrderByDescending(d => d.CreatedAt)
            .Take(MaxKept)
            .ToList();

        var path = Path;
        if (path != null)
            await _fileService.WriteTextAsync(path, JsonSerializer.Serialize(kept, JsonOptions));
        return kept;
    }
}
