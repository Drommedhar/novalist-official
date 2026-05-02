using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Allows extensions to contribute grammar/punctuation/style checking
/// results for the scene editor. The host calls <see cref="CheckAsync"/>
/// after typing pauses (debounced). Implementations should keep prompts
/// short and return quickly for near-real-time feedback.
/// </summary>
public interface IGrammarCheckContributor
{
    /// <summary>
    /// Unique name for this contributor (shown in diagnostics).
    /// </summary>
    string GrammarCheckName { get; }

    /// <summary>
    /// Whether this contributor is currently enabled.
    /// </summary>
    bool IsGrammarCheckEnabled { get; }

    /// <summary>
    /// Called after a typing pause with the current plain text.
    /// Return a list of issues found; the host will merge them with
    /// results from other contributors.
    /// </summary>
    /// <param name="plainText">The current editor plain text.</param>
    /// <param name="language">The UI language code (e.g. "en", "de").</param>
    /// <param name="cancellationToken">Cancelled when the user types again.</param>
    Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a grammar check contribution.
/// </summary>
public sealed class GrammarCheckResult
{
    /// <summary>The issues found (empty if none).</summary>
    public List<GrammarIssue> Issues { get; init; } = [];
}

/// <summary>
/// A single grammar/punctuation/style issue.
/// </summary>
public sealed class GrammarIssue
{
    /// <summary>Zero-based character offset in the plain text.</summary>
    public int Offset { get; init; }

    /// <summary>Length of the flagged text in characters.</summary>
    public int Length { get; init; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Visual category.</summary>
    public GrammarIssueType Type { get; init; } = GrammarIssueType.Grammar;

    /// <summary>Suggested replacements (first is preferred).</summary>
    public List<string> Replacements { get; init; } = [];
}

/// <summary>
/// Categorises grammar issues for visual styling in the editor.
/// </summary>
public enum GrammarIssueType
{
    Spelling,
    Grammar,
    Style
}
