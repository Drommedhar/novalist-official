using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Core.Services;
using Novalist.Mobile.Services;
#if IOS
using CoreGraphics;
using UIKit;
using UniformTypeIdentifiers;
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private static string ArgString(JsonElement args, int index)
    {
        if (args.ValueKind != JsonValueKind.Array || index >= args.GetArrayLength()) return "";
        var el = args[index];
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    }

    private static List<string> ArgStrings(JsonElement args, int index)
    {
        var list = new List<string>();
        if (args.ValueKind != JsonValueKind.Array || index >= args.GetArrayLength()) return list;
        var el = args[index];
        if (el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "");
        return list;
    }

    private static string[] ManuscriptExtensions(JsonElement args) =>
        ArgStrings(args, 2)
            .Select(raw => raw.Trim().TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0 &&
                extension.All(char.IsAsciiLetterOrDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static FilePickerFileType? ManuscriptFileTypes(IEnumerable<string> extensions)
    {
#if IOS
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            // Prefer a system-declared type (DOCX, EPUB, RTF, and so on), while
            // still getting an extension-specific UTI for every runtime format.
            // Info.plist imports Scrivener's types so .scriv is understood as a
            // package and .scrivx as XML; Novalist does not claim ownership.
            using var type = UTType.CreateFromExtension(
                extension,
                extension == "scriv" ? UTTypes.Package : UTTypes.Data);
            if (string.IsNullOrEmpty(type?.Identifier)) return null;
            identifiers.Add(type.Identifier);
        }

        if (identifiers.Count == 0) return null;
        return new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.iOS] = identifiers
        });
#else
        return null;
#endif
    }

    // Absolute folder of the open project; set via setProjectRoot on project open.
    private string? _projectRoot;

    private async Task<string?> ReadProjectAssetAsync(string relative)
    {
        if (string.IsNullOrEmpty(_projectRoot) || string.IsNullOrEmpty(relative)) return null;
        try
        {
            var rootFull = Path.GetFullPath(_projectRoot);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            // Never serve outside the project folder.
            if (!full.StartsWith(rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)) return null;
            if (!await _files.ExistsAsync(full)) return null;
            return await new IosFileAccessCoordinator().ReadAsync(full,
                path => MobileAssetReader.ReadDataUri(path, MimeForExtension(path))).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string MimeForExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".mp4" or ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };

    private async Task<object?> InvokeHostAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "bridgeReady":
                _rendererReady.TrySetResult();
                return null;
            case "bridgeHealth":
                if (_bridgeFailed != 0) throw new IOException("The editor connection stopped.");
                return null;
            case "backgroundSaveCompleted":
                return args.ValueKind == JsonValueKind.Array && args.GetArrayLength() == 2
                    && args[0].TryGetInt32(out var requestId)
                    && CompleteBackgroundSave(requestId, args[1].ValueKind == JsonValueKind.True);
            case "microphoneStart": return await _microphone.StartAsync(_cts.Token);
            case "microphoneRead": return await _microphone.ReadAsync(_cts.Token);
            case "microphoneStop": return await _microphone.StopAsync();
            case "pickFolder":
                return await PickFolderAsync();
            case "releasePickedFile":
                _manuscriptAccess.Release(ArgString(args, 0));
                return null;
            case "defaultProjectRoot":
                return DefaultProjectRoot();
            case "beginProjectAccess":
                // Mirror the MAS contract: resolve the stored bookmark and start
                // access; false lets the renderer re-prompt for the folder.
                return SecurityScopedFolders.BeginAccess(ArgString(args, 0));
            case "resolveStoredProjectPath":
                return ResolveStoredProjectPath(ArgString(args, 0));
            case "endProjectAccess":
                SecurityScopedFolders.EndAccess(ArgString(args, 0));
                return null;
            case "setProjectRoot":
                _projectRoot = ArgString(args, 0);
                IosStoredPathResolver.ActiveProjectPath = _projectRoot;
                SecurityScopedFolders.ReleaseExcept(_projectRoot);
                if (!string.IsNullOrEmpty(_projectRoot))
                    await IosStoredPathResolver.OwnedPaths.RecordAsync(_projectRoot).ConfigureAwait(false);
                return null;
            case "readProjectImage":
            case "readProjectAsset":
                return await ReadProjectAssetAsync(ArgString(args, 0));
            case "pickFile":
                return await PickFileAsync(args);
            case "saveFile":
                return _exports.Create(ArgString(args, 0));
            case "shareExport":
                return await ShareExportAsync(args);
            case "releaseExport":
                await Task.Run(() => _exports.Release(ArgString(args, 0))).ConfigureAwait(false);
                return null;
            case "openExternal":
                return await OpenExternalAsync(args);
            case "copyText":
                await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(ArgString(args, 0)))
                    .ConfigureAwait(false);
                return null;
            case "revealPath":
                return false;          // no file-manager reveal on iOS
            case "readClipboardImage":
                return null;           // MAUI Clipboard is text-only
            case "setNavVisible":
                return await SetNavVisibleAsync(args);
            case "setSidebarTitles":
                return await SetSidebarTitlesAsync(args);
            case "setSidebarCollapsed":
                return await SetSidebarCollapsedAsync(args);
            case "setSidebarSelection":
                return await SetSidebarSelectionAsync(args);
            case "requestLayout":
                return await RequestLayoutAsync();
            case "setPlanningMenuOpen":
                return await SetPlanningMenuOpenAsync(args);
            case "setSelectedTab":
                return await SetSelectedTabAsync(args);
            case "setTabTitles":
                return await SetTabTitlesAsync(args);
            default:
                return null;           // unknown / JS-side no-ops
        }
    }

    private async Task<object?> PickFolderAsync()
    {
        // Real external-folder picker: the iOS document picker returns a
        // security-scoped folder URL (e.g. an iCloud/Files/Working-Copy repo
        // folder). SecurityScopedFolders persists a bookmark and keeps the
        // scope open so the backend can read/write it. Null on cancel.
        return await SecurityScopedFolders.PickFolderAsync().ConfigureAwait(false);
    }

    private static string? ResolveStoredProjectPath(string storedPath)
    {
        var resolver = new IosStoredPathResolver();
        var resolved = resolver.Resolve(storedPath);
        try { return resolved; }
        finally
        {
            if (resolved != null) resolver.Release(resolved);
        }
    }

    private static object? DefaultProjectRoot()
    {
        // Where a new project goes when the writer does not say otherwise:
        // Novalist's own folder in the Files app. Offering it means the
        // common path never involves the picker at all, and a project made
        // this way needs no grant to be read back on the next launch.
        var documents = AppFolders.Documents;
        if (string.IsNullOrEmpty(documents)) return null;
        Directory.CreateDirectory(documents);
        return documents;
    }

    private async Task<object?> PickFileAsync(JsonElement args)
    {
        // Images get the photo-library / Files choice (ImagePicking): the
        // document picker alone cannot see the camera roll. args[2] carries
        // the localized sheet labels (photos, files, cancel), in that order.
        var mode = ArgString(args, 1);
        if (mode == "images")
        {
            var labels = ArgStrings(args, 2);
            return await ImagePicking.PickImageAsync(
                ArgString(args, 0),
                labels.ElementAtOrDefault(0) ?? "Photo Library",
                labels.ElementAtOrDefault(1) ?? "Browse Files",
                labels.ElementAtOrDefault(2) ?? "Cancel").ConfigureAwait(false);
        }
        var options = new PickOptions { PickerTitle = ArgString(args, 0) };
        if (mode == "manuscript")
        {
            var extensions = ManuscriptExtensions(args);
            options.FileTypes = ManuscriptFileTypes(extensions);
            // An absent/malformed backend list must never turn the
            // manuscript chooser into an unrestricted file picker.
            if (options.FileTypes == null) return null;
            return await SecurityScopedFolders.PickManuscriptAsync(
                options.PickerTitle ?? "",
                options.FileTypes.Value,
                extensions,
                ArgString(args, 3), _manuscriptAccess).ConfigureAwait(false);
        }
        var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options))
            .ConfigureAwait(false);
        return result?.FullPath;
    }

    private async Task<object?> ShareExportAsync(JsonElement args)
    {
        var path = ArgString(args, 0);
        var shared = await Task.Run(() => _exports.PrepareShare(path)).ConfigureAwait(false);
        return await ExportSharing.ShareAsync(shared).ConfigureAwait(false);
    }

    private async Task<object?> OpenExternalAsync(JsonElement args)
    {
        var target = ArgString(args, 0);
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.OpenAsync(target)).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
}
