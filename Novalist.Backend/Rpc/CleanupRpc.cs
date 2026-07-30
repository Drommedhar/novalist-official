using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>What a cleanup pass would do, or did.</summary>
public sealed record CleanupReportDto(int ScenesConsidered, int ScenesChanged, string[] ChangedTitles);

/// <summary>
/// A cleanup pass over prose the writer already has.
///
/// Auto-replacements fire while typing and skip pasted text on purpose, so a
/// chapter written elsewhere and pasted in keeps its straight quotes and its
/// double spaces for good.
/// </summary>
public class CleanupRpc(Workspace workspace)
{
    private readonly Workspace _workspace = workspace;

    private CleanupOptions Options(string[] rules) => new()
    {
        Rules = [.. rules.Select(ParseRule).Where(r => r.HasValue).Select(r => r!.Value)],
        // The book's writing language, so a German manuscript is smartened to
        // low-9 quotes rather than to the English pair.
        Language = _workspace.Settings.Effective.AutoReplacementLanguage
    };

    /// <summary>An unknown rule is ignored rather than fatal: an older renderer
    /// naming a rule this build dropped should still clean up the rest.</summary>
    private static CleanupRule? ParseRule(string name)
        => Enum.TryParse<CleanupRule>(name, ignoreCase: true, out var rule) ? rule : null;

    private static CleanupReportDto ToDto(CleanupReport report)
        => new(report.ScenesConsidered, report.ScenesChanged, [.. report.ChangedTitles]);

    /// <summary>
    /// What the pass would change, changing nothing.
    ///
    /// A pass that rewrites every scene in a book is not something a writer
    /// should find out about afterwards.
    /// </summary>
    [JsonRpcMethod("cleanup/preview")]
    public async Task<CleanupReportDto> PreviewAsync(
        string[] rules, string[]? chapterGuids = null, CancellationToken cancellationToken = default)
        => ToDto(await new CleanupService(_workspace.Projects)
            .PreviewAsync(Options(rules), chapterGuids, cancellationToken));

    /// <summary>
    /// Runs the pass. A snapshot is taken of every scene it changes, the same
    /// as Replace All - this rewrites the prose itself.
    /// </summary>
    [JsonRpcMethod("cleanup/run")]
    public async Task<CleanupReportDto> RunAsync(
        string[] rules, string[]? chapterGuids = null, CancellationToken cancellationToken = default)
    {
        var snapshots = new SnapshotService(_workspace.Projects, _workspace.FileService);
        return ToDto(await new CleanupService(_workspace.Projects)
            .RunAsync(Options(rules), chapterGuids, snapshots, cancellationToken));
    }
}
