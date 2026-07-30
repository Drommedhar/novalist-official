using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novalist.Core.Services;

/// <summary>
/// Comma-separated values, written the way a spreadsheet expects to read them.
///
/// RFC 4180 quoting, and the reason it is a class of its own: a synopsis with a
/// comma in it, a scene called "Then, later" and a note spanning two lines are
/// all ordinary content here, and every one of them silently corrupts a naive
/// join into columns that no longer line up.
/// </summary>
public static class Csv
{
    /// <summary>One cell, quoted only when it has to be.</summary>
    public static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        // Excel and LibreOffice both split on the separator inside an unquoted
        // cell, so a comma, a quote or a line break forces the quoted form.
        var needsQuotes = text.Contains(',') || text.Contains('"')
            || text.Contains('\n') || text.Contains('\r');
        if (!needsQuotes) return text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>One row, terminated the way every reader accepts.</summary>
    public static string Row(IEnumerable<string?> cells)
        // CRLF rather than LF: the spreadsheets this is written for are the
        // strictest readers of the format, and both accept CRLF everywhere.
        => string.Join(",", cells.Select(Cell)) + "\r\n";

    /// <summary>A whole sheet: a header row and the rows under it.</summary>
    public static string Sheet(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(Row(header));
        foreach (var row in rows) sb.Append(Row(row));
        return sb.ToString();
    }
}

/// <summary>
/// One scene as a spreadsheet sees it: what it is called, where it sits, and
/// every planning field the writer filled in.
/// </summary>
public sealed class SceneMetadataRow
{
    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    [JsonPropertyName("chapterOrder")]
    public int ChapterOrder { get; set; }

    [JsonPropertyName("scene")]
    public string Scene { get; set; } = string.Empty;

    [JsonPropertyName("sceneOrder")]
    public int SceneOrder { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("pov")]
    public string Pov { get; set; } = string.Empty;

    [JsonPropertyName("words")]
    public int Words { get; set; }

    [JsonPropertyName("wordTarget")]
    public int WordTarget { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("synopsis")]
    public string Synopsis { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("conflict")]
    public string Conflict { get; set; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("plotlines")]
    public string Plotlines { get; set; } = string.Empty;

    [JsonPropertyName("cast")]
    public string Cast { get; set; } = string.Empty;

    /// <summary>
    /// True for a scene the writer parked. It is a column rather than a reason
    /// to drop the row: an outline that hides the scenes somebody set aside is
    /// exactly the outline that cannot answer why the act is short.
    /// </summary>
    [JsonPropertyName("inactive")]
    public bool Inactive { get; set; }

    /// <summary>True for a scene held back from the book but still planned.</summary>
    [JsonPropertyName("excludedFromExport")]
    public bool ExcludedFromExport { get; set; }
}

/// <summary>One Codex entry, flattened to what another tool can read.</summary>
public sealed class EntityMetadataRow
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Every filled-in field, labelled the way the document exports label them.
    ///
    /// Type and description are in here rather than beside it: they are fields
    /// like any other, they are named differently on different entity kinds -
    /// a character has a role where lore has a category - and lifting two of
    /// them out would have meant reading the same value in two places.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = [];

    [JsonPropertyName("sections")]
    public Dictionary<string, string> Sections { get; set; } = [];

    [JsonPropertyName("relationships")]
    public List<string> Relationships { get; set; } = [];
}

/// <summary>Everything a machine-readable export carries about a book.</summary>
public sealed class MetadataExport
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("scenes")]
    public List<SceneMetadataRow> Scenes { get; set; } = [];

    [JsonPropertyName("codex")]
    public List<EntityMetadataRow> Codex { get; set; } = [];
}

/// <summary>Turns the metadata model into the bytes that leave the project.</summary>
public static class MetadataWriter
{
    /// <summary>Column headings, in the order the sheet writes them.</summary>
    public static readonly string[] SceneColumns =
    [
        "Chapter", "Chapter order", "Scene", "Scene order", "Stage", "POV",
        "Words", "Word target", "Date", "Synopsis", "Goal", "Conflict",
        "Outcome", "Tags", "Plotlines", "Cast", "Inactive", "Excluded from export"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // A file somebody will open in an editor: escaping every accented
        // letter to ä would make a German outline unreadable by hand.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The scene sheet, header row included.</summary>
    public static string SceneCsv(IEnumerable<SceneMetadataRow> scenes)
        => Csv.Sheet(SceneColumns, scenes.Select(Cells));

    private static IEnumerable<string?> Cells(SceneMetadataRow s)
    {
        yield return s.Chapter;
        yield return s.ChapterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return s.Scene;
        yield return s.SceneOrder.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return s.Stage;
        yield return s.Pov;
        yield return s.Words.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return s.WordTarget.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return s.Date;
        yield return s.Synopsis;
        yield return s.Goal;
        yield return s.Conflict;
        yield return s.Outcome;
        yield return s.Tags;
        yield return s.Plotlines;
        yield return s.Cast;
        // Spelled out rather than TRUE/FALSE: a sheet a person reads should
        // say what the column means without a legend.
        yield return s.Inactive ? "yes" : "no";
        yield return s.ExcludedFromExport ? "yes" : "no";
    }

    /// <summary>Column headings for the Codex sheet.</summary>
    public static readonly string[] CodexColumns = ["Kind", "Name", "Field", "Value"];

    /// <summary>
    /// The Codex sheet: one row per field rather than one row per entry.
    ///
    /// Entries do not share a shape - a character has eyes and a build, a piece
    /// of lore has a category, and the writer's own types have whatever they
    /// asked for - so a column per field would be mostly empty and would change
    /// width with the project. Kind, name, field, value stays the same shape
    /// whatever is in the book, and pivots back into a wide sheet in one step
    /// for anybody who wants one.
    /// </summary>
    public static string CodexCsv(IEnumerable<EntityMetadataRow> codex)
        => Csv.Sheet(CodexColumns, codex.SelectMany(CodexCells));

    private static IEnumerable<IEnumerable<string?>> CodexCells(EntityMetadataRow entry)
    {
        foreach (var property in entry.Properties)
            yield return [entry.Kind, entry.Name, property.Key, property.Value];
        foreach (var section in entry.Sections)
            yield return [entry.Kind, entry.Name, section.Key, section.Value];
        foreach (var relationship in entry.Relationships)
            yield return [entry.Kind, entry.Name, "Relationship", relationship];
    }

    /// <summary>The whole export, indented so a person can read it too.</summary>
    public static string Json(MetadataExport export)
        => JsonSerializer.Serialize(export, JsonOptions);

    /// <summary>
    /// The outline as OPML: chapters as branches, scenes as leaves, each scene's
    /// synopsis as its note.
    ///
    /// This is what every outliner reads - Scrivener, OmniOutliner, Scapple,
    /// most mind-mappers. A CSV can be pivoted but it cannot carry a shape, and
    /// the shape is the whole point of handing an outline to an outliner.
    ///
    /// Scenes are grouped by the chapter name in the order they arrive, which
    /// is reading order. Two chapters with the same title stay separate: they
    /// are separate chapters, and merging them would silently reorder the book.
    /// </summary>
    public static string Opml(MetadataExport export)
    {
        var text = new System.Text.StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        text.Append("<opml version=\"2.0\">\n");
        text.Append("  <head><title>").Append(Xml(export.Title)).Append("</title></head>\n");
        text.Append("  <body>\n");

        string? openChapter = null;
        var chapterKey = -1;
        foreach (var scene in export.Scenes)
        {
            if (openChapter == null || scene.ChapterOrder != chapterKey)
            {
                if (openChapter != null) text.Append("    </outline>\n");
                text.Append("    <outline text=\"").Append(Xml(scene.Chapter)).Append("\">\n");
                openChapter = scene.Chapter;
                chapterKey = scene.ChapterOrder;
            }

            text.Append("      <outline text=\"").Append(Xml(scene.Scene)).Append('"');
            if (!string.IsNullOrWhiteSpace(scene.Synopsis))
                text.Append(" _note=\"").Append(Xml(scene.Synopsis)).Append('"');
            text.Append(" />\n");
        }
        if (openChapter != null) text.Append("    </outline>\n");

        text.Append("  </body>\n</opml>\n");
        return text.ToString();
    }

    /// <summary>
    /// Escapes a value for an XML attribute.
    ///
    /// A synopsis is prose the writer typed: an ampersand, a quotation mark or
    /// an angle bracket in it would otherwise produce a file no outliner can
    /// open. Newlines become spaces, because an attribute cannot hold one and a
    /// raw newline is what makes the file invalid rather than merely ugly.
    /// </summary>
    private static string Xml(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("\r", " ")
            .Replace("\n", " ");
}
