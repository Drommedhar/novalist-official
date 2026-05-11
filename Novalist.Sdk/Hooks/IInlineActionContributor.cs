using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Extensions contribute editor inline actions (e.g. AI rewrite, expand,
/// describe) that operate on the user's current text selection. The host
/// surfaces them in the editor context menu when text is selected. The
/// contributor performs the transformation and returns text the host inserts
/// or appends.
/// </summary>
public interface IInlineActionContributor
{
    /// <summary>Returns the actions this contributor provides.</summary>
    IReadOnlyList<InlineActionDescriptor> GetInlineActions();

    /// <summary>
    /// Executes the action identified by <see cref="InlineActionDescriptor.Id"/>
    /// against the user's current selection.
    /// </summary>
    Task<InlineActionResult> ExecuteAsync(string actionId, InlineActionRequest request, CancellationToken cancellationToken);
}

public sealed class InlineActionDescriptor
{
    /// <summary>Stable id used to dispatch the action (e.g. "ai.rewrite").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Localized label shown in the context menu.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional group label used as a submenu header
    /// (e.g. "AI"). Items with the same group are nested together.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>Optional unicode glyph rendered before the label.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Lower values appear first. Default 100.</summary>
    public int Priority { get; init; } = 100;
}

public sealed class InlineActionRequest
{
    /// <summary>The currently selected text (plain text).</summary>
    public string SelectedText { get; init; } = string.Empty;

    /// <summary>Active scene id, or empty when no scene context.</summary>
    public string SceneId { get; init; } = string.Empty;

    /// <summary>Active chapter guid, or empty when no scene context.</summary>
    public string ChapterGuid { get; init; } = string.Empty;
}

public enum InlineActionDisposition
{
    /// <summary>Replace the user's selection with <see cref="InlineActionResult.Text"/>.</summary>
    ReplaceSelection,
    /// <summary>Insert <see cref="InlineActionResult.Text"/> immediately after the selection,
    /// leaving the original selection intact.</summary>
    InsertAfterSelection,
}

public sealed class InlineActionResult
{
    /// <summary>Generated text. Empty + non-null Error means the action failed.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>How the host should apply <see cref="Text"/>.</summary>
    public InlineActionDisposition Disposition { get; init; } = InlineActionDisposition.ReplaceSelection;

    /// <summary>Optional error message. Null on success.</summary>
    public string? Error { get; init; }
}
