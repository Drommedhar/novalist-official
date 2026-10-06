using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed partial class ExtensionGalleryService
{
    // ── Update checks ─────────────────────────────────────────────────

    public async Task<List<ExtensionUpdateInfo>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var updates = new List<ExtensionUpdateInfo>();
        var index = await FetchGalleryIndexAsync(ct);
        var extensionsDir = GetExtensionsDirectory();

        if (!Directory.Exists(extensionsDir))
            return updates;

        foreach (var entry in index)
        {
            ct.ThrowIfCancellationRequested();

            var meta = ReadStoreMeta(entry.Id);
            if (meta is null || !meta.InstalledFromGallery)
                continue;

            try
            {
                var latestRelease = await GetLatestCompatibleReleaseAsync(entry, ct);
                if (latestRelease is null)
                    continue;

                // Compare against the version actually on disk (extension.json),
                // not the tag store-meta recorded at install time — a release
                // packaged with a stale manifest version makes those disagree.
                var installedVersion = ReadInstalledManifestVersion(entry.Id) ?? meta.InstalledVersion;
                if (IsNewer(latestRelease.Version, installedVersion))
                {
                    updates.Add(new ExtensionUpdateInfo
                    {
                        ExtensionId = entry.Id,
                        InstalledVersion = installedVersion,
                        AvailableVersion = latestRelease.Version,
                        Release = latestRelease,
                        Entry = entry
                    });
                }
            }
            catch
            {
                // Skip extensions where we can't fetch releases (network error, repo gone, etc.)
            }
        }

        return updates;
    }

    // ── Compatibility ─────────────────────────────────────────────────

    private async Task<bool> IsReleaseCompatibleAsync(GalleryEntry entry, GalleryRelease release, CancellationToken ct)
    {
        // The manifest, to check compatibility and take the icon without
        // downloading the whole ZIP.
        //
        // The release's own copy first, when it published one: a repository with
        // four extensions in it has no manifest at its root, and guessing which
        // project folder to look in would make the gallery responsible for a
        // layout that is none of its business. Falling back to the root keeps
        // every one-extension repository working exactly as it did.
        var tag = release.TagName;
        var url = !string.IsNullOrWhiteSpace(release.ManifestUrl)
            ? release.ManifestUrl
            : $"https://raw.githubusercontent.com/{entry.Repo}/{tag}/extension.json";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return true; // If we can't fetch, assume compatible (will be validated on install)

            var json = await response.Content.ReadAsStringAsync(ct);
            var manifest = JsonSerializer.Deserialize<CompatibilityManifest>(json, JsonOptions);
            if (manifest is null)
                return true;

            // Populate icon from the extension manifest
            release.Icon = manifest.Icon;

            // Check minHostVersion
            if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            {
                if (!VersionInfo.IsCompatibleWith(manifest.MinHostVersion))
                    return false;
            }

            // Check maxHostVersion
            if (!string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
            {
                if (!IsWithinMaxVersion(manifest.MaxHostVersion))
                    return false;
            }

            return true;
        }
        catch
        {
            return true; // Network error — assume compatible, validate on install
        }
    }

    private static bool IsNewer(string remote, string current)
    {
        var remoteParts = ParseVersionParts(remote);
        var currentParts = ParseVersionParts(current);

        for (var i = 0; i < 3; i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }

        return false;
    }

    private static bool IsWithinMaxVersion(string maxVersion)
    {
        var hostVersion = StripPreRelease(VersionInfo.Version);
        var max = StripPreRelease(maxVersion);

        if (Version.TryParse(hostVersion, out var hostVer) && Version.TryParse(max, out var maxVer))
            return hostVer <= maxVer;

        return true;
    }

    /// <summary>
    /// The version a tag names, whatever it prefixes it with.
    ///
    /// A repository holding one extension tags <c>v1.2.0</c>. A repository
    /// holding four has to say which one a tag is for, so it tags
    /// <c>toolkit-v1.2.0</c> - and trimming a leading "v" off that leaves the
    /// whole tag, which parses as version zero. Every such release then compared
    /// as older than what was installed, so the update was never offered and
    /// nothing said why.
    /// </summary>
    internal static string VersionFromTag(string? tag)
    {
        var name = (tag ?? string.Empty).Trim();
        var match = TagVersion().Match(name);
        return match.Success ? match.Groups[1].Value : name;
    }

    // An optional "<slug>-" prefix, an optional "v", then the version itself -
    // keeping any pre-release or build suffix, which the comparison strips.
    [GeneratedRegex(@"^(?:.*-)?[vV]?(\d+(?:\.\d+)*(?:[-+][0-9A-Za-z.\-]+)?)$")]
    private static partial Regex TagVersion();

    private static string StripPreRelease(string version)
    {
        var dash = version.IndexOf('-');
        return dash >= 0 ? version[..dash] : version;
    }

    private static int[] ParseVersionParts(string version)
    {
        version = StripPreRelease(version);
        return version.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();
    }

    private sealed class CompatibilityManifest
    {
        [JsonPropertyName("minHostVersion")] public string? MinHostVersion { get; set; }
        [JsonPropertyName("maxHostVersion")] public string? MaxHostVersion { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
    }
}
