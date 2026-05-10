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
}

public sealed class FindMatch
{
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
