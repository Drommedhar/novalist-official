using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Novalist.Core.Services;

internal sealed class ParsedFolderImport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    public required ImportDefinition Definition { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string Source { get; init; }
    public string Metadata { get; init; } = string.Empty;
    public bool IsJson { get; init; }
    public List<string> Tags { get; init; } = [];
    public Dictionary<ImportField, JsonNode> Values { get; init; } = [];
    public List<EntitySection> Headings { get; init; } = [];
    public List<EntityImage> Images { get; init; } = [];
    public List<ImportImageReference> ImageReferences { get; init; } = [];
    public List<CharacterOverride> ChapterOverrides { get; init; } = [];

    public void Apply(object model, string sectionTitle)
    {
        if (Definition.Template != null && model is IEntityData templated)
            templated.TemplateId = Definition.Template.Id;
        if (model is CharacterData character)
        {
            character.AgeMode = Definition.Template?.AgeMode;
            character.AgeIntervalUnit = Definition.Template?.AgeIntervalUnit;
        }
        foreach (var (field, value) in Values)
        {
            // The service merges source tags, folder tags and the optional batch tag.
            if (field.Key is "tags" or "images" && field.Bucket is "builtIn" or "analysis") continue;
            if (field is ReflectedImportField reflected)
            {
                var destination = field.Bucket == "analysis" ? ((SceneData)model).AnalysisOverrides ??= new() : model;
                reflected.Property.SetValue(destination, value.Deserialize(field.ValueType, JsonOptions));
            }
            else
            {
                ApplyExtraField(model, field, value.GetValue<string>());
            }
        }
        if (model is CharacterData dated && !string.IsNullOrWhiteSpace(dated.BirthDate) && Values.Keys.Any(field => field.Key == "birthDate" && field.Bucket == "builtIn")
            && !Values.Keys.Any(field => field.Key == "ageMode" && field.Bucket == "builtIn"))
        {
            dated.AgeMode = "date";
            dated.AgeIntervalUnit ??= IntervalUnit.Years;
        }
        else if (model is CharacterData numbered && (!string.IsNullOrWhiteSpace(numbered.Age) || ChapterOverrides.Any(scope => !string.IsNullOrWhiteSpace(scope.Age))) && string.IsNullOrWhiteSpace(numbered.BirthDate)
            && !Values.Keys.Any(field => field.Key == "ageMode" && field.Bucket == "builtIn"))
            numbered.AgeMode = "number";
        if (model is CharacterData scoped) scoped.ChapterOverrides.AddRange(ChapterOverrides);
        if (model is IEntityData entity)
        {
            if (Images.Count > 0) entity.Images = Images;
            var sections = entity.Sections;
            sections.AddRange(Headings);
            var preserved = IsJson ? Content : Source;
            if (!string.IsNullOrWhiteSpace(preserved)) sections.Add(new EntitySection
            {
                Title = sectionTitle,
                Content = preserved,
                AiHidden = !IsJson && sections.Any(section => section.AiHidden),
                ReaderHidden = !IsJson && sections.Any(section => section.ReaderHidden)
            });
        }
    }

    private static void ApplyExtraField(object model, ImportField field, string value)
    {
        var properties = model switch
        {
            CustomEntityData custom when field.Bucket == "fields" => custom.Fields ??= [],
            SceneData scene => scene.Properties ??= [],
            ResearchItem research => research.Properties ??= [],
            IEntityData entity => entity.CustomProperties ??= [],
            _ => throw new ArgumentException("Unsupported import model.", nameof(model))
        };
        properties[field.Key] = value;
    }
}

/// <summary>Reads explicit metadata and bounded labelled phrases. No network access or free-prose inference.</summary>
// aislop-ignore-next-line complexity/function-too-long -- Primary-constructor class declaration; the scanner counts independent methods as one constructor body.
internal sealed partial class FolderImportParser(FolderImportSchema schema)
{
    private sealed record RawField(string Key, JsonNode? Value, string? Bucket = null, bool AllowExtra = true);
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["firstname"] = "name",
        ["givenname"] = "name",
        ["vorname"] = "name",
        ["名字"] = "name",
        ["名称"] = "name",
        ["lastname"] = "surname",
        ["familyname"] = "surname",
        ["nachname"] = "surname",
        ["姓氏"] = "surname",
        ["alias"] = "aliases",
        ["aliase"] = "aliases",
        ["别名"] = "aliases",
        ["geschlecht"] = "gender",
        ["性别"] = "gender",
        ["alter"] = "age",
        ["年龄"] = "age",
        ["birthday"] = "birthDate",
        ["dateofbirth"] = "birthDate",
        ["geburtsdatum"] = "birthDate",
        ["出生日期"] = "birthDate",
        ["rolle"] = "role",
        ["身份"] = "role",
        ["gruppe"] = "group",
        ["family"] = "group",
        ["familie"] = "group",
        ["团体"] = "group",
        ["eyecolour"] = "eyeColor",
        ["augenfarbe"] = "eyeColor",
        ["瞳色"] = "eyeColor",
        ["haircolour"] = "hairColor",
        ["haarfarbe"] = "hairColor",
        ["发色"] = "hairColor",
        ["haarlange"] = "hairLength",
        ["发长"] = "hairLength",
        ["große"] = "height",
        ["身高"] = "height",
        ["statur"] = "build",
        ["体型"] = "build",
        ["hautton"] = "skinTone",
        ["肤色"] = "skinTone",
        ["besonderemerkmale"] = "distinguishingFeatures",
        ["显著特征"] = "distinguishingFeatures",
        ["beschreibung"] = "description",
        ["描述"] = "description",
        ["typ"] = "type",
        ["类型"] = "type",
        ["parentlocation"] = "parent",
        ["ubergeordneterort"] = "parent",
        ["上级地点"] = "parent",
        ["herkunft"] = "origin",
        ["来源"] = "origin",
        ["kategorie"] = "category",
        ["类别"] = "category",
        ["tag"] = "tags",
        ["pointofview"] = "pov",
        ["viewpoint"] = "pov",
        ["summary"] = "synopsis",
        ["zusammenfassung"] = "synopsis",
        ["template"] = "templateId",
        ["vorlage"] = "templateId"
    };

    [GeneratedRegex(@"\A---\n(.*?)\n(?:---|\.\.\.)\s*(?:\n|$)", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
    [GeneratedRegex(@"^\s*(?:[-*+]\s+)?(?:\*\*)?(?<key>[\p{L}\p{N}_ .()/-]+?)\s*(?:\*\*)?[:：](?:\*\*)?\s*(?<value>.+)$", RegexOptions.NonBacktracking)]
    private static partial Regex Label();
    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$")]
    private static partial Regex Heading();
    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex TableCells();

    public ParsedFolderImport Parse(string relative, string text, string target)
    {
        if (Path.GetExtension(relative).Equals(".json", StringComparison.OrdinalIgnoreCase)) return ParseJson(text, target);
        var source = text.TrimStart('\uFEFF').Replace("\r\n", "\n");
        var matter = FrontMatter().Match(source);
        var body = matter.Success ? source[matter.Length..].Trim() : source.Trim();
        var raw = new List<RawField>();
        if (matter.Success)
        {
            try
            {
                if (ReadYaml(matter.Groups[1].Value) is JsonObject metadata) Collect(metadata, raw);
            }
            catch (Exception exception) when (exception is YamlException or FormatException)
            {
                // A broken header never costs the writer their file. Its exact
                // text is retained below and explicit body fields still work.
            }
        }
        var chapters = new List<RawChapter>();
        var images = new List<EntityImage>();
        var headings = ReadBody(body, raw, chapters, images, target);
        var templateHint = raw.FirstOrDefault(field => field.Bucket == null && Canonical(field.Key) == Normalize("templateId"))?.Value;
        var definition = schema.Definition(target, Text(templateHint));
        var values = Bind(raw, definition);
        if (target == "character") ReadAppearance(raw, definition, values);
        var isMarkdown = !Path.GetExtension(relative).Equals(".txt", StringComparison.OrdinalIgnoreCase);
        var fallback = isMarkdown ? headings.FirstOrDefault(heading => heading.Level == 1)?.Title ?? Path.GetFileNameWithoutExtension(relative) : Path.GetFileNameWithoutExtension(relative);
        var titleKey = target is "scene" or "research" ? "title" : "name";
        var title = values.FirstOrDefault(pair => pair.Key.Key == titleKey && pair.Key.Bucket == "builtIn").Value;
        // This older character-sheet format uses a full name in its H1, as
        // the existing plugin importer does. Explicit name fields still win.
        if (target == "character" && title == null && headings.Any(heading => Normalize(heading.Title) == "generalinformation"))
        {
            var parts = fallback.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && !values.ContainsKey(definition.Fields["surname"]))
            {
                values[definition.Fields["name"]] = JsonValue.Create(parts[0]);
                values[definition.Fields["surname"]] = JsonValue.Create(parts[1]);
            }
        }
        var sections = new List<EntitySection>();
        if (target is not ("scene" or "research"))
            foreach (var heading in headings.Where(heading => heading.Level > 1 && heading.Content.Length > 0))
                if (Bucket(heading.Title) == null && !MetadataHeading(heading.Title) && Resolve(definition, heading.Title, heading.Bucket) == null)
                    sections.Add(new EntitySection { Title = heading.Title, Content = heading.Content });
        return new ParsedFolderImport
        {
            Definition = definition,
            Title = Text(title) ?? fallback,
            Content = body,
            Source = source.Trim(),
            Values = values,
            Metadata = matter.Success ? matter.Value.Trim() : string.Empty,
            Tags = ReadTags(values),
            Headings = sections,
            Images = target is "scene" or "research" ? [] : images,
            ChapterOverrides = target == "character" ? ReadChapterOverrides(chapters, definition) : []
        };
    }

    private ParsedFolderImport ParseJson(string text, string target)
    {
        JsonObject document;
        try
        {
            using var parsed = JsonDocument.Parse(text.TrimStart('\uFEFF'), new JsonDocumentOptions { MaxDepth = 32 });
            document = JsonNode.Parse(parsed.RootElement.GetRawText()) as JsonObject ?? throw new FormatException();
            // JsonObject detects duplicate keys when materialised; validate every
            // nested object too rather than silently accepting last-key wins.
            RejectDuplicates(parsed.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new FormatException("Invalid Novalist import JSON.");
        }
        var definition = schema.Definition(target);
        if (!FolderImportSchema.Accepts(schema.DocumentSchema(definition), document)
            || document["data"] is not JsonObject data
            || data[target is "scene" or "research" ? "title" : "name"] is not JsonValue title
            || !title.TryGetValue<string>(out var titleText))
            throw new FormatException("The JSON does not match this target's import schema.");
        var raw = new List<RawField>();
        Collect(new JsonObject(data.Where(pair => pair.Key != "images").Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))), raw);
        var values = Bind(raw, definition, explicitKeys: true);
        return new ParsedFolderImport
        {
            Definition = definition,
            Title = titleText,
            Content = document["content"]?.GetValue<string>() ?? string.Empty,
            Source = text,
            IsJson = true,
            Values = values,
            Tags = ReadTags(values),
            ImageReferences = data["images"]?.Deserialize<List<ImportImageReference>>() ?? []
        };
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new FormatException("Duplicate JSON property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }

    private static void Collect(JsonObject map, List<RawField> raw)
    {
        foreach (var (key, value) in map)
        {
            var bucket = Bucket(key);
            if (bucket != null && value is JsonObject fields)
                raw.AddRange(fields.Select(pair => new RawField(pair.Key, pair.Value, bucket)));
            else raw.Add(new RawField(key, value));
        }
    }

    private static Dictionary<ImportField, JsonNode> Bind(List<RawField> raw, ImportDefinition definition, bool explicitKeys = false)
    {
        var values = new Dictionary<ImportField, JsonNode>();
        foreach (var candidate in raw)
        {
            var entry = candidate;
            var dot = entry.Key.IndexOf('.');
            if (!explicitKeys && entry.Bucket == null && dot > 0 && Bucket(entry.Key[..dot]) is { } prefix)
                entry = entry with { Key = entry.Key[(dot + 1)..], Bucket = prefix };
            var matches = explicitKeys ? [] : MatchingFields(definition, entry.Key, entry.Bucket);
            var field = explicitKeys ? definition.Fields.GetValueOrDefault(entry.Bucket == null ? entry.Key : entry.Bucket + "." + entry.Key)
                : matches.Length == 1 ? matches[0] : null;
            if (field == null && entry.Bucket is "customProperties" or "properties" && !FolderImportSchema.Reserved(entry.Key))
            {
                var bucket = definition.Target is "scene" or "research" ? "properties" : "customProperties";
                if (entry.Bucket == bucket) field = new ImportField(entry.Key, bucket, typeof(string));
            }
            if (!explicitKeys && field == null && matches.Length == 0 && entry.Bucket == null && entry.AllowExtra
                && !ProtectedKey(entry.Key) && Text(entry.Value) is { Length: > 0 })
                field = new ImportField(entry.Key, definition.Target is "scene" or "research" ? "properties" : "customProperties", typeof(string));
            if (field == null) continue;
            var value = field is TemplateImportField template ? JsonValue.Create(template.TemplateId) : ConvertValue(field, entry.Value);
            if (value != null && FolderImportSchema.Accepts(FolderImportSchema.FieldSchema(field), value)) values.TryAdd(field, value);
        }
        return values;
    }

    private static ImportField? Resolve(ImportDefinition definition, string key, string? bucket)
    {
        var matches = MatchingFields(definition, key, bucket);
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ImportField[] MatchingFields(ImportDefinition definition, string key, string? bucket)
    {
        var original = Normalize(key);
        var normal = Canonical(key);
        if (bucket == null && normal == "title" && definition.Target is not ("scene" or "research")) normal = "name";
        return definition.Fields.Values.Where(field => (bucket == null || field.Bucket == bucket)
            && (Normalize(field.Key) == original || field.Label != null && Normalize(field.Label) == original
                || field.Bucket is "builtIn" or "analysis" && Normalize(field.Key) == normal)).ToArray();
    }

    private static bool ProtectedKey(string key) => Normalize(key) is "id" or "importedfrom" or "importedmetadata"
        or "images" or "attachments" or "chapteroverrides" or "stateoverrides" or "match" or "arc";

    private sealed record BodyHeading(string Title, int Level, string Content, string? Bucket);
    private static List<BodyHeading> ReadBody(string body, List<RawField> raw, List<RawChapter> chapters, List<EntityImage> images, string target)
    {
        var lines = body.Split('\n');
        var fieldLines = new bool[lines.Length];
        var headings = new List<(string Title, int Level, int Start, string? Bucket)>();
        char fence = '\0';
        string? bucket = null;
        var bucketLevel = 0;
        var section = string.Empty;
        RawChapter? chapter = null;
        var relationships = new JsonArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("```") || line.StartsWith("~~~"))
            {
                if (fence == '\0') fence = line[0];
                else if (line[0] == fence) fence = '\0';
                continue;
            }
            if (fence != '\0') continue;
            fieldLines[index] = true;
            var heading = Heading().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var title = heading.Groups[2].Value;
                section = Normalize(title);
                chapter = null;
                if (level <= bucketLevel) bucket = null;
                if (Bucket(title) is { } next) { bucket = next; bucketLevel = level; }
                headings.Add((title, level, index, bucket));
                continue;
            }
            if (section == "chapterrelevantinformation")
            {
                var scope = ChapterLabel().Match(line);
                if (scope.Success)
                {
                    chapter = new RawChapter(scope.Groups[1].Value.Trim(), []);
                    chapters.Add(chapter);
                }
                else if (chapter != null && Label().Match(line) is { Success: true } scoped && scoped.Groups["value"].Value.Trim().Length > 0)
                    chapter.Fields.Add(new RawField(scoped.Groups["key"].Value.Trim(), Inline(scoped.Groups["value"].Value)));
                continue;
            }
            if (section is "images" or "bilder")
            {
                images.AddRange(ReadImages(line));
                continue;
            }
            if (section is "relationships" or "beziehungen")
            {
                if (Label().Match(line) is { Success: true } relationship)
                    relationships.Add(new JsonObject
                    {
                        ["role"] = relationship.Groups["key"].Value.Trim(),
                        ["target"] = relationship.Groups["value"].Value.Trim().Trim('[', ']')
                    });
                continue;
            }
            if (line.StartsWith('|'))
            {
                var cells = TableCells().Split(line.Trim('|')).Select(cell => cell.Trim().Replace("\\|", "|")).ToArray();
                if (cells.Length == 2 && cells[0].Length > 0 && cells[1].Length > 0 && !TableHeader(cells))
                    raw.Add(new RawField(cells[0].Trim('*', '`'), Inline(cells[1]), bucket));
            }
            else
            {
                var label = Label().Match(line);
                if (label.Success)
                {
                    var key = label.Groups["key"].Value.Trim();
                    var value = label.Groups["value"].Value;
                    if (target == "character" && section == "generalinformation" && Normalize(key) == "relationship") key = "role";
                    var explicitBucket = target == "character" && section == "furtherinformation" && Canonical(key) == "role"
                        && raw.Any(field => field.Bucket == null && Canonical(field.Key) == "role") ? "customProperties" : bucket;
                    raw.Add(new RawField(key, Inline(value), explicitBucket));
                    if (target == "character" && Normalize(key) is "eltern" or "parents" or "mutter" or "mother" or "vater" or "father")
                        foreach (var parent in value.Split(['&', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                            relationships.Add(new JsonObject { ["role"] = key, ["target"] = parent, ["category"] = "family" });
                }
            }
        }
        var result = new List<BodyHeading>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            var end = index + 1 < headings.Count ? headings[index + 1].Start : lines.Length;
            var content = string.Join('\n', lines[(heading.Start + 1)..end]).Trim();
            var fieldContent = string.Join('\n', Enumerable.Range(heading.Start + 1, end - heading.Start - 1)
                .Where(line => fieldLines[line]).Select(line => lines[line])).Trim();
            if (fieldContent.Length > 0 && !MetadataHeading(heading.Title)) raw.Add(new RawField(heading.Title, Inline(fieldContent), heading.Bucket, AllowExtra: false));
            result.Add(new BodyHeading(heading.Title, heading.Level, content, heading.Bucket));
        }
        if (relationships.Count > 0) raw.Add(new RawField("relationships", relationships));
        return result;
    }
}
