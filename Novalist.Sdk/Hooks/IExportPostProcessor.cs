namespace Novalist.Sdk.Hooks;

/// <summary>What a post-export check found.</summary>
public sealed class ExportCheckResult
{
    /// <summary>Whether the file is fit to send. False shows the messages as problems.</summary>
    public bool Ok { get; init; } = true;

    /// <summary>What is wrong, in the writer's language. One line each.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>Things worth knowing that are not problems - a page count, a file size.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Runs after an export has been written, and gets to say whether it is any
/// good.
///
/// Validating an EPUB properly means knowing the EPUB specification, which is
/// exactly the kind of knowledge that should not have to live in Novalist to be
/// available in it. The exporter's job ends at writing a correct file; deciding
/// whether a shop will accept it is somebody else's.
///
/// A processor must not modify the file. It is handed a path so it can read it,
/// and its answer is shown to the writer - silently rewriting an export the
/// writer is about to send would be the worst possible time to be clever.
/// </summary>
public interface IExportPostProcessor
{
    /// <summary>
    /// Formats this checks, by their format key ("Epub", "Pdf", "Docx", or a
    /// format an extension registered). Empty means every format.
    /// </summary>
    IReadOnlyList<string> Formats { get; }

    /// <summary>What to call this check while it runs.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Checks a written export.
    /// </summary>
    /// <param name="outputPath">Absolute path of the file just written.</param>
    /// <param name="formatKey">Which format it was written in.</param>
    Task<ExportCheckResult> CheckAsync(
        string outputPath, string formatKey, CancellationToken cancellationToken = default);
}
