using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A single entry from the gallery index (gallery.json hosted in the gallery repository).
/// </summary>
public sealed class GalleryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// GitHub repository in "owner/repo" format.
    /// </summary>
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Represents a fetched release from an extension's GitHub repository.
/// </summary>
public sealed class GalleryRelease
{
    /// <summary>
    /// The Git tag name, e.g. "v1.2.0".
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// Parsed version without the "v" prefix, e.g. "1.2.0".
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Release notes body (Markdown).
    /// </summary>
    public string Body { get; set; } = string.Empty;

    public bool IsPrerelease { get; set; }

    /// <summary>
    /// browser_download_url of the ZIP asset for this extension.
    /// </summary>
    public string ZipDownloadUrl { get; set; } = string.Empty;

    public long ZipSize { get; set; }

    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// Optional icon URL from the extension's manifest. Placeholder used when null.
    /// </summary>
    public string? Icon { get; set; }
}

/// <summary>
/// Full details for displaying an extension in the store detail view.
/// </summary>
public sealed class GalleryExtensionDetail
{
    public required GalleryEntry Entry { get; set; }
    public GalleryRelease? LatestRelease { get; set; }
    public string ReadmeMarkdown { get; set; } = string.Empty;
    public bool IsCompatible { get; set; }
    public bool IsInstalled { get; set; }
    public bool HasUpdate { get; set; }
    public string? InstalledVersion { get; set; }
}

/// <summary>
/// Describes an available update for an installed extension.
/// </summary>
public sealed class ExtensionUpdateInfo
{
    public required string ExtensionId { get; set; }
    public required string InstalledVersion { get; set; }
    public required string AvailableVersion { get; set; }
    public required GalleryRelease Release { get; set; }
    public required GalleryEntry Entry { get; set; }
}

/// <summary>
/// Metadata stored alongside gallery-installed extensions to track origin and version.
/// Serialized as store-meta.json in the extension folder.
/// </summary>
public sealed class ExtensionStoreMeta
{
    [JsonPropertyName("installedFromGallery")]
    public bool InstalledFromGallery { get; set; }

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("installedVersion")]
    public string InstalledVersion { get; set; } = string.Empty;

    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; }
}
