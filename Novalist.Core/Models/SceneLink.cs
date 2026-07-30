using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>What a link points at.</summary>
public static class LinkKinds
{
    public const string Scene = "scene";
    public const string Research = "research";
    public const string Entity = "entity";

    /// <summary>The kinds a link may name, so an unknown one is refused rather than stored.</summary>
    public static readonly string[] All = [Scene, Research, Entity];

    /// <summary>True for a kind this build can resolve and open.</summary>
    public static bool IsKnown(string? kind)
        => kind != null && All.Contains(kind, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One thing a scene points at: another scene, a research item, or a Codex
/// entry.
///
/// Scenes had no link model at all. A scene that answers another scene, or
/// leans on one research note, could only say so in its own notes as prose -
/// which nothing could follow, and which the scene at the other end never knew
/// about.
/// </summary>
public sealed class SceneLink
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>One of <see cref="LinkKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = LinkKinds.Scene;

    /// <summary>The scene id, research id or entity id at the other end.</summary>
    [JsonPropertyName("targetId")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Why the link is there, in the writer's words: "pays off the promise
    /// made here". Optional - a bare link is still worth having, and demanding
    /// a reason is how a link does not get made.
    /// </summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
