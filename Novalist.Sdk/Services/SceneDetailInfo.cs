
namespace Novalist.Sdk.Services;

/// <summary>What a chapter is, beyond the scenes in it.</summary>
public sealed class ChapterDetailInfo
{
    public string Guid { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }

    /// <summary>"Outline", "FirstDraft", "Revised", "Edited" or "Final".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The act label, or empty for a chapter in no act.</summary>
    public string Act { get; init; } = string.Empty;

    /// <summary>The in-world date as the writer wrote it. Free text.</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Story date range, where the writer set one.</summary>
    public string DateStart { get; init; } = string.Empty;
    public string DateEnd { get; init; } = string.Empty;

    /// <summary>The writer's description of the chapter.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The chapter's own word target, where one is set.</summary>
    public int? WordTarget { get; init; }

    /// <summary>Words across the chapter's scenes.</summary>
    public int WordCount { get; init; }

    /// <summary>Scenes in the chapter, in order.</summary>
    public IReadOnlyList<string> SceneIds { get; init; } = [];

    /// <summary>The writer's own typed fields on this chapter.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// The parts of a scene a caller wants changed. Null leaves a field as it is.
/// </summary>
public sealed class SceneMetadataPatch
{
    public string? Synopsis { get; init; }
    public string? Notes { get; init; }
    public string? Pov { get; init; }
    public string? Emotion { get; init; }
    public string? Conflict { get; init; }
    public int? Intensity { get; init; }
    public string? Stage { get; init; }
    public string? NarrativeMode { get; init; }
    public string? DateStart { get; init; }
    public string? DateEnd { get; init; }
    public bool? Inactive { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// The writer's own fields. Only the keys given are written; a key with a
    /// null value is removed.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Properties { get; init; }
}

/// <summary>What a scene is, beyond the words in it.</summary>
public sealed class SceneDetailInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChapterGuid { get; init; } = string.Empty;
    public int Order { get; init; }
    public int WordCount { get; init; }

    /// <summary>Whose head the scene is in, as the writer set it or as the host detected it.</summary>
    public string Pov { get; init; } = string.Empty;

    /// <summary>The writer's synopsis.</summary>
    public string Synopsis { get; init; } = string.Empty;

    /// <summary>Free-text notes on the scene.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Dramatic intensity 0-10 where the host or the writer set one, else null.</summary>
    public int? Intensity { get; init; }

    /// <summary>The dominant emotion, where one is recorded.</summary>
    public string Emotion { get; init; } = string.Empty;

    /// <summary>The central conflict, where one is recorded.</summary>
    public string Conflict { get; init; } = string.Empty;

    /// <summary>Story-structure stage label, or empty when unset; values follow the book's chosen structure.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>
    /// True when the scene is out of the book but still in the plan.
    ///
    /// An extension could not tell a parked scene from a live one, so every
    /// report counted words the manuscript does not contain and named scenes
    /// the reader will never reach.
    /// </summary>
    public bool Inactive { get; init; }

    /// <summary>Tags the writer put on the scene.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Threads the scene belongs to.</summary>
    public IReadOnlyList<string> PlotlineIds { get; init; } = [];

    /// <summary>
    /// Ids of the Codex entries the writer said are in this scene.
    ///
    /// Novalist has known this since assigned casts shipped and never handed it
    /// to an extension, so a report on who drops out of the book could only read
    /// the point of view - one name per scene, whoever else was standing there.
    /// </summary>
    public IReadOnlyList<string> Cast { get; init; } = [];

    /// <summary>Id of the entry the scene is about, or empty.</summary>
    public string FocusEntityId { get; init; } = string.Empty;

    /// <summary>Story date as written, or empty.</summary>
    public string DateStart { get; init; } = string.Empty;
    public string DateEnd { get; init; } = string.Empty;

    /// <summary>"Flashback", "FlashForward", "Parallel", "Frame", "Dream",
    /// "TimeSkip", or empty for a scene that simply happens next.</summary>
    public string NarrativeMode { get; init; } = string.Empty;

    /// <summary>Containing chapter's act label, or empty when that chapter has no act.</summary>
    public string Act { get; init; } = string.Empty;

    /// <summary>The writer's own typed fields on this scene.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}
