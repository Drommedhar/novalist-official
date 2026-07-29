using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Extensions contribute editor inline actions (e.g. AI rewrite, expand,
/// describe). Most operate on the user's current text selection, and the host
/// surfaces those in the editor context menu when text is selected.
///
/// An action that sets <see cref="InlineActionDescriptor.AllowsEmptySelection"/>
/// is also offered at a bare caret, and reached from the editor's slash menu.
/// That is what a "continue writing from here" or a typed beat directive needs:
/// there is nothing selected, and the interesting context is what comes before
/// the caret rather than what is inside a selection.
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

    /// <summary>
    /// Whether this action makes sense with nothing selected.
    ///
    /// False by default, so an action written before this existed keeps needing
    /// a selection rather than suddenly appearing at every caret with an empty
    /// <see cref="InlineActionRequest.SelectedText"/> it was never written to
    /// handle. Actions that set it are also listed in the slash menu.
    /// </summary>
    public bool AllowsEmptySelection { get; init; }

    /// <summary>
    /// Optional slash-menu keyword, without the slash. Empty falls back to the
    /// part of <see cref="Id"/> after the last dot, so "ai.continue" is typed
    /// as "/continue".
    /// </summary>
    public string SlashKeyword { get; init; } = string.Empty;
}

public sealed class InlineActionRequest
{
    /// <summary>The currently selected text (plain text).</summary>
    public string SelectedText { get; init; } = string.Empty;

    /// <summary>Active scene id, or empty when no scene context.</summary>
    public string SceneId { get; init; } = string.Empty;

    /// <summary>Active chapter guid, or empty when no scene context.</summary>
    public string ChapterGuid { get; init; } = string.Empty;

    /// <summary>
    /// The prose immediately before the caret, up to a few hundred words.
    ///
    /// This is what a continue-writing action continues from. With a selection
    /// it is the text before the selection starts; at a bare caret it is
    /// everything the writer has written up to that point in the scene, which
    /// is otherwise unreachable - the request used to carry the selection and
    /// nothing else.
    /// </summary>
    public string PrecedingText { get; init; } = string.Empty;

    /// <summary>
    /// What the writer typed after the slash, when the action came from the
    /// slash menu. Empty otherwise.
    ///
    /// This carries a beat directive: typing "/beat she finally admits it" gives
    /// the action "she finally admits it" to write towards.
    /// </summary>
    public string Directive { get; init; } = string.Empty;
}

public enum InlineActionDisposition
{
    /// <summary>Replace the user's selection with <see cref="InlineActionResult.Text"/>.</summary>
    ReplaceSelection,
    /// <summary>Insert <see cref="InlineActionResult.Text"/> immediately after the selection,
    /// leaving the original selection intact.</summary>
    InsertAfterSelection,

    /// <summary>
    /// Insert at the caret, replacing nothing. What a continue-writing action
    /// wants: the writer stopped mid-scene and the prose carries on from there.
    /// With a selection this behaves as <see cref="InsertAfterSelection"/>.
    /// </summary>
    InsertAtCaret,
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
