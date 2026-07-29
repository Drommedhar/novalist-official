namespace Novalist.Core.Models;

public enum FindScope
{
    /// <summary>Currently open scene only.</summary>
    CurrentScene,
    /// <summary>All scenes in the chapter that owns the current scene.</summary>
    CurrentChapter,
    /// <summary>All scenes in the active book.</summary>
    ActiveBook,
    /// <summary>All scenes across every book in the project.</summary>
    Project
}

public sealed class FindOptions
{
    public string Pattern { get; init; } = string.Empty;
    public string Replacement { get; init; } = string.Empty;
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool UseRegex { get; init; }
    public FindScope Scope { get; init; } = FindScope.ActiveBook;
    /// <summary>Optional anchor for CurrentScene / CurrentChapter scopes.</summary>
    public string? AnchorChapterGuid { get; init; }
    public string? AnchorSceneId { get; init; }

    /// <summary>
    /// Also search a scene's synopsis, its notes and its comments.
    ///
    /// Those are where a writer leaves the things they mean to come back to,
    /// and a search that cannot see them will not find them.
    /// </summary>
    public bool IncludeSceneNotes { get; init; }

    /// <summary>
    /// Also search Codex entries - names, descriptions and section prose.
    /// Reported only: renaming an entry is the Codex's own job, which carries
    /// the change through every reference.
    /// </summary>
    public bool IncludeCodex { get; init; }
}

public sealed class FindMatch
{
    /// <summary>
    /// The book the match is in. Only interesting for a whole-project search,
    /// where two books can hold a scene of the same name.
    /// </summary>
    public string BookTitle { get; init; } = string.Empty;

    /// <summary>
    /// What the match is in: <c>prose</c>, <c>synopsis</c>, <c>notes</c>,
    /// <c>comment</c> or <c>codex</c>. Only prose, synopses and notes can be
    /// replaced; the rest are reported so the writer can go and look.
    /// </summary>
    public string Field { get; init; } = "prose";

    public string ChapterGuid { get; init; } = string.Empty;
    public string ChapterTitle { get; init; } = string.Empty;
    public string SceneId { get; init; } = string.Empty;
    public string SceneTitle { get; init; } = string.Empty;
    /// <summary>0-based character index within the scene's plain text.</summary>
    public int Index { get; init; }
    public int Length { get; init; }
    /// <summary>~40 chars of text before the match.</summary>
    public string Before { get; init; } = string.Empty;
    public string MatchedText { get; init; } = string.Empty;
    /// <summary>~40 chars of text after the match.</summary>
    public string After { get; init; } = string.Empty;
}
