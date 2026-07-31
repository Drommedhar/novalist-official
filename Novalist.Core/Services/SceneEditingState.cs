namespace Novalist.Core.Services;

/// <summary>
/// Which scene the writer has open, and whether it has edits not yet saved.
///
/// Nothing outside the editor knew this. An extension running a pass over the
/// manuscript - a cleanup, an importer, a generated draft - would write over
/// whichever scene the writer happened to be typing in, and the editor's next
/// autosave would write back over that. Whichever landed second won, and the
/// other person's work was gone with no error anywhere.
///
/// The renderer is the only thing that can know this, so it says so. The guard
/// is deliberately conservative: an extension is told the scene is busy from
/// the moment it is opened with unsaved changes until it is saved or closed.
/// </summary>
public sealed class SceneEditingState
{
    private readonly object _lock = new();
    private string? _chapterGuid;
    private string? _sceneId;
    private bool _dirty;

    /// <summary>
    /// Records what the editor is doing. A null scene means nothing is open.
    /// </summary>
    public void Set(string? chapterGuid, string? sceneId, bool dirty)
    {
        lock (_lock)
        {
            _chapterGuid = chapterGuid;
            _sceneId = sceneId;
            // Nothing open cannot be dirty, whatever the caller says.
            _dirty = dirty && !string.IsNullOrEmpty(sceneId);
        }
    }

    /// <summary>
    /// True when this scene is open with unsaved changes, so writing to it
    /// would race the editor.
    /// </summary>
    public bool IsBusy(string chapterGuid, string sceneId)
    {
        lock (_lock)
        {
            return _dirty
                && string.Equals(_sceneId, sceneId, StringComparison.Ordinal)
                && string.Equals(_chapterGuid, chapterGuid, StringComparison.Ordinal);
        }
    }

    /// <summary>The scene the editor has open, or null. For diagnostics.</summary>
    public (string? ChapterGuid, string? SceneId, bool Dirty) Current
    {
        get { lock (_lock) { return (_chapterGuid, _sceneId, _dirty); } }
    }
}
