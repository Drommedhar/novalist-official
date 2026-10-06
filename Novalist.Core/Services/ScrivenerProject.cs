using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>
/// One document of a Scrivener draft, flattened into the part, chapter and
/// scene Novalist will create for it.
///
/// <c>PartKey</c> and <c>ChapterKey</c> are the binder identity of the folders
/// this document sat in, and are what the import groups on. The titles beside
/// them are for display only: Scrivener's own novel template names every part
/// "Part" and every chapter "Chapter", so grouping by title collapsed a
/// four-chapter book into one chapter with four scenes in it.
/// </summary>
public sealed record ScrivenerScene(
    string PartKey,
    string PartTitle,
    string ChapterKey,
    string ChapterTitle,
    string Title,
    string Text,
    string Html,
    string Synopsis,
    string Notes,
    string Label,
    string Status,
    bool IncludeInCompile,
    IReadOnlyDictionary<string, string> CustomFields,
    ScrivenerTargetKind TargetKind = ScrivenerTargetKind.Manuscript,
    string TargetKey = "",
    string TargetTitle = "");

/// <summary>Where a document's chapter is bound: the book being imported into,
/// a draft of it, or a book of its own.</summary>
public enum ScrivenerTargetKind
{
    /// <summary>The active book's current draft - what an import has always done.</summary>
    Manuscript,

    /// <summary>A new draft of the active book, named after its binder folder.</summary>
    Draft,

    /// <summary>A new book in the project, named after its binder folder.</summary>
    Book
}

/// <summary>
/// Where one binder folder's contents are sent.
///
/// Scrivener only marks the draft, the trash and its template sheets; everything
/// else is the writer's own arrangement, and no set of rules reads that reliably.
/// A binder whose draft folder is empty because the next draft has not been
/// started, with nine finished ones filed under a folder called "Old", is a
/// perfectly ordinary way to work and imported as nine folders of research.
/// So the rules produce a starting point and the writer corrects it.
/// </summary>
public enum ScrivenerDestination
{
    /// <summary>Chapters and scenes of the book being imported into.</summary>
    Manuscript,

    /// <summary>A draft of that book, named after the folder.</summary>
    Draft,

    /// <summary>A new book, named after the folder.</summary>
    Book,

    /// <summary>Codex entries, as characters.</summary>
    Characters,

    /// <summary>Codex entries, as places.</summary>
    Places,

    /// <summary>Research items, with the folder title as a tag.</summary>
    Research,

    /// <summary>Left in Scrivener, and named as such before the import runs.</summary>
    Skip
}

/// <summary>
/// One row of the binder the writer can redirect, with where the rules would
/// send it.
///
/// Only the top level and the level below it are offered. That is enough to
/// separate nine drafts filed inside one folder, and stopping there keeps the
/// part / chapter / scene shape below a draft the binder's own business rather
/// than a wall of dropdowns.
/// </summary>
public sealed record ScrivenerBinderRow(
    string Key,
    string Title,
    /// <summary>0 for a top-level binder entry, 1 for a direct child of one.</summary>
    int Depth,
    ScrivenerDestination Destination,
    /// <summary>Documents anywhere beneath this row - what it is worth.</summary>
    int Documents,
    bool HasChildren);

/// <summary>
/// One field the writer added to every document in Scrivener 3, which is the
/// nearest thing Scrivener has to a custom entity: a named, optionally
/// constrained value carried by each document rather than a free-form note.
/// </summary>
public sealed record ScrivenerCustomField(string Id, string Title, IReadOnlyList<string> Options);

/// <summary>What kind of Codex entry a Scrivener sketch becomes.</summary>
public enum ScrivenerEntityKind
{
    Character,
    Location
}

/// <summary>A character or setting sketch, bound for the Codex.</summary>
public sealed record ScrivenerEntity(
    ScrivenerEntityKind Kind,
    string Name,
    string Text,
    string Notes,
    string MarkdownText,
    string MarkdownNotes);

/// <summary>What kind of research item a binder document becomes.</summary>
public enum ScrivenerResearchKind
{
    Note,
    Pdf,
    Image,
    File
}

/// <summary>
/// A document outside the draft: a research note, an imported PDF, a picture,
/// a piece of front matter. <see cref="SourcePath"/> is absolute and empty for
/// a note, whose prose is in <see cref="Text"/>.
/// </summary>
public sealed record ScrivenerResearch(
    string Title,
    ScrivenerResearchKind Kind,
    string Text,
    string MarkdownText,
    string SourcePath,
    string FolderTag);

/// <summary>What a Scrivener project turned into, plus what was left behind.</summary>
public sealed class ScrivenerProject
{
    public IReadOnlyList<ScrivenerScene> Scenes { get; init; } = [];

    /// <summary>Character and setting sketches, bound for the Codex.</summary>
    public IReadOnlyList<ScrivenerEntity> Entities { get; init; } = [];

    /// <summary>Everything outside the draft that carried content.</summary>
    public IReadOnlyList<ScrivenerResearch> Research { get; init; } = [];

    /// <summary>The project's own custom metadata fields, in binder order.</summary>
    public IReadOnlyList<ScrivenerCustomField> CustomFields { get; init; } = [];

    /// <summary>"2" or "3" - which Scrivener laid the project out. Empty when
    /// the folder could not be read at all.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// What the import will not bring across, in the writer's terms.
    ///
    /// Scrivener holds a great deal Novalist has no equivalent for, and a silent
    /// import that drops half a project is worse than one that says so before it
    /// starts.
    /// </summary>
    public IReadOnlyList<string> Losses { get; init; } = [];

    public bool IsEmpty => Scenes.Count == 0 && Entities.Count == 0 && Research.Count == 0;
}

/// <summary>
/// A content-safe explanation of why a Scrivener project could not be read.
/// Stage and reason are parser-owned constants. <see cref="Detail"/> is a
/// parser-owned explanation rather than the raw exception message; XML
/// failures also carry their parser-reported line and position.
/// </summary>
public sealed record ScrivenerReadDiagnostic(
    string Stage,
    string Reason,
    string ExceptionType = "",
    string Detail = "",
    int LineNumber = 0,
    int LinePosition = 0,
    int ErrorCode = 0);
