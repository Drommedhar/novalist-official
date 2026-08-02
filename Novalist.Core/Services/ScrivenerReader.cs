using System.Xml.Linq;

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
    string Synopsis,
    string Notes,
    string Label,
    string Status,
    bool IncludeInCompile,
    IReadOnlyDictionary<string, string> CustomFields);

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
    string Notes);

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
/// Reads a Scrivener project folder.
///
/// Both layouts are handled because both are in the wild: Scrivener 2 numbers
/// its documents and keeps them in <c>Files/Docs/&lt;id&gt;.rtf</c>, Scrivener 3
/// gives each a UUID folder under <c>Files/Data/</c>. The binder tree in the
/// <c>.scrivx</c> is the same shape in both, which is what makes one reader
/// enough.
///
/// What the binder means is read from its <c>Type</c> attributes and its icon
/// names, never from the titles. An earlier reader matched the strings
/// "Research", "Trash" and "Front Matter", which is wrong twice over: those
/// titles are the writer's to change and are already translated in a
/// non-English Scrivener, and nothing at all marked the draft. So the draft
/// folder - titled "Manuscript" in the stock template - looked like an ordinary
/// chapter folder, and every part, chapter and scene beneath it collapsed into
/// one chapter called Manuscript, while the template's own instruction sheet
/// was imported as prose.
///
/// The import is still lossy and still says so. Snapshots, collections, compile
/// settings and label colours have no equivalent here, and pretending otherwise
/// would leave a writer discovering the gaps one at a time.
/// </summary>
public static class ScrivenerReader
{
    /// <summary>The extension a Scrivener project folder carries.</summary>
    public const string ProjectExtension = ".scriv";

    /// <summary>The chapter a draft document lands in when the binder gave it
    /// no folder of its own.</summary>
    public const string DefaultChapterTitle = "Imported";

    // Binder Type attributes that mark a container Scrivener owns rather than
    // the writer. These are stable identifiers, unlike the titles beside them.
    private const string DraftFolder = "DraftFolder";
    private const string ResearchFolder = "ResearchFolder";
    private const string TrashFolder = "TrashFolder";
    private const string Folder = "Folder";

    /// <summary>Whether a path looks like a Scrivener project. True for the
    /// folder and for the .scrivx inside it, since a file picker gives one or
    /// the other depending on the platform.</summary>
    public static bool LooksLikeScrivener(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path))
            return Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);
        return Path.GetExtension(path).Equals(".scrivx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a project. Returns an empty result rather than throwing for
    /// anything unreadable: an import that cannot start should say so in the
    /// dialog, not crash the app.
    /// </summary>
    public static ScrivenerProject Read(string path)
    {
        try
        {
            var root = ResolveRoot(path);
            if (root == null) return new ScrivenerProject();

            var scrivx = Directory.EnumerateFiles(root, "*.scrivx").FirstOrDefault();
            if (scrivx == null) return new ScrivenerProject();

            var document = XDocument.Load(scrivx);
            var binder = document.Descendants("Binder").FirstOrDefault();
            if (binder == null) return new ScrivenerProject();

            // Scrivener 3 keeps documents under Files/Data; Scrivener 2 under
            // Files/Docs. Which folder exists is what tells them apart.
            var version = Directory.Exists(Path.Combine(root, "Files", "Data")) ? "3" : "2";
            var ctx = new Context(root, version, document);

            var top = binder.Elements("BinderItem").ToList();
            // A binder with no draft folder is not a Scrivener-authored project -
            // a hand-made one, or a fragment. Reading the whole binder as the
            // manuscript is right there, and wrong the moment a draft exists.
            var draft = top.FirstOrDefault(i => TypeOf(i) == DraftFolder);
            if (draft == null)
            {
                foreach (var item in top) WalkDraft(item, ctx, Node.Empty, Node.Empty);
            }
            else
            {
                foreach (var item in top) WalkTop(item, draft, ctx);
            }

            return new ScrivenerProject
            {
                Scenes = ctx.Scenes,
                Entities = ctx.Entities,
                Research = ctx.Research,
                CustomFields = ctx.CustomFields,
                Version = version,
                Losses = [.. ctx.Losses.Distinct(StringComparer.Ordinal)]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            return new ScrivenerProject();
        }
    }

    /// <summary>The project folder, whether the caller pointed at it or at the
    /// .scrivx inside it.</summary>
    private static string? ResolveRoot(string path)
    {
        if (Directory.Exists(path)) return path;
        if (File.Exists(path)) return Path.GetDirectoryName(path);
        return null;
    }

    // ── The binder's top level ───────────────────────────────────────────

    /// <summary>
    /// One top-level binder entry, routed by what Scrivener says it is.
    ///
    /// Only the draft becomes chapters and scenes. Everything else that carried
    /// content becomes a Codex entry or a research item, because dropping a
    /// character sheet somebody filled in is not an acceptable import.
    /// </summary>
    private static void WalkTop(XElement item, XElement draft, Context ctx)
    {
        var title = TitleOf(item);

        if (ReferenceEquals(item, draft))
        {
            var children = item.Element("Children");
            if (children != null)
            {
                foreach (var child in children.Elements("BinderItem"))
                    WalkDraft(child, ctx, Node.Empty, Node.Empty);
            }

            return;
        }

        if (TypeOf(item) == TrashFolder)
        {
            ctx.Losses.Add(title);
            return;
        }

        // The template sheets are Scrivener's blank forms, not the writer's
        // filled-in ones. Importing them produces a character called "Character
        // Sketch" whose every field is a prompt.
        if (UuidOf(item) is { Length: > 0 } uuid && uuid == ctx.TemplateFolderUuid)
        {
            ctx.Losses.Add(title);
            return;
        }

        var kind = EntityKindOf(item);
        if (kind != null)
        {
            WalkEntities(item, kind.Value, ctx);
            return;
        }

        WalkResearch(item, ctx, folderTag: string.Empty);
    }

    // ── The draft ────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the draft, turning the first level of folders into parts when they
    /// hold folders of their own and into chapters when they do not.
    ///
    /// Scrivener nests arbitrarily and Novalist is three levels - act, chapter,
    /// scene - so anything below a chapter flattens into it. That loses nesting
    /// rather than text, which is the right way round.
    /// </summary>
    private static void WalkDraft(XElement item, Context ctx, Node part, Node chapter)
    {
        var title = TitleOf(item);
        var children = item.Element("Children");
        var childItems = children?.Elements("BinderItem").ToList() ?? [];
        var type = TypeOf(item);
        var isFolder = type is Folder or DraftFolder || childItems.Count > 0;

        if (isFolder)
        {
            Node nextPart = part, nextChapter = chapter;
            var self = new Node(KeyOf(item, ctx), title);
            if (part.IsEmpty && chapter.IsEmpty && childItems.Any(c => TypeOf(c) == Folder))
            {
                // Holds folders, and nothing above it does: a part.
                nextPart = self;
            }
            else if (chapter.IsEmpty)
            {
                nextChapter = self;
            }

            foreach (var child in childItems)
                WalkDraft(child, ctx, nextPart, nextChapter);

            // A folder can carry text of its own - a chapter with an epigraph.
            var folderText = ReadRtf(DocumentPath(item, ctx, "content.rtf", ".rtf"));
            if (folderText.Length > 0)
                AddScene(item, ctx, nextPart, nextChapter.IsEmpty ? self : nextChapter, title, folderText);

            return;
        }

        AddScene(item, ctx, part, chapter.IsEmpty ? Node.DefaultChapter : chapter, title,
            ReadRtf(DocumentPath(item, ctx, "content.rtf", ".rtf")));
    }

    /// <summary>A part or chapter the walk is inside: its binder identity and
    /// the title to show for it.</summary>
    private readonly record struct Node(string Key, string Title)
    {
        public static Node Empty => new(string.Empty, string.Empty);

        /// <summary>The chapter for a draft document the binder gave no folder.</summary>
        public static Node DefaultChapter => new(" loose", DefaultChapterTitle);

        public bool IsEmpty => Key.Length == 0;
    }

    /// <summary>A binder item's identity: its UUID in Scrivener 3, its numeric
    /// ID in Scrivener 2.</summary>
    private static string KeyOf(XElement item, Context ctx)
    {
        var key = ctx.Version == "3"
            ? UuidOf(item)
            : ((string?)item.Attribute("ID") ?? string.Empty).Trim();
        // A folder with no identity at all still has to be distinguishable from
        // the next one, or two untitled chapters merge.
        return key.Length > 0 ? key : " " + item.GetHashCode().ToString();
    }

    /// <summary>
    /// Records one draft document.
    ///
    /// A document with no prose still lands. Outlining in empty binder
    /// documents is how a Scrivener project starts, and an importer that reads
    /// only the ones with text in them turns a planned book into an empty one -
    /// the stock novel template, imported, produced nothing at all.
    /// </summary>
    private static void AddScene(
        XElement item, Context ctx, Node part, Node chapter, string title, string text)
    {
        ctx.Scenes.Add(new ScrivenerScene(
            part.Key,
            part.Title,
            chapter.Key,
            chapter.Title,
            title,
            text,
            ReadPlain(DocumentPath(item, ctx, "synopsis.txt", "_synopsis.txt")),
            ReadRtf(DocumentPath(item, ctx, "notes.rtf", "_notes.rtf")),
            ctx.LabelName(MetaOf(item, "LabelID")),
            ctx.StatusName(MetaOf(item, "StatusID")),
            !string.Equals(MetaOf(item, "IncludeInCompile"), "No", StringComparison.OrdinalIgnoreCase),
            CustomFieldsOf(item, ctx)));
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

        var text = ReadRtf(DocumentPath(item, ctx, "content.rtf", ".rtf"));
        var notes = ReadRtf(DocumentPath(item, ctx, "notes.rtf", "_notes.rtf"));
        if (text.Length == 0 && notes.Length == 0) return;

        ctx.Entities.Add(new ScrivenerEntity(kind, TitleOf(item), text, notes));
    }

    // ── Everything else that carried content ─────────────────────────────

    /// <summary>
    /// Research, notes and front matter. Folder titles come across as tags, so
    /// the shape of somebody's research survives even though its folders do not.
    /// </summary>
    private static void WalkResearch(XElement item, Context ctx, string folderTag)
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
                var kind = EntityKindOf(child);
                if (kind != null) WalkEntities(child, kind.Value, ctx);
                else WalkResearch(child, ctx, tag);
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

        var text = ReadRtf(DocumentPath(item, ctx, "content.rtf", ".rtf"));
        if (text.Length == 0) return;

        ctx.Research.Add(new ScrivenerResearch(
            title, ScrivenerResearchKind.Note, text, string.Empty, folderTag));
    }

    /// <summary>A file-backed research item, whose bytes the import copies.</summary>
    private static void AddFile(
        XElement item, Context ctx, string title, ScrivenerResearchKind kind,
        string folderTag, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var file = DocumentPath(item, ctx, "content." + extension, "." + extension);
            if (file != null && File.Exists(file))
            {
                ctx.Research.Add(new ScrivenerResearch(
                    title, kind, string.Empty, file, folderTag));
                return;
            }
        }
    }

    // ── Binder vocabulary ────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> EmptyFields =
        new Dictionary<string, string>(StringComparer.Ordinal);

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
            return uuid.Length == 0 ? null : Path.Combine(ctx.Root, "Files", "Data", uuid, name3);
        }

        var id = ((string?)item.Attribute("ID") ?? string.Empty).Trim();
        if (id.Length == 0) return null;

        // Files/Docs/12.rtf, 12_notes.rtf, 12_synopsis.txt - and 12.pdf for an
        // imported file, which keeps its own extension rather than taking a suffix.
        return Path.Combine(ctx.Root, "Files", "Docs", id + suffix2);
    }

    /// <summary>An RTF document's prose, through the reader a plain .rtf import
    /// already uses. Empty when there is no such file.</summary>
    private static string ReadRtf(string? file)
    {
        if (file == null || !File.Exists(file)) return string.Empty;

        // Read wraps the whole walk in the same catch, so a file that vanishes
        // mid-import degrades to an empty project rather than needing a second
        // guard here.
        var document = ManuscriptReader.ReadRtf(File.ReadAllText(file));
        return string.Join("\n\n", document.Paragraphs.Select(p => p.Text)).Trim();
    }

    private static string ReadPlain(string? file)
        => file != null && File.Exists(file) ? File.ReadAllText(file).Trim() : string.Empty;

    /// <summary>What one read needs to know about the project it is walking.</summary>
    private sealed class Context
    {
        private readonly Dictionary<string, string> _labels;
        private readonly Dictionary<string, string> _statuses;

        public Context(string root, string version, XDocument document)
        {
            Root = root;
            Version = version;
            TemplateFolderUuid = (document.Descendants("TemplateFolderUUID").FirstOrDefault()
                ?.Value ?? string.Empty).Trim();
            _labels = NamesById(document, "LabelSettings", "Label");
            _statuses = NamesById(document, "StatusSettings", "Status");
            CustomFields = ReadCustomFields(document);
            _customIds = [.. CustomFields.Select(f => f.Id)];
        }

        private readonly HashSet<string> _customIds;

        /// <summary>The project's own custom metadata fields, in declared order.</summary>
        public List<ScrivenerCustomField> CustomFields { get; }

        public bool HasCustomField(string id) => _customIds.Contains(id);

        /// <summary>
        /// The custom metadata fields a Scrivener 3 project declares. A field
        /// carries a title, and a list one carries the values it allows, which
        /// is enough to rebuild it as a typed column rather than free text.
        /// </summary>
        private static List<ScrivenerCustomField> ReadCustomFields(XDocument document)
        {
            var fields = new List<ScrivenerCustomField>();
            var settings = document.Descendants("CustomMetaDataSettings").FirstOrDefault();
            if (settings == null) return fields;

            foreach (var field in settings.Elements("MetaDataField"))
            {
                var id = ((string?)field.Attribute("ID") ?? string.Empty).Trim();
                if (id.Length == 0) continue;

                var title = (field.Element("Title")?.Value ?? id).Trim();
                var options = field.Descendants("ListItem")
                    .Select(o => o.Value.Trim())
                    .Where(o => o.Length > 0)
                    .ToList();
                fields.Add(new ScrivenerCustomField(id, title.Length > 0 ? title : id, options));
            }

            return fields;
        }

        public string Root { get; }
        public string Version { get; }
        public string TemplateFolderUuid { get; }

        public List<ScrivenerScene> Scenes { get; } = [];
        public List<ScrivenerEntity> Entities { get; } = [];
        public List<ScrivenerResearch> Research { get; } = [];
        public List<string> Losses { get; } = [];

        public string LabelName(string id) => Lookup(_labels, id);
        public string StatusName(string id) => Lookup(_statuses, id);

        private static string Lookup(Dictionary<string, string> map, string id)
            // -1 is Scrivener's "No Label" and "No Status"; both are the absence
            // of a value rather than a value a writer would want as a tag.
            => id.Length == 0 || id == "-1" || !map.TryGetValue(id, out var name)
                ? string.Empty
                : name;

        /// <summary>The project's label or status vocabulary, by id.</summary>
        private static Dictionary<string, string> NamesById(
            XDocument document, string settings, string entry)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var root = document.Descendants(settings).FirstOrDefault();
            if (root == null) return map;

            foreach (var element in root.Descendants(entry))
            {
                var id = ((string?)element.Attribute("ID") ?? string.Empty).Trim();
                var name = element.Value.Trim();
                if (id.Length > 0 && name.Length > 0) map[id] = name;
            }

            return map;
        }
    }
}
