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

    /// <summary>Short text marker, when there is no vector icon.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// Whether this format can put the book's cover in the file.
    ///
    /// The Export view shows its "Include the book cover" toggle only for formats
    /// that say yes, because a control that changes nothing is worse than no
    /// control. Formats that say no are always handed an empty
    /// <see cref="ExportContext.CoverImagePath"/>.
    /// </summary>
    public bool SupportsCover { get; init; }

    /// <summary>Export handler. Receives the export context.</summary>
    public Func<ExportContext, Task>? Export { get; init; }
}

/// <summary>
/// What an export handler is told about the export it is running.
///
/// A contributed format was given a path, a project root and a title, which is
/// not enough to produce a file that matches what the writer asked for: it could
/// not know their language, so every file came out marked English, and it could
/// not know about the cover they had chosen, so no contributed format ever
/// included one. Everything the built-in formats resolve is passed on here.
/// </summary>
public sealed class ExportContext
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>The title the writer entered, or the book's name.</summary>
    public string BookName { get; init; } = string.Empty;

    /// <summary>The author as entered in the Export view. Empty when not given.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// The book's writing language as a BCP-47 tag ("de", "en-GB"). Mark it on
    /// the file: a German novel served as English is read wrong by screen
    /// readers, hyphenated wrong, and spell-checked against the wrong
    /// dictionary.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Absolute path of the cover image, or empty when the book has none or the
    /// writer turned it off. Formats that cannot carry an image ignore it.
    /// </summary>
    public string CoverImagePath { get; init; } = string.Empty;

    /// <summary>Whether the writer asked for a title page.</summary>
    public bool IncludeTitlePage { get; init; } = true;

    /// <summary>
    /// Guids of the chapters the writer chose, in book order. Empty means every
    /// chapter, which is what an export that names no selection has always done.
    ///
    /// A contributed format used to be given no selection at all, so the Export
    /// view hid the chapter list for it and every run produced the whole book -
    /// there was no way to send somebody the first three chapters in any format
    /// but a built-in one.
    /// </summary>
    public IReadOnlyList<string> SelectedChapterGuids { get; init; } = [];
}
