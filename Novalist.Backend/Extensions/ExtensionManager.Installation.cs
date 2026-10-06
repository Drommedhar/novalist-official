using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Backend.Extensions;

public sealed partial class ExtensionManager
{
    /// <summary>
    /// Discovers a newly installed extension by ID, adds it to the Extensions list,
    /// enables it, and loads + initializes it.
    /// </summary>
    public async Task DiscoverAndEnableAsync(string extensionId)
    {
        // Don't duplicate if already known
        if (Extensions.Any(e => string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase)))
            return;

        var discovered = _loader.DiscoverExtensions();
        var info = discovered.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));

        if (info == null) return;

        info.IsEnabled = true;
        _settingsService.Settings.Extensions[extensionId] = true;
        await _settingsService.SaveAsync();

        Extensions.Add(info);

        if (_loader.LoadExtension(info))
        {
            InitializeExtension(info);
        }
    }

    /// <summary>
    /// Reloads an extension from disk after its files changed underneath us (a
    /// gallery install or update wrote a new version into the extensions folder).
    /// Unloads and drops the currently-loaded instance if present, then
    /// re-discovers, enables, loads, and initializes it from the new files so the
    /// change takes effect without an app restart. No-op when the id is neither
    /// loaded nor present on disk.
    /// </summary>
    public async Task ReloadExtensionAsync(string extensionId)
    {
        var existing = Extensions.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Unloads the collectible assembly context (memory-loaded, no file
            // locks) so the freshly written DLLs can be loaded below.
            await DisableExtensionAsync(existing.Manifest.Id);
            Extensions.Remove(existing);
        }

        await DiscoverAndEnableAsync(extensionId);
    }

    /// <summary>
    /// Installs an extension from a source folder: validates its manifest, copies
    /// the folder into the extensions directory (replacing any previous install of
    /// the same id), then discovers, enables, loads, and initializes it.
    /// Returns the installed extension id.
    /// </summary>
    public async Task<string> InstallFromFolderAsync(string sourceFolder)
    {
        // The Mac App Store build has no extension feature and its UI offers no
        // way here, but the RPC method still exists - so it refuses rather than
        // writing an assembly into the container that nothing will ever load.
        if (_loader.ExtensionsDisabled)
            throw new InvalidOperationException("Extensions are not available in this edition.");

        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException("The selected folder does not exist.");

        var manifestPath = Path.Combine(sourceFolder, "extension.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The selected folder does not contain an extension.json manifest.");

        ExtensionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), InstallJsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"extension.json could not be parsed: {ex.Message}");
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id?.TrimEnd('.', ' ')))
            throw new InvalidOperationException("extension.json is missing a valid \"id\".");

        var id = manifest.Id;
        Log.Info($"Extension install from folder: id={id}.");

        // Stage the source before removing an existing installation: the selected
        // folder may be that installation or one of its subdirectories.
        var staged = Path.Combine(Path.GetTempPath(), "novalist-extension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);
        try
        {
            CopyDirectory(sourceFolder, staged);
            await RemoveInstalledAsync(id);

            var target = Path.Combine(_loader.ExtensionsDirectory, SanitizeFolderName(id));
            CopyDirectory(staged, target);

            await DiscoverAndEnableAsync(id);
            return id;
        }
        finally
        {
            Directory.Delete(staged, recursive: true);
        }
    }

    /// <summary>
    /// Uninstalls an extension: unloads it if loaded, deletes its folder from disk,
    /// and drops its persisted enable/disable state.
    /// </summary>
    public async Task UninstallAsync(string extensionId)
    {
        Log.Info($"Extension uninstall: id={extensionId}.");
        await RemoveInstalledAsync(extensionId);
        _settingsService.Settings.Extensions.Remove(extensionId);
        await _settingsService.SaveAsync();
    }

    /// <summary>
    /// Unloads (if loaded), removes from the live collection, and deletes the
    /// on-disk folder for the given extension id. No-op when not installed.
    /// </summary>
    private async Task RemoveInstalledAsync(string extensionId)
    {
        var info = Extensions.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (info == null)
            return;

        // DisableExtensionAsync unloads the assembly context (memory-loaded, so no
        // file locks are held) and persists the disabled state.
        await DisableExtensionAsync(info.Manifest.Id);
        Extensions.Remove(info);

        if (!string.IsNullOrEmpty(info.FolderPath) && Directory.Exists(info.FolderPath))
            Directory.Delete(info.FolderPath, recursive: true);
    }

    private static string SanitizeFolderName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>The on-disk directory extensions are discovered from and installed into.</summary>
    public string ExtensionsDirectory => _loader.ExtensionsDirectory;
}
