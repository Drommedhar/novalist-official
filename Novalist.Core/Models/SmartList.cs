using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A saved scene query, akin to Scrivener "Collections". Persisted in
/// <see cref="ProjectMetadata"/>. UI for managing these is wired up at the
/// Explorer/Manuscript layer.
/// </summary>
public sealed class SmartList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="ChapterStatus"/> names; null = any status.</summary>
    [JsonPropertyName("chapterStatus")]
    public string? ChapterStatus { get; set; }

    /// <summary>Substring match on resolved POV (override or auto-detected); null = any.</summary>
    [JsonPropertyName("povContains")]
    public string? PovContains { get; set; }

    /// <summary>Tag that must appear in <see cref="SceneAnalysisOverrides.Tags"/>.</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    /// <summary>Plotline id the scene must belong to (Plot Grid membership); null = any.</summary>
    [JsonPropertyName("plotlineId")]
    public string? PlotlineId { get; set; }
}
