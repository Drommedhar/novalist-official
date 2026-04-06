using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Novalist.Sdk;

namespace Novalist.Desktop.Services;

/// <summary>
/// Discovers and loads extensions from the extensions folder.
/// </summary>
public sealed class ExtensionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the root extensions directory: %APPDATA%/Novalist/Extensions/
    /// </summary>
    public static string GetExtensionsDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Novalist", "Extensions");
    }

    /// <summary>
    /// Scans the extensions directory and returns discovered extension info objects.
    /// Does not load assemblies — call <see cref="LoadExtension"/> for that.
    /// </summary>
    public List<ExtensionInfo> DiscoverExtensions()
    {
        var results = new List<ExtensionInfo>();
        var extensionsDir = GetExtensionsDirectory();

        if (!Directory.Exists(extensionsDir))
        {
            Directory.CreateDirectory(extensionsDir);
            return results;
        }

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

            var assembly = Assembly.LoadFrom(assemblyPath);

            // Find the IExtension implementation
            var extensionType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (extensionType == null)
            {
                info.LoadError = "No IExtension implementation found in assembly.";
                return false;
            }

            var instance = Activator.CreateInstance(extensionType) as IExtension;
            if (instance == null)
            {
                info.LoadError = "Failed to instantiate IExtension implementation.";
                return false;
            }

            info.Instance = instance;
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
}
