namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a custom export format contributed by an extension.
/// </summary>
public sealed class ExportFormatDescriptor
{
    /// <summary>Format key (e.g. "odt", "fountain").</summary>
    public string FormatKey { get; init; } = string.Empty;

    /// <summary>Display name (e.g. "OpenDocument Text").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>File extension (e.g. ".odt").</summary>
    public string FileExtension { get; init; } = string.Empty;

    /// <summary>Icon emoji.</summary>
    public string Icon { get; init; } = "📄";

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Export handler. Receives the export context.</summary>
    public Func<ExportContext, Task>? Export { get; init; }
}

/// <summary>
/// Context information passed to an export handler.
/// </summary>
public sealed class ExportContext
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string BookName { get; init; } = string.Empty;
}
