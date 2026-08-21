using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Novalist.Sdk;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Discovers and loads extensions from the extensions folder.
/// </summary>
public sealed class ExtensionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _extensionsDirOverride;
    private readonly string? _bundledDirOverride;
    private readonly bool? _disabledOverride;

    /// <param name="extensionsDir">Extensions directory; defaults to %APPDATA%/Novalist/Extensions. Tests pass a temp dir.</param>
    /// <param name="bundledDir">Where extensions shipped inside the application
    /// live. Null uses NOVALIST_BUNDLED_EXTENSIONS, which the app sets when it
    /// is packaged and leaves unset otherwise.</param>
    /// <param name="disabled">Whether the extension feature is off entirely.
    /// Null uses NOVALIST_EXTENSIONS_DISABLED, which the app sets in the Mac App
    /// Store build and leaves unset everywhere else. Tests pass it directly.</param>
    public ExtensionLoader(string? extensionsDir = null, string? bundledDir = null, bool? disabled = null)
    {
        _extensionsDirOverride = extensionsDir;
        _bundledDirOverride = bundledDir;
        _disabledOverride = disabled;
    }

    /// <summary>
    /// Whether this build has no extension feature at all.
    ///
    /// True in the Mac App Store build. An extension is a .NET assembly that is
    /// downloaded after review and adds views, commands and hooks to the app,
    /// which the App Store does not permit an app to do; the sandbox that build
    /// runs under does not grant the entitlements to load one either. So the
    /// feature is not degraded there, it is absent: nothing is seeded, nothing
    /// is discovered, and nothing can be installed.
    ///
    /// The Developer ID build downloaded directly is unaffected - App Store
    /// rules do not reach it, and its entitlements already allow this.
    /// </summary>
    public bool ExtensionsDisabled => _disabledOverride ?? DisabledByEnvironment;

    /// <summary>The same answer for callers that hold no loader - the store RPC
    /// reaches the gallery without one. See <see cref="ExtensionsDisabled"/>.</summary>
    public static bool DisabledByEnvironment =>
        string.Equals(
            Environment.GetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED"),
            "1",
            StringComparison.Ordinal);

    /// <summary>Where extensions shipped inside the application live, or null
    /// when this build ships none.</summary>
    private string? BundledDirectory =>
        _bundledDirOverride ?? Environment.GetEnvironmentVariable("NOVALIST_BUNDLED_EXTENSIONS");

    /// <summary>The resolved root extensions directory this loader discovers from
    /// and installs into (override in tests, else %APPDATA%/Novalist/Extensions).</summary>
    public string ExtensionsDirectory => _extensionsDirOverride ?? GetExtensionsDirectory();

    /// <summary>
    /// Returns the root extensions directory: &lt;settings-root&gt;/Extensions/.
    /// The settings root honors NOVALIST_SETTINGS_DIR (matching SettingsService)
    /// so an isolated data dir gets an isolated extensions dir; unset, it is the
    /// production %APPDATA%/Novalist/Extensions.
    /// </summary>
    public static string GetExtensionsDirectory()
    {
        var root = Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        return Path.Combine(root, "Extensions");
    }

    /// <summary>
    /// Copies extensions shipped inside the application into the writer's
    /// extensions folder, where everything else already looks for them.
    ///
    /// Seeded rather than discovered in place, because an installed extension is
    /// a folder that gets written to - its settings, its Python environment, its
    /// downloaded models - and the application directory is read-only on macOS
    /// and unwritable for a standard user on Windows. One folder, one set of
    /// rules for updating and removing, and an extension the writer later
    /// updates from the gallery simply wins.
    ///
    /// Only when it is missing or older. Version strings are compared as
    /// versions rather than as text, so 1.10.0 is newer than 1.9.0 - which
    /// string comparison gets backwards, and would have downgraded somebody's
    /// extension on every launch.
    /// </summary>
    private void SeedBundled(string extensionsDir)
    {
        var bundled = BundledDirectory;
        if (string.IsNullOrWhiteSpace(bundled) || !Directory.Exists(bundled))
            return;

        foreach (var source in Directory.GetDirectories(bundled))
        {
            var manifestPath = Path.Combine(source, "extension.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var target = Path.Combine(extensionsDir, Path.GetFileName(source));
                if (Directory.Exists(target) && !IsNewer(manifestPath, Path.Combine(target, "extension.json")))
                    continue;

                CopyOver(source, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or JsonException)
            {
                // A bundled extension that cannot be copied is one the writer
                // does without. Taking the whole application down over it, or
                // over a locked file left by a previous run, would be worse than
                // the missing feature.
            }
        }
    }

    /// <summary>Whether the shipped manifest names a newer version than the
    /// installed one. An unreadable or unparseable version on either side counts
    /// as "not newer", so a strange manifest never overwrites somebody's
    /// working install.</summary>
    private static bool IsNewer(string shippedManifest, string installedManifest)
    {
        var shipped = VersionIn(shippedManifest);
        var installed = VersionIn(installedManifest);
        return shipped != null && installed != null && shipped > installed;
    }

    private static Version? VersionIn(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(
                File.ReadAllText(manifestPath), JsonOptions);
            return Version.TryParse(manifest?.Version, out var version) ? version : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Copies a folder over another, leaving anything already there
    /// that we do not ship - which is where the extension keeps what it has
    /// downloaded.</summary>
    private static void CopyOver(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    /// <summary>
    /// Scans the extensions directory and returns discovered extension info objects.
    /// Does not load assemblies — call <see cref="LoadExtension"/> for that.
    /// </summary>
    public List<ExtensionInfo> DiscoverExtensions()
    {
        var results = new List<ExtensionInfo>();

        // Before the directory is even created: a build with no extension
        // feature should leave no trace of one on disk for somebody to wonder
        // about. See ExtensionsDisabled.
        if (ExtensionsDisabled)
            return results;

        var extensionsDir = _extensionsDirOverride ?? GetExtensionsDirectory();

        // Created before the scan rather than instead of it, so a first run that
        // ships an extension seeds it and finds it in the same pass.
        Directory.CreateDirectory(extensionsDir);
        SeedBundled(extensionsDir);

        foreach (var folder in Directory.GetDirectories(extensionsDir))
        {
            var manifestPath = Path.Combine(folder, "extension.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                    continue;

                results.Add(new ExtensionInfo
                {
                    Manifest = manifest,
                    FolderPath = folder
                });
            }
            catch (Exception ex)
            {
                results.Add(new ExtensionInfo
                {
                    Manifest = new ExtensionManifest
                    {
                        Id = Path.GetFileName(folder),
                        Name = Path.GetFileName(folder)
                    },
                    FolderPath = folder,
                    LoadError = $"Failed to parse extension.json: {ex.Message}"
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Loads an extension assembly and creates the IExtension instance.
    /// Returns true on success; on failure sets <see cref="ExtensionInfo.LoadError"/>.
    /// </summary>
    public bool LoadExtension(ExtensionInfo info)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(info.LoadError))
                return false;

            var manifest = info.Manifest;

            // Check host version compatibility
            if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            {
                if (!Core.VersionInfo.IsCompatibleWith(manifest.MinHostVersion))
                {
                    info.LoadError = $"Requires host version >= {manifest.MinHostVersion} (current: {Core.VersionInfo.Version})";
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
            {
                if (!IsWithinMaxVersion(manifest.MaxHostVersion))
                {
                    info.LoadError = $"Requires host version <= {manifest.MaxHostVersion} (current: {Core.VersionInfo.Version})";
                    return false;
                }
            }

            // Load assembly
            var assemblyPath = Path.Combine(info.FolderPath, manifest.EntryAssembly);
            if (!File.Exists(assemblyPath))
            {
                info.LoadError = $"Entry assembly not found: {manifest.EntryAssembly}";
                return false;
            }

            // Load into a collectible AssemblyLoadContext so we can unload later.
            // Use stream-based loading to avoid holding file locks on the DLLs.
            var loadContext = new ExtensionLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromFileStream(Path.GetFullPath(assemblyPath));

            // Find the IExtension implementation
            var extensionType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (extensionType == null)
            {
                info.LoadError = "No IExtension implementation found in assembly.";
                return false;
            }

            // extensionType is already verified to implement IExtension above, so
            // the cast cannot fail; any construction failure throws and is caught below.
            info.Instance = (IExtension)Activator.CreateInstance(extensionType)!;
            info.LoadContext = loadContext;
            info.IsLoaded = true;
            return true;
        }
        catch (Exception ex)
        {
            info.LoadError = $"Load failed: {ex.Message}";
            return false;
        }
    }

    private static bool IsWithinMaxVersion(string maxVersion)
    {
        var hostVersionStr = Core.VersionInfo.Version;
        // Strip pre-release suffix for comparison
        var dashIndex = hostVersionStr.IndexOf('-');
        if (dashIndex >= 0)
            hostVersionStr = hostVersionStr[..dashIndex];

        var maxDashIndex = maxVersion.IndexOf('-');
        if (maxDashIndex >= 0)
            maxVersion = maxVersion[..maxDashIndex];

        if (Version.TryParse(hostVersionStr, out var hostVer) && Version.TryParse(maxVersion, out var maxVer))
            return hostVer <= maxVer;

        return true; // If we can't parse, allow it
    }
}

/// <summary>
/// Runtime state for a discovered extension.
/// </summary>
public sealed class ExtensionInfo
{
    public required ExtensionManifest Manifest { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public IExtension? Instance { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsLoaded { get; set; }
    public string? LoadError { get; set; }

    /// <summary>
    /// The collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/> that loaded this extension.
    /// Set to null after unloading to allow GC to collect the context and release file locks.
    /// </summary>
    internal ExtensionLoadContext? LoadContext { get; set; }
}
