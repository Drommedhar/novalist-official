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
    /// Begin security-scoped access to a previously-picked path. Returns true when
    /// access is available; false only when there is no usable bookmark (exact or
    /// an ancestor's), so the renderer can re-prompt for the folder.
    /// </summary>
    public static bool BeginAccess(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        lock (Gate)
        {
            if (Active.ContainsKey(path)) return true;
        }

        var (bookmark, _) = FindBookmark(path);
        if (bookmark == null) return false;
        try
        {
            var data = new NSData(bookmark, NSDataBase64DecodingOptions.None);
            var url = NSUrl.FromBookmarkData(
                data, NSUrlBookmarkResolutionOptions.WithoutUI, null, out _, out var err);
            if (url == null || err != null) return false;
            if (!url.StartAccessingSecurityScopedResource()) return false;
            lock (Gate) Active[path] = url;
            return true;
        }
        catch
        {
            return false;
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
