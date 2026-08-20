using System.Text.Json;
using Foundation;
using Microsoft.Maui.Storage;
using UIKit;
using UniformTypeIdentifiers;

namespace Novalist.Mobile.Services;

/// <summary>
/// iOS counterpart of the Mac App Store security-scoped-bookmark machinery
/// (app/src/main/mac-bookmarks.ts). The renderer is sandbox-agnostic: openProject
/// calls beginProjectAccess(path) before the backend touches files and re-prompts
/// with pickFolder if it returns false. This service implements that same contract
/// natively so a project can live in an EXTERNAL folder (e.g. a Git repo a user
/// cloned with an iOS Git client) rather than only inside the app container.
///
/// A folder the user picks is only reachable while its security scope is active.
/// We capture a bookmark when the folder is first picked, persist it (keyed by
/// path), and resolve + start-accessing it before reopening the project on a later
/// launch. Bookmarks also survive the data-container UUID changing across installs,
/// which is why external folders fix the stale-recents problem.
/// </summary>
public static class SecurityScopedFolders
{
    private static readonly object Gate = new();
    // Path passed to BeginAccess -> the URL we started accessing for it (may be an
    // ancestor of the path when a parent folder was the one actually bookmarked).
    private static readonly Dictionary<string, NSUrl> Active = new();

    private static string StorePath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, "security-bookmarks.json");

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new Dictionary<string, string>();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            // A corrupt/unreadable store must never block the app - start fresh.
            return new Dictionary<string, string>();
        }
    }

    private static void Save(Dictionary<string, string> map)
    {
        try { File.WriteAllText(StorePath, JsonSerializer.Serialize(map)); }
        catch { /* best-effort persistence; a failure just means re-pick next time */ }
    }

    /// <summary>
    /// Present the iOS folder picker. On pick: start accessing the folder, capture
    /// and persist a bookmark, keep the scope active (so the immediately-following
    /// project/create or project/open can write/read), and return its path. Returns
    /// null if the user cancels.
    /// </summary>
    public static Task<string?> PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder });
                picker.AllowsMultipleSelection = false;
                picker.DidPickDocumentAtUrls += (_, e) =>
                {
                    var url = e.Urls.Length > 0 ? e.Urls[0] : null;
                    tcs.TrySetResult(url != null ? AdoptPickedFolder(url) : null);
                };
                picker.WasCancelled += (_, _) => tcs.TrySetResult(null);
                var top = TopViewController();
                if (top == null) { tcs.TrySetResult(null); return; }
                top.PresentViewController(picker, true, null);
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    // Start accessing the freshly-picked folder, persist a bookmark, and keep the
    // scope open under its own path so create/open works right away.
    private static string? AdoptPickedFolder(NSUrl url)
    {
        var path = url.Path;
        if (string.IsNullOrEmpty(path)) return null;

        var started = url.StartAccessingSecurityScopedResource();
        var data = url.CreateBookmarkData(0, Array.Empty<string>(), null, out var err);
        if (data != null && err == null)
        {
            lock (Gate)
            {
                var map = Load();
                map[path] = data.GetBase64EncodedString(NSDataBase64EncodingOptions.None);
                Save(map);
            }
        }
        if (started)
        {
            lock (Gate) Active[path] = url;
        }
        return path;
    }

    /// <summary>
    /// Make a stored path usable. Returns true when access is available at that
    /// exact path; false when the folder has moved or is out of reach, so the
    /// renderer can re-prompt for it.
    ///
    /// Not the same question as "is there a bookmark". Novalist's own Documents
    /// folder is where new projects go now, and nothing inside the app's
    /// container needs a grant to be read - answering "no bookmark, ask the
    /// writer" there put a folder picker in front of every project the app had
    /// made itself. So the bookmark is tried first, and anything it cannot
    /// account for is put to the filesystem: a folder we can already see is a
    /// folder we can already open.
    /// </summary>
    public static bool BeginAccess(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var current = ResolveCurrentPath(path);
        if (current != null) return current == path;
        return Directory.Exists(path);
    }

    /// <summary>
    /// Where a previously-picked path lives now, with its grant resumed - or null
    /// when no bookmark covers it.
    ///
    /// This is the part a stored path cannot do for itself. A bookmark tracks the
    /// folder, not the spelling of its location, so it still resolves after iOS
    /// has moved it: an app update re-creates the container under a fresh UUID
    /// and rewrites the absolute path of everything inside it, and a folder the
    /// writer moved in the Files app simply is not where it was. Resolving tells
    /// us the current address; the store is re-keyed to it so the next launch
    /// finds it directly, and a bookmark iOS reports as stale is rewritten from
    /// the resolved URL before it decays into an unusable one.
    ///
    /// The tail matters: a bookmark for a parent folder covers the projects
    /// inside it, so when the parent has moved, its children move with it.
    /// </summary>
    public static string? ResolveCurrentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Gate)
        {
            if (Active.ContainsKey(path)) return path;
        }

        var (bookmark, key) = FindBookmark(path);
        if (bookmark == null || key == null) return null;
        try
        {
            var data = new NSData(bookmark, NSDataBase64DecodingOptions.None);
            var url = NSUrl.FromBookmarkData(
                data, NSUrlBookmarkResolutionOptions.WithoutUI, null, out var stale, out var err);
            if (url == null || err != null) return null;
            if (!url.StartAccessingSecurityScopedResource()) return null;

            var home = url.Path;
            if (string.IsNullOrEmpty(home)) return null;
            if (home != key || stale) Rekey(key, home, url, stale);

            var current = home + path[key.Length..];
            lock (Gate) Active[current] = url;
            return current;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Move a bookmark to the path it now resolves to, refreshing the
    /// bookmark itself when iOS says the old one is on its way out.</summary>
    private static void Rekey(string oldKey, string newKey, NSUrl url, bool stale)
    {
        lock (Gate)
        {
            var map = Load();
            if (!map.TryGetValue(oldKey, out var data)) return;
            if (stale)
            {
                var fresh = url.CreateBookmarkData(0, Array.Empty<string>(), null, out var err);
                if (fresh != null && err == null)
                    data = fresh.GetBase64EncodedString(NSDataBase64EncodingOptions.None);
            }
            map.Remove(oldKey);
            map[newKey] = data;
            Save(map);
        }
    }

    /// <summary>Release a scoped-resource access started by BeginAccess.</summary>
    public static void EndAccess(string path)
    {
        NSUrl? url;
        lock (Gate)
        {
            if (!Active.TryGetValue(path, out url)) return;
            Active.Remove(path);
        }
        try { url.StopAccessingSecurityScopedResource(); }
        catch { /* already released */ }
    }

    // Exact bookmark for path, else the longest ancestor bookmark that contains it
    // (a folder grant covers children, e.g. a project created under a picked parent).
    private static (string? Bookmark, string? Key) FindBookmark(string path)
    {
        lock (Gate)
        {
            var map = Load();
            if (map.TryGetValue(path, out var exact)) return (exact, path);
            string? bestKey = null;
            foreach (var key in map.Keys)
            {
                var prefix = key.EndsWith('/') ? key : key + "/";
                if (path.StartsWith(prefix, StringComparison.Ordinal)
                    && (bestKey == null || key.Length > bestKey.Length))
                    bestKey = key;
            }
            return bestKey == null ? (null, null) : (map[bestKey], bestKey);
        }
    }

    private static UIViewController? TopViewController()
    {
        UIWindow? window = null;
        foreach (var w in UIApplication.SharedApplication.Windows)
        {
            if (w.IsKeyWindow) { window = w; break; }
            window ??= w;
        }
        var vc = window?.RootViewController;
        while (vc?.PresentedViewController != null) vc = vc.PresentedViewController;
        return vc;
    }
}
