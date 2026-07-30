namespace Novalist.Core.Services;

/// <summary>Which of the user's asset folders changed.</summary>
public enum UserAssetKind
{
    Themes,
    Locales,
    Analysis
}

/// <summary>
/// Watch logic for the user's Themes, Locales and Analysis folders, isolated
/// from the native <c>FileSystemWatcher</c> so it can be tested without real OS
/// events or timers.
///
/// All three folders were read once at startup and a restart was needed after
/// any change - so dropping in a theme meant relaunching to find out whether it
/// was any good, which is the wrong loop for something you iterate on. The
/// coordinator decides which files matter, which folder they belong to, and
/// coalesces a burst of events into one reload per folder.
/// </summary>
public sealed class AssetWatchCoordinator
{
    private readonly Func<IReadOnlyCollection<UserAssetKind>, Task> _reload;
    private readonly Action _scheduleFlush;
    private readonly object _gate = new();
    private readonly HashSet<UserAssetKind> _pending = [];

    /// <param name="reload">Reloads the folders named. Never called with an empty set.</param>
    /// <param name="scheduleFlush">Arms the debounce window; when it elapses the
    /// owner calls <see cref="FlushAsync"/>.</param>
    public AssetWatchCoordinator(
        Func<IReadOnlyCollection<UserAssetKind>, Task> reload, Action scheduleFlush)
    {
        _reload = reload;
        _scheduleFlush = scheduleFlush;
    }

    /// <summary>
    /// True for a file one of the loaders would actually read.
    ///
    /// A stray note, an editor's swap file or a screenshot in the folder should
    /// not cost a reload - and on Windows an editor saving one file can produce
    /// several events for temporary siblings.
    /// </summary>
    public static bool IsRelevant(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".css", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ingests a raw event for one folder. Irrelevant files are dropped;
    /// anything else marks that folder dirty and re-arms the debounce.
    /// </summary>
    public void NotifyChange(UserAssetKind kind, string path)
    {
        if (!IsRelevant(path)) return;
        lock (_gate) _pending.Add(kind);
        _scheduleFlush();
    }

    /// <summary>
    /// Called when the debounce window elapses. Reloads every folder that
    /// changed since the last flush, in one pass, and never lets a failure tear
    /// down the watch: a theme file somebody is halfway through editing is
    /// unreadable for a moment and that is not an error worth reporting.
    /// </summary>
    public async Task FlushAsync()
    {
        UserAssetKind[] due;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            due = [.. _pending];
            _pending.Clear();
        }

        try
        {
            await _reload(due);
        }
        catch
        {
            // A failed reload must never end the session's watching.
        }
    }
}
