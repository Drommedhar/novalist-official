using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

internal record ImportField(string Key, string Bucket, Type ValueType,
    CustomPropertyDefinition? Definition = null, string? Label = null);
internal record ReflectedImportField(string Key, string Bucket, PropertyInfo Property)
    : ImportField(Key, Bucket, Property.PropertyType);
internal sealed record TemplateImportField(PropertyInfo Property, string TemplateId)
    : ReflectedImportField("templateId", "builtIn", Property);
internal sealed record ImportTemplate(string Id, string Name, List<CustomPropertyDefinition> Properties,
    string? AgeMode = null, IntervalUnit? AgeIntervalUnit = null);
internal sealed record ImportDefinition(string Target, Dictionary<string, ImportField> Fields, ImportTemplate? Template);

/// <summary>Shares the active target and field definitions between schema export and parsing; includes the book's templates and custom entity types.</summary>
public sealed class FolderImportSchema
{
    private static readonly Dictionary<string, (Type Model, string Keys)> Models = new()
    {
        ["character"] = (typeof(CharacterData), "name surname aliases gender age birthDate ageMode ageIntervalUnit role group eyeColor hairColor hairLength height build skinTone distinguishingFeatures ai readerHidden tags sections relationships"),
        ["location"] = (typeof(LocationData), "name aliases type parent isWorld description group ai readerHidden tags sections relationships"),
        ["item"] = (typeof(ItemData), "name aliases type description origin group ai readerHidden tags sections relationships"),
        ["lore"] = (typeof(LoreData), "name aliases category description group ai readerHidden tags sections relationships"),
        ["scene"] = (typeof(SceneData), "title date isFavorite notes synopsis goal outcome inactive wordTarget beat stage label excludeFromExport narrativeMode strand"),
        ["research"] = (typeof(ResearchItem), "title status rating tags"),
        ["custom"] = (typeof(CustomEntityData), "name aliases group ai readerHidden tags sections relationships")
    };
    private readonly BookData _book;
    private readonly Dictionary<string, CustomEntityTypeDefinition> _types;
    private readonly Dictionary<string, ImportDefinition> _active = [];
    public IReadOnlyList<string> Targets { get; }
    internal IReadOnlyList<ChapterData> Chapters => _book.Chapters;

    public FolderImportSchema(BookData? book = null, IEnumerable<CustomEntityTypeDefinition>? types = null)
    {
        _book = book ?? new BookData();
        _types = (types ?? []).ToDictionary(type => type.TypeKey, StringComparer.Ordinal);
        Targets = ["character", "location", "item", "lore", "scene", "research", .. _types.Keys];
        foreach (var target in Targets) _active[target] = BuildDefinition(target, null);
    }

    public string Export()
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "Novalist structured import, version 1",
            ["description"] = "Give this schema to an external AI agent together with your source material. Produce one JSON document per entry. Keep names, facts and prose faithful to the source; omit unknown values. Use the examples and exact property keys. Put extra scalar facts in customProperties (entities) or properties (scenes/research), and prose in content or sections. Do not invent IDs or templates. Save one UTF-8 .json file per entry, grouped in folders by target, and choose File > Import a folder in Novalist. Alternatively send each document to the local import API inside a record with a stable sourceId; GET /v1 describes the batch envelope and endpoints. To include Codex images, upload their raw bytes to POST /v1/images first and put the returned imageId, a source name and optional alt text in data.images. Image references must already exist in the destination book. No Novalist AI connection is needed.",
            ["$comment"] = "One object per document, never a dataset array. content is Markdown. Examples illustrate the format only; never copy their values as facts. For folder imports the selected folder target must equal target. API imports take the target from each document. Novalist generates IDs, storage paths and order. This export contains field definitions and active template IDs, never existing entries or their values. Fetch or export again after changing the active template or custom type definitions.",
            ["oneOf"] = new JsonArray(Targets.Select(target => (JsonNode)DocumentSchema(_active[target])).ToArray())
        };
        return schema.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true });
    }

    /// <summary>Exports a standalone JSON Schema document for one target in Targets; throws ArgumentException for an unknown target.</summary>
    public string ExportTarget(string target)
    {
        if (!_active.TryGetValue(target, out var definition)) throw new ArgumentException("Unknown import target.", nameof(target));
        var schema = DocumentSchema(definition);
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return schema.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true });
    }

    internal ImportDefinition Definition(string target, string? template = null)
        => template == null ? _active[target] : BuildDefinition(target, template);

    private ImportDefinition BuildDefinition(string target, string? hint)
    {
        var model = Models.GetValueOrDefault(target, Models["custom"]);
        var properties = JsonProperties(model.Model);
        var fields = model.Keys.Split(' ').ToDictionary(key => key,
            key => (ImportField)new ReflectedImportField(key, "builtIn", properties[key]));
        if (target is not ("scene" or "research"))
            fields["images"] = new ImportField("images", "builtIn", typeof(List<ImportImageReference>));
        if (target == "scene")
            foreach (var (key, property) in JsonProperties(typeof(SceneAnalysisOverrides)).Where(pair => pair.Value.CanWrite))
            {
                fields[key] = new ReflectedImportField(key, "analysis", property);
            }
        if (target is "scene" or "research")
        {
            var scope = target == "scene" ? ManuscriptPropertyScope.Scene : ManuscriptPropertyScope.Research;
            foreach (var property in _book.ManuscriptProperties.Where(property => property.Scope == scope && !Reserved(property.Key)))
                fields["properties." + property.Key] = new ImportField(property.Key, "properties", typeof(string),
                    Definition: new CustomPropertyDefinition { Key = property.Key, Type = property.Type, EnumOptions = property.EnumOptions }, Label: property.Label);
        }
        if (_types.TryGetValue(target, out var type))
            foreach (var field in type.DefaultFields)
                fields["fields." + field.Key] = new ImportField(field.Key, "fields", typeof(string), Definition:
                    new CustomPropertyDefinition { Key = field.Key, Type = field.Type, TypeKey = field.TypeKey, EnumOptions = field.EnumOptions, Prompt = field.Prompt }, Label: field.DisplayName);
        var (templates, activeId) = Templates(target);
        var matching = templates.Where(template => template.Id == hint || template.Name.Equals(hint, StringComparison.OrdinalIgnoreCase)).ToArray();
        var template = hint == null ? templates.FirstOrDefault(template => template.Id == activeId) ?? templates.FirstOrDefault()
            : matching.Length == 1 ? matching[0] : null;
        if (template != null)
        {
            fields["templateId"] = new TemplateImportField(properties["templateId"], template.Id);
            foreach (var property in template.Properties.Where(property => !Reserved(property.Key)))
                fields["customProperties." + property.Key] = new ImportField(property.Key, "customProperties", typeof(string), Definition: property);
        }
        return new ImportDefinition(target, fields, template);
    }

    private static Dictionary<string, PropertyInfo> JsonProperties(Type model)
        => model.GetProperties()
            .SelectMany(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } attribute
                ? new[] { (attribute.Name, Property: property) } : [])
            .ToDictionary(entry => entry.Name, entry => entry.Property);

    private (List<ImportTemplate>, string) Templates(string target) => target switch
    {
        "character" => (_book.CharacterTemplates.Select(template => new ImportTemplate(template.Id, template.Name, template.CustomPropertyDefs, template.AgeMode, template.AgeIntervalUnit)).ToList(), _book.ActiveCharacterTemplateId),
        "location" => (_book.LocationTemplates.Select(template => new ImportTemplate(template.Id, template.Name, template.CustomPropertyDefs)).ToList(), _book.ActiveLocationTemplateId),
        "item" => (_book.ItemTemplates.Select(template => new ImportTemplate(template.Id, template.Name, template.CustomPropertyDefs)).ToList(), _book.ActiveItemTemplateId),
        "lore" => (_book.LoreTemplates.Select(template => new ImportTemplate(template.Id, template.Name, template.CustomPropertyDefs)).ToList(), _book.ActiveLoreTemplateId),
        _ => (_book.CustomEntityTemplates.Where(template => template.EntityTypeKey == target)
            .Select(template => new ImportTemplate(template.Id, template.Name, template.CustomPropertyDefs)).ToList(), _book.ActiveCustomEntityTemplateIds.GetValueOrDefault(target, string.Empty))
    };

    internal JsonObject DocumentSchema(ImportDefinition definition)
    {
        var data = new JsonObject();
        foreach (var field in definition.Fields.Values.Where(field => field.Bucket is "builtIn" or "analysis"))
            data[field.Key] = FieldSchema(field);
        var bucket = definition.Target is "scene" or "research" ? "properties" : "customProperties";
        var extras = new JsonObject();
        foreach (var field in definition.Fields.Values.Where(field => field.Bucket == bucket)) extras[field.Key] = FieldSchema(field);
        var extraSchema = ObjectSchema(extras, ScalarSchema());
        extraSchema["propertyNames"] = new JsonObject { ["not"] = new JsonObject { ["enum"] = new JsonArray("importedFrom", "importedMetadata") } };
        data[bucket] = extraSchema;
        if (_types.ContainsKey(definition.Target))
        {
            var custom = new JsonObject();
            foreach (var field in definition.Fields.Values.Where(field => field.Bucket == "fields")) custom[field.Key] = FieldSchema(field);
            var customSchema = ObjectSchema(custom);
            var required = _types[definition.Target].DefaultFields.Where(field => field.Required).Select(field => JsonValue.Create(field.Key)).ToArray();
            if (required.Length > 0) customSchema["required"] = new JsonArray(required);
            data["fields"] = customSchema;
        }
        if (definition.Template != null)
            data["templateId"] = new JsonObject
            {
                ["type"] = "string",
                ["const"] = definition.Template.Id,
                ["default"] = definition.Template.Id,
                ["description"] = $"Active template: {definition.Template.Name}. Omit to use it automatically."
            };
        var titleKey = definition.Target is "scene" or "research" ? "title" : "name";
        data[titleKey]!["minLength"] = 1;
        data[titleKey]!["pattern"] = @"\S";
        var dataSchema = ObjectSchema(data);
        var requiredData = new JsonArray(titleKey);
        dataSchema["required"] = requiredData;
        if (data["fields"]?["required"] != null) requiredData.Add("fields");
        var document = ObjectSchema(new JsonObject
        {
            ["novalistImport"] = new JsonObject { ["type"] = "integer", ["const"] = 1 },
            ["target"] = new JsonObject { ["type"] = "string", ["const"] = definition.Target },
            ["data"] = dataSchema,
            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Markdown: scene manuscript, research note, or additional entity writing." }
        });
        document["required"] = definition.Target is "scene" or "research"
            ? new JsonArray("novalistImport", "target", "data", "content") : new JsonArray("novalistImport", "target", "data");
        document["title"] = _types.GetValueOrDefault(definition.Target)?.DisplayName ?? definition.Target;
        var exampleData = new JsonObject { [titleKey] = "Example " + definition.Target };
        if (definition.Target == "character") { exampleData["surname"] = "Example surname"; exampleData["age"] = "32"; exampleData["eyeColor"] = "brown"; }
        if (definition.Template != null) exampleData["templateId"] = definition.Template.Id;
        foreach (var group in definition.Fields.Values.Where(field => field.Definition != null).GroupBy(field => field.Bucket))
        {
            var sample = new JsonObject();
            foreach (var field in group) sample[field.Key] = SampleValue(field);
            exampleData[group.Key] = sample;
        }
        document["examples"] = new JsonArray(new JsonObject
        {
            ["novalistImport"] = 1,
            ["target"] = definition.Target,
            ["data"] = exampleData,
            ["content"] = "Preserved writing in **Markdown**."
        });
        return document;
    }

    private static string SampleValue(ImportField field)
    {
        var shape = FieldSchema(field);
        return shape["enum"] is JsonArray choices ? choices.GetValues<string>().First()
            : shape["format"] != null ? "2000-01-01" : shape["pattern"] != null ? "0" : "Example value";
    }

    internal static JsonObject ObjectSchema(JsonObject properties, JsonNode? additional = null)
        => new() { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = additional ?? JsonValue.Create(false) };
    private static JsonObject ScalarSchema() => new() { ["type"] = "string" };

    internal static JsonObject FieldSchema(ImportField field)
    {
        var valueType = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        JsonObject result;
        if (field.Definition != null)
        {
            result = ScalarSchema();
            var definition = field.Definition;
            if (BuiltInType(definition) is { } propertyType)
            {
                if (propertyType is CustomPropertyType.Int or CustomPropertyType.Timespan) result["pattern"] = @"^-?(0|[1-9][0-9]*)$";
                if (propertyType == CustomPropertyType.Bool) result["enum"] = new JsonArray("true", "false");
                if (propertyType == CustomPropertyType.Date) result["format"] = "date";
                if (propertyType == CustomPropertyType.Enum && definition.EnumOptions is { Count: > 0 })
                    result["enum"] = new JsonArray(definition.EnumOptions.Select(option => JsonValue.Create(option)).ToArray());
            }
            result["description"] = $"{field.Label ?? field.Key}: {definition.TypeKey ?? definition.Type.ToString()}. Store the value as text. {definition.Prompt}".Trim();
        }
        else if (valueType == typeof(List<EntitySection>))
            result = new JsonObject
            {
                ["type"] = "array",
                ["items"] = ObjectSchema(new JsonObject
                {
                    ["title"] = ScalarSchema(),
                    ["content"] = ScalarSchema(),
                    ["aiHidden"] = new JsonObject { ["type"] = "boolean" },
                    ["readerHidden"] = new JsonObject { ["type"] = "boolean" }
                })
            };
        else if (valueType == typeof(List<EntityRelationship>))
            result = new JsonObject
            {
                ["type"] = "array",
                ["items"] = ObjectSchema(new JsonObject
                {
                    ["role"] = ScalarSchema(),
                    ["target"] = new JsonObject { ["type"] = "string", ["description"] = "Entity name, not an ID." },
                    ["category"] = ScalarSchema()
                })
            };
        else if (valueType == typeof(List<ImportImageReference>))
            result = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = ImportImageStore.MaxImagesPerEntry,
                ["description"] = "Upload image bytes to POST /v1/images first. Use its returned imageId; Novalist keeps a portable copy in the book. Omit images for text-only imports.",
                ["items"] = ImageReferenceSchema()
            };
        else if (valueType == typeof(List<string>)) result = new JsonObject { ["type"] = "array", ["items"] = ScalarSchema() };
        else if (valueType.IsEnum) result = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames(valueType).Select(name => JsonValue.Create(name)).ToArray()) };
        else result = new JsonObject { ["type"] = valueType == typeof(bool) ? "boolean" : valueType == typeof(int) ? "integer" : "string" };
        if (field.Bucket is "builtIn" or "analysis")
        {
            if (field.Key is "rating" or "intensity") { result["minimum"] = field.Key == "rating" ? 0 : -10; result["maximum"] = field.Key == "rating" ? 5 : 10; }
            if (field.Key == "wordTarget") result["minimum"] = 0;
            if (field.Key == "ageMode") result["enum"] = new JsonArray("number", "date");
            if (field.Key == "sections") result["items"]!["required"] = new JsonArray("title", "content");
            if (field.Key == "relationships") result["items"]!["required"] = new JsonArray("role", "target");
        }
        return result;
    }

    internal static JsonObject ImageReferenceSchema()
    {
        var image = ObjectSchema(new JsonObject
        {
            ["imageId"] = new JsonObject { ["type"] = "string", ["maxLength"] = 64, ["pattern"] = "^[0-9a-f]{64}$", ["description"] = "The imageId returned by POST /v1/images, not a filename or filesystem path." },
            ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["pattern"] = @"\S", ["description"] = "Image label from the source, or its filename." },
            ["alt"] = new JsonObject { ["type"] = "string", ["description"] = "Optional alternative text from the source. Omit if unknown." }
        });
        image["required"] = new JsonArray("imageId", "name");
        return image;
    }

    internal static CustomPropertyType? BuiltInType(CustomPropertyDefinition definition)
        => string.IsNullOrEmpty(definition.TypeKey) ? definition.Type
            : Enum.TryParse<CustomPropertyType>(definition.TypeKey, true, out var type) && Enum.IsDefined(type) ? type : null;

    internal static bool Reserved(string key) => key is "importedFrom" or "importedMetadata";

    // Only the keywords generated above are needed. Keeping validation here
    // means the accepted JSON and the exported contract cannot drift apart.
    internal static bool Accepts(JsonObject schema, JsonNode? value)
    {
        if (value == null) return false;
        if (schema["const"] is { } constant && !JsonNode.DeepEquals(constant, value)) return false;
        if (schema["enum"] is JsonArray options && !options.Any(option => JsonNode.DeepEquals(option, value))) return false;
        switch (schema["type"]?.GetValue<string>())
        {
            case "object":
                if (value is not JsonObject map || schema["properties"] is not JsonObject properties) return false;
                if (schema["required"] is JsonArray required && required.GetValues<string>().Any(key => !map.ContainsKey(key))) return false;
                foreach (var (key, child) in map)
                {
                    if (schema["propertyNames"] != null && Reserved(key)) return false;
                    if (properties[key] is JsonObject field) { if (!Accepts(field, child)) return false; }
                    else if (schema["additionalProperties"] is JsonObject extra) { if (!Accepts(extra, child)) return false; }
                    else return false;
                }
                break;
            case "array":
                if (value is not JsonArray array || schema["items"] is not JsonObject items || array.Any(child => !Accepts(items, child))) return false;
                if (schema["maxItems"] is { } maxItems && array.Count > maxItems.GetValue<int>()) return false;
                break;
            case "string":
                if (value is not JsonValue text || !text.TryGetValue<string>(out var content)) return false;
                if (schema["minLength"] is { } minimum && content.Length < minimum.GetValue<int>()) return false;
                if (schema["maxLength"] is { } maxLength && content.Length > maxLength.GetValue<int>()) return false;
                if (schema["pattern"] is { } pattern && !Regex.IsMatch(content, pattern.GetValue<string>(), RegexOptions.None, TimeSpan.FromSeconds(1))) return false;
                if (schema["format"]?.GetValue<string>() == "date" && !DateOnly.TryParseExact(content, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
                break;
            case "boolean":
                if (value is not JsonValue boolean || !boolean.TryGetValue<bool>(out _)) return false;
                break;
            case "integer":
                if (value is not JsonValue number || !number.TryGetValue<int>(out var integer)) return false;
                if (schema["minimum"] is { } lower && integer < lower.GetValue<int>()) return false;
                if (schema["maximum"] is { } upper && integer > upper.GetValue<int>()) return false;
                break;
        }
        return true;
    }
}
