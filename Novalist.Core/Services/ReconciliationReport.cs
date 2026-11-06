using Novalist.Core.Models;

namespace Novalist.Core.Services;

public enum ChapterChangeKind { New, Renamed, Deleted }
public enum SceneChangeKind { New, Moved, Renamed, Deleted }

/// <summary>A detected chapter-level difference between disk and cache.</summary>
public sealed record ChapterChange(ChapterChangeKind Kind, string Guid, string FolderName, string? OldFolderName = null);

/// <summary>A detected scene-level difference between disk and cache.</summary>
public sealed record SceneChange(
    SceneChangeKind Kind,
    string Id,
    string FileName,
    string ChapterGuid,
    string? FromChapterGuid = null,
    string? OldFileName = null);

/// <summary>An ambiguity the reconciler cannot resolve automatically (surfaced for review).</summary>
public sealed record ReconciliationConflict(string Kind, string Detail);

/// <summary>Read-only summary of what changed on disk relative to the cached manifest.</summary>
public sealed class ReconciliationReport
{
    public List<ChapterChange> Chapters { get; } = new();
    public List<SceneChange> Scenes { get; } = new();
    public List<ReconciliationConflict> Conflicts { get; } = new();
    internal Dictionary<string, DraftIndexEntry> RefreshedIndexEntries { get; } = new(StringComparer.Ordinal);

    public bool HasChanges => Chapters.Count > 0 || Scenes.Count > 0 || Conflicts.Count > 0;
}
