using Novalist.Core.Models;

namespace Novalist.Core.Services;

public enum ExportFormat
{
    Epub,
    Docx,
    Pdf,
    Markdown,
    FinalDraft,
    LaTeX,
    Codex,
    CodexPdf,

    /// <summary>
    /// Scene metadata as a spreadsheet sheet. Everything Novalist writes is
    /// prose or a document; nothing machine-readable left the project, so an
    /// outline could not be pulled into a spreadsheet or another tool.
    /// </summary>
    Csv,

    /// <summary>Scene metadata and the Codex together, for another tool to read.</summary>
    Json,

    /// <summary>
    /// The Codex as a sheet, one row per field rather than per entry. Entries
    /// do not share a shape, so a column per field would be mostly empty and
    /// would change width with the project.
    /// </summary>
    CodexCsv,

    /// <summary>
    /// The outline as OPML, which is what every outliner reads. A sheet can be
    /// pivoted but it cannot carry a shape, and the shape is the point of
    /// handing an outline to an outliner.
    /// </summary>
    Opml,

    /// <summary>
    /// Everything the project holds in one JSON document - plot threads,
    /// research, saved lists and collections as well as the scenes and the
    /// Codex, none of which had an export path of their own.
    /// </summary>
    WorldJson,

    /// <summary>The same, as one page a person can read and browse.</summary>
    WorldHtml,

    /// <summary>
    /// Every scene's synopsis in reading order. The synopsis of a book existed
    /// only as forty separate boxes nobody could put side by side.
    /// </summary>
    SynopsisReport,

    /// <summary>
    /// How the book divides between points of view. "How much of this is in
    /// Mira's head" could not be asked at all.
    /// </summary>
    PovReport
}

/// <summary>What an export would contain, reported before it is written.</summary>
public sealed class ExportPreview
{
    public int Chapters { get; init; }
    public int Scenes { get; init; }
    public int Words { get; init; }
    public int Characters { get; init; }
    public int Pages { get; init; }

    /// <summary>
    /// Pictures in the prose with nothing written about what they show. A
    /// reader who cannot see them gets nothing at all, and an EPUB that
    /// carries one cannot honestly claim to be accessible - so the count is
    /// reported before the export runs rather than discovered afterwards.
    /// </summary>
    public int UndescribedImages { get; init; }

    /// <summary>
    /// True only on the Normseite grid, where the layout fixes the columns and
    /// the lines so the count is arithmetic rather than a guess. Everywhere
    /// else the number is an estimate and has to be shown as one.
    /// </summary>
    public bool PagesAreExact { get; init; }
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Epub;
    public bool IncludeTitlePage { get; set; } = true;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    /// <summary>Optional preset id from <see cref="ExportPresets.All"/>.</summary>
    public string? PresetId { get; set; }
    public List<string> SelectedChapterGuids { get; set; } = [];

    /// <summary>
    /// Scene stages this export includes, by key. Null or empty means every
    /// stage - the common case, and the one an export that names no filter
    /// has to keep doing.
    /// </summary>
    public List<string>? IncludedStages { get; set; }

    /// <summary>
    /// Further books of the project to append after the open one, by id.
    ///
    /// Null or empty is one book, which is what every export did before box
    /// sets existed. The open book is always first and never listed here.
    /// </summary>
    public List<string>? IncludedBookIds { get; set; }

    /// <summary>ISBN, publisher, series and the rest. Never null; an empty one
    /// simply writes nothing extra.</summary>
    public Models.PublishingMetadata Publishing { get; set; } = new();

    /// <summary>
    /// Absolute path of the book's cover image. When set and readable, EPUB gets
    /// a real cover (manifest item with <c>properties="cover-image"</c>, the
    /// EPUB 2 <c>meta name="cover"</c> retailers still read, and a cover page
    /// first in the spine) and PDF gets a full-bleed cover page. Empty or
    /// missing means no cover, which is what every export did before.
    /// </summary>
    public string CoverImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Leave out everything marked as not for readers.
    ///
    /// A world page that lists the villain's real name beside everything else
    /// is worse than no world page at all, so this is what turns the same
    /// export into something that can be handed to somebody reading the book.
    /// </summary>
    public bool ForReaders { get; set; }

    /// <summary>
    /// BCP-47 language tag written to EPUB's <c>dc:language</c>. Defaults to
    /// English only when nothing is supplied; a German or Chinese book that
    /// ships as <c>en</c> is mis-shelved at retailer ingestion.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Codex export filter: qualified entity keys of the form <c>type:id</c>
    /// (<c>character:</c>, <c>location:</c>, <c>item:</c>, <c>lore:</c>).
    /// <c>null</c> exports every entity; an empty list exports none.
    /// </summary>
    public List<string>? SelectedEntityKeys { get; set; }

    /// <summary>
    /// Translations for the codex export's fixed labels, keyed by
    /// <see cref="ExportService"/>'s label keys ("role", "characters", …).
    /// Supplied by the UI in the user's language; missing keys fall back to
    /// English.
    /// </summary>
    public Dictionary<string, string>? Labels { get; set; }

    /// <summary>
    /// Front- and back-matter pages to write around the story. Compiled from the
    /// book so the exporter does not need the project service.
    /// </summary>
    public List<MatterExportContent> Matter { get; set; } = [];

    /// <summary>
    /// The scene break this export prints, with its placeholders resolved.
    /// Set by the compile; the layout's own separator until then.
    /// </summary>
    public string ResolvedSeparator { get; set; } = string.Empty;

    /// <summary>
    /// The running head this export prints, with its placeholders resolved and
    /// the page number appended by whichever writer draws it.
    ///
    /// Empty means the submission default - surname and short title - which is
    /// what every Shunn export printed before a layout could say otherwise.
    /// </summary>
    public string ResolvedRunningHead { get; set; } = string.Empty;

    /// <summary>
    /// Layouts the writer authored, so a custom preset id resolves to theirs
    /// rather than silently falling back to the default.
    /// </summary>
    public List<ExportPreset> CustomPresets { get; set; } = [];

    /// <summary>
    /// Substitutions applied to the compiled output only. Replace All writes to
    /// the source scenes; these never do, so a rule can be turned off without
    /// anything to undo.
    /// </summary>
    public List<Models.ExportReplacement> Replacements { get; set; } = [];

    /// <summary>
    /// How deep the table of contents goes. 1 lists the chapters, which is what
    /// every export did before; 2 also lists the scenes inside them. Values
    /// outside that range are clamped rather than rejected, because a contents
    /// list is not worth failing an export over.
    /// </summary>
    public int TocDepth { get; set; } = 1;

    /// <summary>
    /// Heading printed above the contents. Empty means "Table of Contents" -
    /// which is wrong in every language but English, and wrong in English for
    /// anyone who wanted "Contents".
    /// </summary>
    public string TocTitle { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path of a DOCX whose styles this export should adopt. When set
    /// and readable, its <c>word/styles.xml</c> replaces the one Novalist
    /// generates, so an agent's or publisher's house style survives an export
    /// instead of being reapplied by hand afterwards. A path that is missing,
    /// locked, or not a DOCX falls back to Novalist's own styles: a bad
    /// reference document is a reason to ignore it, never to fail the export.
    /// </summary>
    public string ReferenceDocPath { get; set; } = string.Empty;

    /// <summary>The contents depth, clamped to what the writers can render.</summary>
    public int EffectiveTocDepth => Math.Clamp(TocDepth, 1, 2);

    /// <summary>
    /// The store this build is for, by <see cref="Models.RetailerLink.Key"/>, or
    /// empty for a neutral build that names no shop.
    ///
    /// One format, one path, one file was the whole model, so every copy of a
    /// book sold in five shops carried the same back-matter link - and Amazon
    /// refuses a book whose back matter links to a rival store.
    /// </summary>
    public string RetailerKey { get; set; } = string.Empty;

    /// <summary>The store this build is for, or null when it names none.</summary>
    public Models.RetailerLink? ResolveRetailer()
        => string.IsNullOrWhiteSpace(RetailerKey)
            ? null
            : Publishing.Retailers.FirstOrDefault(
                r => string.Equals(r.Key, RetailerKey.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which parts of a Codex entry the export carries:
    /// <c>images</c>, <c>fields</c>, <c>relationships</c>, <c>sections</c>.
    ///
    /// Null means all of them - what every codex export did before. A series
    /// bible that has to leave the portraits out, or a submission packet that
    /// wants the names and nothing else, was an all-or-nothing choice per entry
    /// until this existed.
    /// </summary>
    public List<string>? CodexParts { get; set; }

    /// <summary>
    /// Section titles to carry, when sections are included at all. Null means
    /// every section; naming some is how "Appearance but not Secrets" is said.
    /// </summary>
    public List<string>? SelectedSectionTitles { get; set; }

    /// <summary>True when this export carries the named part of an entry.</summary>
    public bool IncludesPart(string part)
        => CodexParts == null || CodexParts.Contains(part, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when a section with this title belongs in the export. False for
    /// every section when sections are off, whatever the title list says.
    /// </summary>
    public bool IncludesSection(string? title)
        => IncludesPart("sections")
           && (SelectedSectionTitles == null
               || SelectedSectionTitles.Contains(title ?? string.Empty, StringComparer.OrdinalIgnoreCase));

    /// <summary>Resolves to the configured preset (or default).</summary>
    public ExportPreset ResolvePreset()
    {
        if (!string.IsNullOrWhiteSpace(PresetId))
        {
            var custom = CustomPresets.FirstOrDefault(p => p.Id == PresetId);
            if (custom != null) return custom;
            return ExportPresets.GetById(PresetId);
        }
        return ExportPresets.GetById(ExportPresets.DefaultId);
    }
}

/// <summary>One front- or back-matter page on its way into an export.</summary>
public class MatterExportContent
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The <see cref="Models.BookMatterKind"/> name, so writers can key
    /// per-kind layout off it without referencing the enum.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>"Front" or "Back".</summary>
    public string Placement { get; set; } = string.Empty;

    /// <summary>Heading to print. Empty means print no heading.</summary>
    public string Title { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool InTableOfContents { get; set; }
}

public class ChapterExportContent
{
    /// <summary>
    /// True for the divider that announces a volume of a box set rather than a
    /// chapter of one. It carries no scenes, is never numbered, sits a level
    /// above a chapter in the contents, and the chapters after it belong to it.
    /// </summary>
    public bool IsVolume { get; set; }

    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? Subtitle { get; set; }

    /// <summary>
    /// Which chapter of the project this is.
    ///
    /// Carried through the compile so a writer that has to go back to the
    /// project - the audiobook render, which needs each scene's cast and
    /// directions - can, rather than matching on a title two chapters might
    /// share. Empty on the volume dividers of a box set, which are not chapters.
    /// </summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>
    /// The heading this chapter prints, with the layout's format applied and
    /// its placeholders resolved.
    ///
    /// Built once in the compile, the same way suggested edits are, so six
    /// writers cannot disagree about what a chapter is called - and so a
    /// placeholder in a heading format resolves in every format rather than in
    /// whichever one the resolver happened to be wired into.
    /// </summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>True when this chapter opens straight into its prose.</summary>
    public bool HideHeading { get; set; }

    public List<SceneExportContent> Scenes { get; set; } = [];
}

public class SceneExportContent
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>Which scene of the project this is, for the same reason
    /// <see cref="ChapterExportContent.Guid"/> is carried. Empty on matter
    /// pages, which are not scenes.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Unresolved inline comments on this scene, carried through so DOCX can
    /// emit them as real Word comments an editor can reply to. Resolved ones are
    /// left behind: they are a record of a finished conversation, not a note the
    /// editor should see.
    /// </summary>
    public List<SceneExportComment> Comments { get; set; } = [];

    /// <summary>
    /// The scene's footnotes, keyed by the id its <c>&lt;sup class="nv-fn"&gt;</c>
    /// anchor carries. Kept beside the prose rather than appended to it, so each
    /// format can render a real note where the anchor sits.
    /// </summary>
    public Dictionary<string, string> Footnotes { get; set; } = [];
}

/// <summary>One comment travelling with a scene into an export.</summary>
public class SceneExportComment
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The prose the comment was attached to, used to place the Word
    /// comment's range.</summary>
    public string AnchorText { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Internal representation of a text segment with formatting metadata.
/// </summary>
internal sealed class InlineSegment
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>
    /// Struck-through prose. Kept because a writer who struck a line meant the
    /// reader to see it struck; a highlight, by contrast, is a working mark and
    /// is dropped on the way out.
    /// </summary>
    public bool Strike { get; set; }

    /// <summary>
    /// When set, this segment is a footnote anchor rather than prose: the text
    /// of the note, which each format renders in its own way. The prose it was
    /// anchored to is the segment before it.
    /// </summary>
    public string? FootnoteText { get; set; }
}
