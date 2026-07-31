using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One plot thread, as an archive records it.</summary>
public sealed record ArchivedPlotline(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("importance")] string Importance,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("steps")] IReadOnlyList<string> Steps,
    [property: JsonPropertyName("unresolved")] int Unresolved);

/// <summary>One research note.</summary>
public sealed record ArchivedResearch(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("content")] string Content);

/// <summary>One saved list, with the rules that define it.</summary>
public sealed record ArchivedSmartList(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("match")] string Match,
    [property: JsonPropertyName("rules")] IReadOnlyList<string> Rules);

/// <summary>One hand-curated set of scenes.</summary>
public sealed record ArchivedCollection(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scenes")] int Scenes);

/// <summary>
/// Everything a project holds, in one document.
///
/// The metadata export already carried scenes and the Codex. Plotlines,
/// research, saved lists, collections and maps had no export path at all, so a
/// project could be read out in pieces and never as a whole.
/// </summary>
public sealed class WorldArchiveDocument
{
    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    [JsonPropertyName("book")]
    public string Book { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("scenes")]
    public List<SceneMetadataRow> Scenes { get; set; } = [];

    [JsonPropertyName("codex")]
    public List<EntityMetadataRow> Codex { get; set; } = [];

    [JsonPropertyName("plotlines")]
    public List<ArchivedPlotline> Plotlines { get; set; } = [];

    [JsonPropertyName("research")]
    public List<ArchivedResearch> Research { get; set; } = [];

    [JsonPropertyName("smartLists")]
    public List<ArchivedSmartList> SmartLists { get; set; } = [];

    [JsonPropertyName("collections")]
    public List<ArchivedCollection> Collections { get; set; } = [];

    /// <summary>Map names. The maps themselves are images and layer trees, and
    /// belong in the project folder rather than inside a document.</summary>
    [JsonPropertyName("maps")]
    public List<string> Maps { get; set; } = [];
}

/// <summary>
/// The whole project as one document, and as something a person can read.
///
/// The on-disk format has always been open, but reading a project out meant
/// knowing where every piece lived. World Anvil exports a world as JSON plus
/// browsable HTML; Kanka and LegendKeeper ship portability archives. Novalist
/// had a Markdown timeline outline and nothing else - no plotlines, no
/// research, no saved lists, no collections.
/// </summary>
public static class WorldArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // A file somebody opens in an editor: escaping every accented letter
        // would make a German world unreadable by hand.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Gathers the parts a metadata export does not reach. Scenes and the Codex
    /// are passed in, because compiling those already exists and doing it twice
    /// is how two exports come to disagree.
    /// </summary>
    public static WorldArchiveDocument Build(
        MetadataExport metadata, ProjectMetadata? project, BookData? book)
    {
        var archive = new WorldArchiveDocument
        {
            Project = project?.Name ?? string.Empty,
            Book = metadata.Title,
            Author = metadata.Author,
            Scenes = [.. metadata.Scenes],
            Codex = [.. metadata.Codex]
        };

        foreach (var plotline in (book?.Plotlines ?? []).OrderBy(p => p.Order))
        {
            archive.Plotlines.Add(new ArchivedPlotline(
                plotline.Name,
                plotline.Importance.ToString(),
                plotline.Description,
                [.. plotline.Steps.OrderBy(s => s.Order)
                    .Select(s => s.Resolved ? $"[done] {s.Text}" : s.Text)],
                plotline.UnresolvedSteps));
        }

        foreach (var item in (project?.ResearchItems ?? []).OrderBy(r => r.Order))
        {
            archive.Research.Add(new ArchivedResearch(
                item.Title, item.Type.ToString(), [.. item.Tags], item.Content));
        }

        foreach (var list in project?.SmartLists ?? [])
        {
            archive.SmartLists.Add(new ArchivedSmartList(
                list.Name,
                list.Match.ToString(),
                // The rule as a sentence: a JSON blob of field/op/value says
                // less to a reader than "pov contains Mira".
                [.. list.Rules.Select(r => $"{r.Field} {r.Op} {r.Value}".Trim())]));
        }

        foreach (var collection in book?.Collections ?? [])
            archive.Collections.Add(new ArchivedCollection(collection.Name, collection.SceneIds.Count));

        foreach (var map in book?.Maps ?? [])
            archive.Maps.Add(map.Name);

        return archive;
    }

    /// <summary>The archive as JSON, indented so a person can read it too.</summary>
    public static string Json(WorldArchiveDocument archive)
        => JsonSerializer.Serialize(archive, JsonOptions);

    /// <summary>
    /// The archive as one browsable page.
    ///
    /// A single file rather than a folder of them: it opens by double-clicking,
    /// survives being emailed, and cannot arrive with its stylesheet missing.
    /// Everything is inline for the same reason.
    /// </summary>
    public static string Html(WorldArchiveDocument archive)
    {
        var page = new StringBuilder();
        page.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Escape(archive.Book)).Append("</title>\n<style>\n");
        page.Append("""
            :root { color-scheme: light dark; }
            body { font-family: Georgia, serif; line-height: 1.5; margin: 0 auto;
                   max-width: 46rem; padding: 2rem 1rem; }
            h1 { margin-bottom: 0; }
            .by { color: #777; margin-top: .25rem; }
            nav a { margin-right: 1rem; }
            section { margin-top: 2.5rem; }
            table { border-collapse: collapse; width: 100%; }
            th, td { border-bottom: 1px solid #8884; padding: .35rem .5rem;
                     text-align: left; vertical-align: top; }
            th { font-size: .85em; text-transform: uppercase; letter-spacing: .05em; }
            .empty { color: #777; font-style: italic; }
            .entry { margin-bottom: 1.25rem; }
            .entry h3 { margin-bottom: .2rem; }
            .kind { color: #777; font-size: .85em; }
            """);
        page.Append("\n</style>\n</head>\n<body>\n");

        page.Append("<h1>").Append(Escape(archive.Book)).Append("</h1>\n");
        if (!string.IsNullOrWhiteSpace(archive.Author))
            page.Append("<p class=\"by\">").Append(Escape(archive.Author)).Append("</p>\n");

        page.Append("<nav>");
        foreach (var (id, label) in Sections)
            page.Append("<a href=\"#").Append(id).Append("\">").Append(label).Append("</a>");
        page.Append("</nav>\n");

        Section(page, "scenes", "Scenes", archive.Scenes.Count, () =>
        {
            page.Append("<table><tr><th>Chapter</th><th>Scene</th><th>Words</th><th>Synopsis</th></tr>");
            foreach (var scene in archive.Scenes)
            {
                page.Append("<tr><td>").Append(Escape(scene.Chapter))
                    .Append("</td><td>").Append(Escape(scene.Scene))
                    .Append("</td><td>").Append(scene.Words)
                    .Append("</td><td>").Append(Escape(scene.Synopsis)).Append("</td></tr>");
            }
            page.Append("</table>");
        });

        Section(page, "codex", "Codex", archive.Codex.Count, () =>
        {
            foreach (var entry in archive.Codex)
            {
                page.Append("<div class=\"entry\"><h3>").Append(Escape(entry.Name))
                    .Append("</h3><div class=\"kind\">").Append(Escape(entry.Kind)).Append("</div>");
                foreach (var pair in entry.Properties)
                    page.Append("<div><b>").Append(Escape(pair.Key)).Append("</b>: ")
                        .Append(Escape(pair.Value)).Append("</div>");
                foreach (var pair in entry.Sections)
                    page.Append("<div><b>").Append(Escape(pair.Key)).Append("</b><br>")
                        .Append(Escape(pair.Value)).Append("</div>");
                foreach (var tie in entry.Relationships)
                    page.Append("<div>").Append(Escape(tie)).Append("</div>");
                page.Append("</div>");
            }
        });

        Section(page, "plotlines", "Plot threads", archive.Plotlines.Count, () =>
        {
            foreach (var plotline in archive.Plotlines)
            {
                page.Append("<div class=\"entry\"><h3>").Append(Escape(plotline.Name))
                    .Append("</h3><div class=\"kind\">").Append(Escape(plotline.Importance))
                    .Append(", ").Append(plotline.Unresolved).Append(" open</div>")
                    .Append("<div>").Append(Escape(plotline.Description)).Append("</div><ul>");
                foreach (var step in plotline.Steps)
                    page.Append("<li>").Append(Escape(step)).Append("</li>");
                page.Append("</ul></div>");
            }
        });

        Section(page, "research", "Research", archive.Research.Count, () =>
        {
            foreach (var item in archive.Research)
            {
                page.Append("<div class=\"entry\"><h3>").Append(Escape(item.Title))
                    .Append("</h3><div class=\"kind\">").Append(Escape(item.Kind));
                if (item.Tags.Count > 0)
                    page.Append(" - ").Append(Escape(string.Join(", ", item.Tags)));
                page.Append("</div><div>").Append(Escape(item.Content)).Append("</div></div>");
            }
        });

        Section(page, "lists", "Saved lists", archive.SmartLists.Count, () =>
        {
            foreach (var list in archive.SmartLists)
            {
                page.Append("<div class=\"entry\"><h3>").Append(Escape(list.Name))
                    .Append("</h3><div class=\"kind\">matches ").Append(Escape(list.Match))
                    .Append("</div><ul>");
                foreach (var rule in list.Rules)
                    page.Append("<li>").Append(Escape(rule)).Append("</li>");
                page.Append("</ul></div>");
            }
        });

        Section(page, "collections", "Collections", archive.Collections.Count, () =>
        {
            page.Append("<ul>");
            foreach (var collection in archive.Collections)
                page.Append("<li>").Append(Escape(collection.Name))
                    .Append(" - ").Append(collection.Scenes).Append(" scenes</li>");
            page.Append("</ul>");
        });

        Section(page, "maps", "Maps", archive.Maps.Count, () =>
        {
            page.Append("<ul>");
            foreach (var map in archive.Maps)
                page.Append("<li>").Append(Escape(map)).Append("</li>");
            page.Append("</ul>");
        });

        page.Append("</body>\n</html>\n");
        return page.ToString();
    }

    private static readonly (string Id, string Label)[] Sections =
    [
        ("scenes", "Scenes"), ("codex", "Codex"), ("plotlines", "Plot threads"),
        ("research", "Research"), ("lists", "Saved lists"),
        ("collections", "Collections"), ("maps", "Maps")
    ];

    /// <summary>
    /// A section, with its heading and its count. An empty one says so rather
    /// than being left out: "no research" and "research was not exported" are
    /// different things and the reader cannot tell them apart from a gap.
    /// </summary>
    private static void Section(StringBuilder page, string id, string label, int count, Action body)
    {
        page.Append("<section id=\"").Append(id).Append("\"><h2>").Append(label)
            .Append(" <span class=\"kind\">(").Append(count).Append(")</span></h2>\n");
        if (count == 0) page.Append("<p class=\"empty\">Nothing here.</p>");
        else body();
        page.Append("\n</section>\n");
    }

    private static string Escape(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
