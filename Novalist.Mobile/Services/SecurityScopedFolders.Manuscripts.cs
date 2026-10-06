using System.Text.Json;
using Foundation;
using Microsoft.Maui.Storage;
using UIKit;
using UniformTypeIdentifiers;

namespace Novalist.Mobile.Services;

public static partial class SecurityScopedFolders
{
    /// <summary>
    /// Present the one manuscript chooser with both folders and the exact file
    /// types advertised by the backend. The selected security scope stays open
    /// until <see cref="ReleaseTemporaryManuscriptAccess"/> is called. A directly
    /// selected .scrivx receives only file access from iOS, so it is returned only
    /// after the writer grants its exact parent folder in a second prompt.
    /// </summary>
    public static Task<string?> PickManuscriptAsync(
        string title,
        IEnumerable<string> allowedTypeIdentifiers,
        IEnumerable<string> allowedExtensions,
        string scrivenerAccessTitle)
    {
        var tcs = new TaskCompletionSource<string?>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var extensions = allowedExtensions
                    .Select(extension => extension.Trim().TrimStart('.').ToLowerInvariant())
                    .Where(extension => extension.Length > 0 &&
                        extension.All(char.IsAsciiLetterOrDigit))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (extensions.Count == 0) { tcs.TrySetResult(null); return; }

                var types = new Dictionary<string, UTType>(StringComparer.Ordinal);
                foreach (var identifier in allowedTypeIdentifiers)
                {
                    var type = UTType.CreateFromIdentifier(identifier);
                    if (type != null) types[type.Identifier] = type;
                }
                types[UTTypes.Folder.Identifier] = UTTypes.Folder;
                if (types.Count == 1) { tcs.TrySetResult(null); return; }

                PresentDocumentPicker(types.Values, title, initialDirectory: null, selectedUrl => CompleteManuscriptSelection(selectedUrl, extensions, tcs, title, scrivenerAccessTitle));
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    /// <summary>Stop the temporary scope held for an imported manuscript.</summary>
    public static void ReleaseTemporaryManuscriptAccess(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        NSUrl? url;
        lock (Gate)
        {
            if (!TemporaryManuscripts.Remove(path, out url)) return;
        }
        try { url.StopAccessingSecurityScopedResource(); }
        catch { /* already released */ }
    }

    private static void PresentScrivenerAccessPicker(
        string projectRoot,
        string title,
        Func<NSUrl?, Action?> completed)
    {
        var types = new List<UTType> { UTTypes.Folder };
        var scrivenerPackage = UTType.CreateFromExtension("scriv", UTTypes.Package);
        if (scrivenerPackage != null) types.Add(scrivenerPackage);
        var parent = Path.GetDirectoryName(projectRoot);
        PresentDocumentPicker(
            types,
            title,
            string.IsNullOrEmpty(parent) ? null : NSUrl.FromFilename(parent),
            completed);
    }

    private static void PresentDocumentPicker(
        IEnumerable<UTType> allowedTypes,
        string title,
        NSUrl? initialDirectory,
        Func<NSUrl?, Action?> completed)
    {
        var picker = new UIDocumentPickerViewController(allowedTypes.ToArray())
        {
            AllowsMultipleSelection = false,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            DirectoryUrl = initialDirectory
        };
        var finished = false;
        void Finish(NSUrl? url)
        {
            if (finished) return;
            finished = true;
            Action? afterDismissed;
            try { afterDismissed = completed(url); }
            catch { return; }
            if (afterDismissed == null) return;

            try
            {
                var presenter = picker.PresentingViewController;
                if (presenter != null)
                    presenter.DismissViewController(true, afterDismissed);
                else
                    MainThread.BeginInvokeOnMainThread(afterDismissed);
            }
            catch { MainThread.BeginInvokeOnMainThread(afterDismissed); }
        }
        picker.DidPickDocumentAtUrls += (_, e) =>
            Finish(e.Urls.Length > 0 ? e.Urls[0] : null);
        picker.WasCancelled += (_, _) => Finish(null);

        var top = TopViewController();
        if (top == null) { Finish(null); return; }
        top.PresentViewController(picker, true, null);
    }

    private static bool HoldTemporaryAccess(string selectionPath, NSUrl accessUrl)
    {
        var started = false;
        NSUrl? previous = null;
        try
        {
            started = accessUrl.StartAccessingSecurityScopedResource();
            if (!started) return IsInsideAppContainer(selectionPath);

            lock (Gate)
            {
                TemporaryManuscripts.Remove(selectionPath, out previous);
                TemporaryManuscripts[selectionPath] = accessUrl;
            }
            if (previous != null)
            {
                try { previous.StopAccessingSecurityScopedResource(); }
                catch { /* already released */ }
            }
            return true;
        }
        catch
        {
            if (started)
            {
                try { accessUrl.StopAccessingSecurityScopedResource(); }
                catch { /* best-effort rollback */ }
            }
            if (previous != null)
            {
                try { previous.StopAccessingSecurityScopedResource(); }
                catch { /* best-effort rollback */ }
            }
            lock (Gate)
            {
                if (TemporaryManuscripts.TryGetValue(selectionPath, out var current) &&
                    ReferenceEquals(current, accessUrl))
                    TemporaryManuscripts.Remove(selectionPath);
            }
            return false;
        }
    }

    private static bool IsInsideAppContainer(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return IsInside(full, FileSystem.Current.AppDataDirectory) ||
                IsInside(full, FileSystem.Current.CacheDirectory) ||
                IsInside(full, AppFolders.Documents);
        }
        catch { return false; }
    }

    private static bool IsInside(string fullPath, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.Ordinal) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static Action? CompleteManuscriptSelection(NSUrl? selectedUrl, HashSet<string> extensions,
        TaskCompletionSource<string?> tcs, string title, string scrivenerAccessTitle)
    {
                    try
                    {
                        var selectedPath = selectedUrl?.Path;
                        if (selectedUrl == null || string.IsNullOrEmpty(selectedPath))
                        {
                            tcs.TrySetResult(null);
                            return null;
                        }

                        // UTIs express conformance, not an exact suffix: asking
                        // for public.plain-text can otherwise admit many files
                        // beyond .txt. Folders remain available for suffixless
                        // Scrivener projects and are validated by the backend.
                        var isDirectory = selectedUrl.HasDirectoryPath || Directory.Exists(selectedPath);
                        var selectedExtension = Path.GetExtension(selectedPath).TrimStart('.');
                        if (!isDirectory && !extensions.Contains(selectedExtension))
                        {
                            tcs.TrySetResult(null);
                            return null;
                        }

                        if (!Path.GetExtension(selectedPath).Equals(".scrivx", StringComparison.OrdinalIgnoreCase))
                        {
                            tcs.TrySetResult(HoldTemporaryAccess(selectedPath, selectedUrl) ? selectedPath : null);
                            return null;
                        }

                        var projectRoot = Path.GetDirectoryName(selectedPath);
                        if (string.IsNullOrEmpty(projectRoot))
                        {
                            tcs.TrySetResult(null);
                            return null;
                        }

                        // Present the exact-parent access prompt only from the first
                        // picker's dismissal completion, never while it is still on
                        // screen or in the middle of its automatic dismissal.
                        return () =>
                        RequestScrivenerParentAccess(projectRoot, selectedPath, tcs, title, scrivenerAccessTitle);
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                        return null;
                    }
                }

    private static void RequestScrivenerParentAccess(string projectRoot, string selectedPath,
        TaskCompletionSource<string?> tcs, string title, string scrivenerAccessTitle)
    {
                            try
                            {
                                PresentScrivenerAccessPicker(
                                    projectRoot,
                                    string.IsNullOrWhiteSpace(scrivenerAccessTitle)
                                        ? title
                                        : scrivenerAccessTitle,
                                    accessUrl =>
                                    {
                                        try
                                        {
                                            var accessPath = accessUrl?.Path;
                                            if (accessUrl == null || string.IsNullOrEmpty(accessPath) ||
                                                !SamePath(accessPath, projectRoot) ||
                                                !HoldTemporaryAccess(selectedPath, accessUrl))
                                            {
                                                tcs.TrySetResult(null);
                                                return null;
                                            }
                                            // The exact manifest remains authoritative even
                                            // if its now-accessible parent has other binders.
                                            tcs.TrySetResult(selectedPath);
                                            return null;
                                        }
                                        catch
                                        {
                                            tcs.TrySetResult(null);
                                            return null;
                                        }
                                    });
                            }
                            catch { tcs.TrySetResult(null); }
                        }
}
