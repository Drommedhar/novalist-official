using System.Text.Json.Serialization;

namespace Novalist.Sdk;

/// <summary>
/// Describes an extension package. Deserialized from <c>extension.json</c> in the extension folder.
/// </summary>
public sealed class ExtensionManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("maxHostVersion")]
    public string MaxHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Optional URL to an icon image (PNG recommended, 128×128 or larger).
    /// A placeholder is shown when not set.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Declarative web contributions (views, etc.) rendered by the host.
    /// </summary>
    [JsonPropertyName("contributes")]
    public WebContributions? Contributes { get; set; }
}

/// <summary>SDK v2 web contribution block of <c>extension.json</c>.</summary>
public sealed class WebContributions
{
    /// <summary>Webview surfaces (main-area views and inspector panels).</summary>
    [JsonPropertyName("views")]
    public List<WebViewContribution> Views { get; set; } = [];

    /// <summary>
    /// Scripts this extension runs inside the interface. Empty for every
    /// extension that does not need to, which should be most of them.
    /// </summary>
    public List<RendererPluginContribution> Renderer { get; set; } = [];
}

/// <summary>
/// A webview surface contributed by an extension: an HTML entry point served
/// from the extension folder, rendered in a sandboxed frame by web hosts.
/// </summary>
/// <summary>
/// JavaScript an extension runs inside the interface itself.
///
/// A webview is sandboxed: it renders in a frame and talks over a message
/// channel, and it cannot touch the editor, the binder or anything else the
/// writer is looking at. A renderer plugin can. That is the point of it and
/// also the cost: an extension running here sees every keystroke in the
/// editor, and one that misbehaves produces bugs indistinguishable from
/// Novalist's own.
///
/// Shipped deliberately, with that stated. Install a renderer plugin the way
/// you would install any program - from somebody you have reason to trust.
/// </summary>
public sealed class RendererPluginContribution
{
    /// <summary>
    /// The script, relative to the extension folder. One file: it may import
    /// nothing, because there is no module resolver behind it.
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// Which version of the plugin API the script was written against. The
    /// host refuses a version it does not implement rather than loading the
    /// script and letting it fail somewhere less obvious.
    /// </summary>
    public int ApiVersion { get; set; } = 1;
}

public sealed class WebViewContribution
{
    /// <summary>Unique view key, e.g. <c>com.example.chat</c>.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Display title; may be a localization key resolved by the extension's locales.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Lucide-style SVG path data for the view's icon.</summary>
    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = string.Empty;

    /// <summary>Placement: <c>"main"</c> (content view) or <c>"inspector"</c> (right panel).</summary>
    [JsonPropertyName("placement")]
    public string Placement { get; set; } = "main";

    /// <summary>Entry HTML file, relative to the extension folder (e.g. <c>web/chat.html</c>).</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = string.Empty;
}
