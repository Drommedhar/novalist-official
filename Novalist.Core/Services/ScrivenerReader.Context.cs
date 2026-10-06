using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public static partial class ScrivenerReader
{
    /// <summary>What one read needs to know about the project it is walking.</summary>
    private sealed class Context
    {
        private readonly Dictionary<string, string> _labels;
        private readonly Dictionary<string, string> _statuses;
        private readonly Dictionary<string, int> _scenePositions = new(StringComparer.Ordinal);

        public Context(
            string? filesRoot,
            string? dataRoot,
            string? docsRoot,
            string version,
            XDocument document)
        {
            DataRoot = dataRoot;
            DocsRoot = docsRoot;
            Version = version;
            TemplateFolderUuid = (document.Descendants("TemplateFolderUUID").FirstOrDefault()
                ?.Value ?? string.Empty).Trim();
            _labels = NamesById(document, "LabelSettings", "Label");
            _statuses = NamesById(document, "StatusSettings", "Status");
            CustomFields = ReadCustomFields(document);
            _customIds = [.. CustomFields.Select(f => f.Id)];
            Styles = ReadStyles(filesRoot);
        }

        private readonly HashSet<string> _customIds;

        /// <summary>The project's own custom metadata fields, in declared order.</summary>
        public List<ScrivenerCustomField> CustomFields { get; }
        public IReadOnlyDictionary<string, ScrivenerStyleInfo> Styles { get; }

        public bool HasCustomField(string id) => _customIds.Contains(id);

        public string SceneTitle(string chapterKey, string title)
        {
            var position = _scenePositions.TryGetValue(chapterKey, out var previous) ? previous + 1 : 1;
            _scenePositions[chapterKey] = position;
            return title.Length > 0 ? title : $"Scene {position}";
        }

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

        private static IReadOnlyDictionary<string, ScrivenerStyleInfo> ReadStyles(string? filesRoot)
        {
            var path = FindFile(filesRoot, "styles.xml");
            if (path == null || !File.Exists(path))
                return new Dictionary<string, ScrivenerStyleInfo>(StringComparer.Ordinal);

            var result = new Dictionary<string, ScrivenerStyleInfo>(StringComparer.Ordinal);
            var document = LoadXml(path);
            foreach (var style in document.Descendants("Style"))
            {
                var id = ((string?)style.Attribute("ID") ?? string.Empty).Trim();
                if (id.Length == 0) continue;

                var name = ((string?)style.Attribute("Name") ?? string.Empty).Trim();
                var type = ((string?)style.Attribute("Type") ?? string.Empty).Trim();
                var format = style.Element("Format")?.Value ?? string.Empty;
                var parsed = ManuscriptReader.ReadRtf(format);
                var paragraph = parsed.Paragraphs.FirstOrDefault();
                var runs = parsed.Paragraphs.SelectMany(p => p.Runs).ToList();
                var headingMatch = Regex.Match(format, @"<\$Scr_H::(?<level>\d+)>",
                    RegexOptions.CultureInvariant);
                var heading = headingMatch.Success
                    && int.TryParse(headingMatch.Groups["level"].Value, out var level)
                    ? Math.Clamp(level, 1, 6)
                    : 0;

                var paragraphStyle = name.ToLowerInvariant() switch
                {
                    "title" or "heading 1" => ImportedParagraphStyle.Heading,
                    "heading 2" => ImportedParagraphStyle.Subheading,
                    "block quote" or "blockquote" => ImportedParagraphStyle.BlockQuote,
                    "verse" or "poetry" => ImportedParagraphStyle.Poetry,
                    _ => ImportedParagraphStyle.Normal
                };

                result[id] = new ScrivenerStyleInfo(
                    id,
                    name,
                    type.Contains("Char", StringComparison.OrdinalIgnoreCase),
                    runs.Any(r => r.Bold),
                    runs.Any(r => r.Italic),
                    runs.Any(r => r.Underline),
                    runs.Any(r => r.Strike),
                    heading,
                    paragraphStyle,
                    paragraph?.Alignment ?? ImportedTextAlignment.Default);
            }

            return result;
        }

        public string? DataRoot { get; }
        public string? DocsRoot { get; }
        public string Version { get; }
        public string TemplateFolderUuid { get; }

        /// <summary>Which book or draft the walk is currently filling. Set while
        /// a folder mapped to a draft or a book of its own is being read.</summary>
        public ScrivenerTarget Target { get; set; } = ScrivenerTarget.Manuscript;

        /// <summary>
        /// The chapter a document with no folder around it lands in, one per book
        /// or draft.
        ///
        /// A draft that never got chapter folders is just a run of documents, and
        /// four of the nine in the project this was reported from looked like
        /// that. With one shared key they all landed in a single chapter that
        /// belonged to whichever draft was read first, so that draft held
        /// everybody's documents and the other three were created empty.
        /// </summary>
        public Node LooseChapter => new(Node.LooseChapterKey + Target.Key, DefaultChapterTitle);

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
