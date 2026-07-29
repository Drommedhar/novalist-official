using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>
/// What happened when a scene was saved. <see cref="Conflicted"/> means the file
/// changed on disk since the editor read it, so the save was refused and
/// <see cref="DiskHtml"/> carries what is actually there.
/// </summary>
public sealed record SceneSaveOutcome(
    bool Conflicted,
    string Hash,
    string? DiskHtml);

/// <summary>One row of a two-way merge: what the writer has, what is on disk,
/// and whether they agree. A row where both sides are equal needs no choosing.
/// </summary>
public sealed record MergeRow(string? Mine, string? Theirs, string State);

/// <summary>
/// Stops a scene save from destroying an edit that arrived from somewhere else.
///
/// Novalist keeps projects in a plain folder, which people put in Dropbox,
/// iCloud, OneDrive or Syncthing. Two machines editing the same scene is not an
/// exotic case, it is Tuesday. The write path used to overwrite whatever was on
/// disk without looking, so the loser of that race lost their work silently and
/// with no way to find out.
///
/// The check is optimistic: the editor remembers the hash of what it read, and a
/// save carrying a stale hash is refused rather than merged automatically. An
/// automatic merge of prose is a bad idea — a sentence spliced from two drafts
/// reads like neither — so the writer is shown both and chooses.
/// </summary>
public sealed class SceneConflictGuard
{
    private readonly IProjectService _projectService;
    private readonly ISnapshotService? _snapshots;

    /// <param name="snapshots">Optional. When present, resolving a conflict
    /// snapshots both sides first, so a wrong choice at the merge dialog is
    /// recoverable rather than final.</param>
    public SceneConflictGuard(IProjectService projectService, ISnapshotService? snapshots = null)
    {
        _projectService = projectService;
        _snapshots = snapshots;
    }

    /// <summary>The fingerprint of what is on disk for this scene right now.
    /// An absent file hashes as empty, which is what a brand-new scene reads as,
    /// so creating one is never mistaken for a conflict.</summary>
    public async Task<string> DiskHashAsync(ChapterData chapter, SceneData scene)
        => ContentHasher.Hash(await _projectService.ReadSceneContentAsync(chapter, scene));

    /// <summary>
    /// Saves a scene unless the file changed under it.
    ///
    /// <paramref name="expectedHash"/> is what the editor last saw. Null or empty
    /// skips the check entirely, which is how every caller that has no business
    /// knowing about conflicts (imports, bulk operations, restores) keeps
    /// working unchanged.
    /// </summary>
    public async Task<SceneSaveOutcome> SaveAsync(
        ChapterData chapter, SceneData scene, string html, string? expectedHash)
    {
        if (!string.IsNullOrEmpty(expectedHash))
        {
            var diskHtml = await _projectService.ReadSceneContentAsync(chapter, scene);
            var diskHash = ContentHasher.Hash(diskHtml);
            if (!string.Equals(diskHash, expectedHash, StringComparison.Ordinal))
            {
                // Someone else's save landed first. Refuse, and hand back what
                // they wrote so the writer can see it rather than guess.
                return new SceneSaveOutcome(true, diskHash, diskHtml);
            }
        }

        await _projectService.WriteSceneContentAsync(chapter, scene, html);
        return new SceneSaveOutcome(false, ContentHasher.Hash(html), null);
    }

    /// <summary>
    /// Writes the writer's chosen resolution, keeping both original versions
    /// first. The merge dialog is the one place in Novalist where a click can
    /// discard a paragraph someone wrote, so both sides are snapshotted before
    /// anything is overwritten.
    /// </summary>
    public async Task<string> ResolveAsync(
        ChapterData chapter, SceneData scene, string mergedHtml)
    {
        if (_snapshots != null)
        {
            // On-disk side first: taking it means reading the file, and the
            // resolution is about to replace it.
            await _snapshots.TakeAsync(chapter, scene, "Before merge: version on disk");
            await _projectService.WriteSceneContentAsync(chapter, scene, mergedHtml);
            await _snapshots.TakeAsync(chapter, scene, "After merge");
        }
        else
        {
            await _projectService.WriteSceneContentAsync(chapter, scene, mergedHtml);
        }

        return ContentHasher.Hash(mergedHtml);
    }

    /// <summary>
    /// The two versions lined up for the merge dialog, as plain prose rather
    /// than HTML — a writer choosing between two drafts is reading sentences,
    /// not markup, and a tag-level diff would bury the actual difference.
    /// </summary>
    public static IReadOnlyList<MergeRow> Rows(string mineHtml, string theirsHtml)
        => [.. TextDiff
            .ComputePaired(TextDiff.StripHtml(mineHtml), TextDiff.StripHtml(theirsHtml))
            .Select(row => new MergeRow(row.LeftText, row.RightText, StateOf(row)))];

    private static string StateOf(PairedDiffRow row)
    {
        if (row.IsEqual) return "equal";
        if (row.IsChanged) return "changed";
        return row.IsLeftOnly ? "mine" : "theirs";
    }
}
