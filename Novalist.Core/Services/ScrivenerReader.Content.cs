using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public static partial class ScrivenerReader
{
    /// <summary>
    /// Records one draft document.
    ///
    /// A document with no prose still lands. Outlining in empty binder
    /// documents is how a Scrivener project starts, and an importer that reads
    /// only the ones with text in them turns a planned book into an empty one -
    /// the stock novel template, imported, produced nothing at all.
    /// </summary>
    private static void AddScene(
        XElement item, Context ctx, Node part, Node chapter, string title, RichContent content)
    {
        var sceneTitle = ctx.SceneTitle(chapter.Key, title);
        var notes = ReadRtf(item, ctx, "notes.rtf", "_notes.rtf");
        ctx.Scenes.Add(new ScrivenerScene(
            part.Key,
            part.Title,
            chapter.Key,
            chapter.Title,
            sceneTitle,
            content.Text,
            content.Html,
            ReadPlain(DocumentPath(item, ctx, "synopsis.txt", "_synopsis.txt")),
            notes.Text,
            ctx.LabelName(MetaOf(item, "LabelID")),
            ctx.StatusName(MetaOf(item, "StatusID")),
            !string.Equals(MetaOf(item, "IncludeInCompile"), "No", StringComparison.OrdinalIgnoreCase),
            CustomFieldsOf(item, ctx),
            ctx.Target.Kind,
            ctx.Target.Key,
            ctx.Target.Title));
    }

    /// <summary>
    /// This document's custom metadata, keyed by field id.
    ///
    /// Scrivener has written the item two ways across its 3.x line - the value
    /// as the element's own text with the field in an ID attribute, and the
    /// value in a Value child beside a FieldID one - so both are read. A field
    /// the project never declared is ignored: without its title there is
    /// nothing to call the column.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CustomFieldsOf(XElement item, Context ctx)
    {
        var custom = item.Element("MetaData")?.Element("CustomMetaData");
        if (custom == null) return EmptyFields;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in custom.Elements("MetaDataItem"))
        {
            var id = ((string?)entry.Attribute("ID")
                      ?? (string?)entry.Element("FieldID") ?? string.Empty).Trim();
            var value = (entry.Element("Value")?.Value ?? entry.Value).Trim();
            if (id.Length > 0 && value.Length > 0 && ctx.HasCustomField(id)) values[id] = value;
        }

        return values.Count > 0 ? values : EmptyFields;
    }

    // ── Codex entries ────────────────────────────────────────────────────

    /// <summary>
    /// Every filled-in sketch under a characters or places folder, flattened -
    /// a writer who groups characters by house still gets the characters.
    /// </summary>
    private static void WalkEntities(XElement item, ScrivenerEntityKind kind, Context ctx)
    {
        var children = item.Element("Children");
        if (children != null)
        {
            foreach (var child in children.Elements("BinderItem"))
                WalkEntities(child, EntityKindOf(child) ?? kind, ctx);
        }

        var text = ReadRtf(item, ctx, "content.rtf", ".rtf");
        var notes = ReadRtf(item, ctx, "notes.rtf", "_notes.rtf");
        if (text.IsEmpty && notes.IsEmpty) return;

        ctx.Entities.Add(new ScrivenerEntity(
            kind, TitleOf(item), text.Text, notes.Text, text.Markdown, notes.Markdown));
    }

    // ── Everything else that carried content ─────────────────────────────

    /// <summary>
    /// Research, notes and front matter. Folder titles come across as tags, so
    /// the shape of somebody's research survives even though its folders do not.
    /// </summary>
    /// <param name="respectEntityIcons">
    /// Whether a sketch filed in here should still become a Codex entry. True
    /// when nothing but the rules put this folder here, false when the writer
    /// named it research themselves - saying research and getting characters
    /// anyway would make the choice meaningless.
    /// </param>
    private static void WalkResearch(
        XElement item, Context ctx, string folderTag, bool respectEntityIcons = true)
    {
        var title = TitleOf(item);
        var children = item.Element("Children");
        var childItems = children?.Elements("BinderItem").ToList() ?? [];

        if (childItems.Count > 0)
        {
            // The nearest folder, not the outermost: "Sample Output" says more
            // about a document than "Research" does.
            var tag = title;
            foreach (var child in childItems)
            {
                var kind = respectEntityIcons ? EntityKindOf(child) : null;
                if (kind != null) WalkEntities(child, kind.Value, ctx);
                else WalkResearch(child, ctx, tag, respectEntityIcons);
            }
        }

        var type = TypeOf(item);
        if (type is Folder or ResearchFolder && childItems.Count > 0) return;

        if (string.Equals(type, "PDF", StringComparison.OrdinalIgnoreCase))
        {
            AddFile(item, ctx, title, ScrivenerResearchKind.Pdf, folderTag, "pdf");
            return;
        }

        if (string.Equals(type, "Image", StringComparison.OrdinalIgnoreCase))
        {
            AddFile(item, ctx, title, ScrivenerResearchKind.Image, folderTag,
                "png", "jpg", "jpeg", "gif", "webp");
            return;
        }

        // Anything else Scrivener imported whole - an interview recording, a
        // location walk-through, a spreadsheet - names its own extension rather
        // than having a binder type of its own.
        var extension = MetaOf(item, "FileExtension").TrimStart('.');
        if (extension.Length > 0)
        {
            AddFile(item, ctx, title, ScrivenerResearchKind.File, folderTag, extension);
            return;
        }

        var text = ReadRtf(item, ctx, "content.rtf", ".rtf");
        if (text.IsEmpty) return;

        ctx.Research.Add(new ScrivenerResearch(
            title, ScrivenerResearchKind.Note, text.Text, text.Markdown, string.Empty, folderTag));
    }

    private static string TitleOf(XElement item)
        => ((string?)item.Element("Title") ?? string.Empty).Trim();

    private static string TypeOf(XElement item)
        => ((string?)item.Attribute("Type") ?? string.Empty).Trim();

    private static string UuidOf(XElement item)
        => ((string?)item.Attribute("UUID") ?? string.Empty).Trim();

    private static string MetaOf(XElement item, string name)
        => ((string?)item.Element("MetaData")?.Element(name) ?? string.Empty).Trim();

    /// <summary>
    /// Whether this entry holds characters or places, and which.
    ///
    /// Read from the icon Scrivener assigns - "Characters (Photo)" on the
    /// folder, "Characters (Character Sheet)" on a sheet inside it. The icon
    /// name is an internal identifier and stays put when the writer renames the
    /// folder or runs Scrivener in another language, both of which the titles
    /// do not survive. The stock English titles are still honoured as a
    /// fallback, for a project old enough to carry no icon at all.
    /// </summary>
    private static ScrivenerEntityKind? EntityKindOf(XElement item)
    {
        var icon = MetaOf(item, "IconFileName");
        if (icon.StartsWith("Characters", StringComparison.OrdinalIgnoreCase))
            return ScrivenerEntityKind.Character;
        if (icon.StartsWith("Locations", StringComparison.OrdinalIgnoreCase))
            return ScrivenerEntityKind.Location;
        if (icon.Length > 0) return null;

        return TitleOf(item).ToLowerInvariant() switch
        {
            "characters" or "character sketch" => ScrivenerEntityKind.Character,
            "places" or "settings" or "setting sketch" => ScrivenerEntityKind.Location,
            _ => null
        };
    }

    // ── Files on disk ────────────────────────────────────────────────────

    /// <summary>
    /// Where one document's file lives.
    ///
    /// Scrivener 3 keys on a UUID folder and names the files inside it, so
    /// <paramref name="name3"/> is that name. Scrivener 2 keys on a numeric id
    /// and puts the distinguishing part in the filename, so
    /// <paramref name="suffix2"/> is what follows the id - ".rtf", "_notes.rtf",
    /// "_synopsis.txt", or the file's own extension for an imported one.
    /// </summary>
    private static string? DocumentPath(XElement item, Context ctx, string name3, string suffix2)
    {
        if (ctx.Version == "3")
        {
            var uuid = UuidOf(item);
            if (uuid.Length == 0) return null;
            var folder = FindDirectory(ctx.DataRoot, uuid);
            return FindFile(folder, name3);
        }

        var id = ((string?)item.Attribute("ID") ?? string.Empty).Trim();
        if (id.Length == 0) return null;

        // Files/Docs/12.rtf, 12_notes.rtf, 12_synopsis.txt - and 12.pdf for an
        // imported file, which keeps its own extension rather than taking a suffix.
        return FindFile(ctx.DocsRoot, id + suffix2);
    }

    /// <summary>An RTF document's plain text, semantic editor HTML and Markdown.
    /// Empty when there is no such file.</summary>
    private static RichContent ReadRtf(
        XElement item, Context ctx, string name3, string suffix2)
    {
        var file = DocumentPath(item, ctx, name3, suffix2);
        if (file == null || !File.Exists(file)) return RichContent.Empty;

        // Read wraps the whole walk in the same catch, so a file that vanishes
        // mid-import degrades to an empty project rather than needing a second
        // guard here.
        var document = ManuscriptReader.ReadRtf(File.ReadAllBytes(file));
        var paragraphs = ScrivenerFormatting.Apply(
            document.Paragraphs,
            ctx.Styles,
            ReadStyleIds(FindFile(
                Path.GetDirectoryName(file),
                Path.GetFileNameWithoutExtension(file) + ".styles")));
        return new RichContent(
            ImportedRichText.ToPlainText(paragraphs),
            ImportedRichText.ToHtml(paragraphs),
            ImportedRichText.ToMarkdown(paragraphs),
            paragraphs);
    }

    private static IReadOnlyList<string> ReadStyleIds(string? path)
        => path != null && File.Exists(path)
            ? File.ReadAllText(path)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    private sealed record RichContent(
        string Text,
        string Html,
        string Markdown,
        IReadOnlyList<ImportedParagraph> Paragraphs)
    {
        public static RichContent Empty { get; } = new(string.Empty, string.Empty, string.Empty, []);
        public bool IsEmpty => Paragraphs.Count == 0;
    }

    private static string ReadPlain(string? file)
        => file != null && File.Exists(file) ? File.ReadAllText(file).Trim() : string.Empty;
}
