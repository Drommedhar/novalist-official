namespace Novalist.Core.Services;

/// <summary>What a cleanup pass did, or would do.</summary>
public sealed class CleanupReport
{
    /// <summary>Scenes the pass looked at.</summary>
    public int ScenesConsidered { get; set; }

    /// <summary>Scenes it changed, or would change.</summary>
    public int ScenesChanged { get; set; }

    /// <summary>Titles of the scenes it would change, so a preview can name them.</summary>
    public List<string> ChangedTitles { get; set; } = [];
}

/// <summary>
/// Runs a cleanup pass over the manuscript.
///
/// Auto-replacements fire while typing and skip pasted text on purpose, so a
/// chapter written elsewhere and pasted in keeps its straight quotes and its
/// double spaces for good. Find and Replace can be pointed at each of them one
/// pattern at a time - if the writer knows which ones to look for.
/// </summary>
public class CleanupService(IProjectService projectService)
{
    private readonly IProjectService _projectService = projectService;

    /// <summary>
    /// What the pass would change, without changing anything.
    ///
    /// A pass that rewrites every scene in a book is not something to find out
    /// about afterwards, which is why this exists separately.
    /// </summary>
    public Task<CleanupReport> PreviewAsync(
        CleanupOptions options, IReadOnlyList<string>? chapterGuids = null,
        CancellationToken cancellationToken = default)
        => WalkAsync(options, chapterGuids, write: false, null, cancellationToken);

    /// <summary>
    /// Runs the pass and writes the scenes it changed.
    ///
    /// A snapshot per changed scene, the same as Replace All: this rewrites the
    /// prose itself, and the writer has to be able to get it back.
    /// </summary>
    public Task<CleanupReport> RunAsync(
        CleanupOptions options, IReadOnlyList<string>? chapterGuids = null,
        ISnapshotService? snapshots = null, CancellationToken cancellationToken = default)
        => WalkAsync(options, chapterGuids, write: true, snapshots, cancellationToken);

    private async Task<CleanupReport> WalkAsync(
        CleanupOptions options, IReadOnlyList<string>? chapterGuids, bool write,
        ISnapshotService? snapshots, CancellationToken cancellationToken)
    {
        var report = new CleanupReport();
        if (options.Rules.Count == 0) return report;

        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            // No chapters named means the whole book, which is what a pass
            // called "clean up the manuscript" has to mean by default.
            if (chapterGuids is { Count: > 0 } && !chapterGuids.Contains(chapter.Guid)) continue;

            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.ScenesConsidered++;

                var html = await _projectService.ReadSceneContentAsync(chapter, scene)
                    .ConfigureAwait(false);
                var cleaned = ProseCleanup.Apply(html, options);
                if (string.Equals(html, cleaned, StringComparison.Ordinal)) continue;

                report.ScenesChanged++;
                report.ChangedTitles.Add(scene.Title);

                if (!write) continue;
                if (snapshots != null)
                    await snapshots.TakeAsync(chapter, scene, "Auto-snapshot before cleanup")
                        .ConfigureAwait(false);
                await _projectService.WriteSceneContentAsync(chapter, scene, cleaned)
                    .ConfigureAwait(false);
            }
        }

        if (write && report.ScenesChanged > 0)
            await _projectService.SaveScenesAsync().ConfigureAwait(false);

        return report;
    }
}
