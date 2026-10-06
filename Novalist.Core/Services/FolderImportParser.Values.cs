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

internal sealed partial class FolderImportParser
{
    private static JsonNode? ConvertValue(ImportField field, JsonNode? value)
    {
        if (value == null) return null;
        var type = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        if (type == typeof(List<string>))
        {
            var parts = value is JsonArray array ? array.Select(Text) : (Text(value) ?? string.Empty)
                .Trim('[', ']').Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return new JsonArray(parts.OfType<string>().Where(part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(part => JsonValue.Create(Regex.Replace(part, @"^[-*+]\s+", "").Trim('"', '\''))).ToArray());
        }
        if (type == typeof(List<EntitySection>) || type == typeof(List<EntityRelationship>))
            return value is JsonArray ? NormalizeFlags(value) : null;
        var text = Text(value);
        if (text == null) return null;
        if (type == typeof(bool)) return Boolean(text) is { } boolean ? JsonValue.Create(boolean) : null;
        if (type == typeof(int)) return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? JsonValue.Create(integer) : null;
        if (type.IsEnum)
        {
            var choice = Enum.GetNames(type).FirstOrDefault(choice => choice.Equals(text, StringComparison.OrdinalIgnoreCase));
            return choice == null ? null : JsonValue.Create(choice);
        }
        if (field.Definition is { } definition && FolderImportSchema.BuiltInType(definition) is { } propertyType)
        {
            switch (propertyType)
            {
                case CustomPropertyType.Bool:
                    if (Boolean(text) is not { } boolean) return null;
                    text = boolean ? "true" : "false";
                    break;
                case CustomPropertyType.Int:
                case CustomPropertyType.Timespan:
                    if (!BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return null;
                    text = integer.ToString(CultureInfo.InvariantCulture);
                    break;
                case CustomPropertyType.Enum:
                    text = definition.EnumOptions?.FirstOrDefault(option => option.Equals(text, StringComparison.OrdinalIgnoreCase)) ?? text;
                    break;
            }
        }
        return JsonValue.Create(text);
    }

    private static JsonNode NormalizeFlags(JsonNode value)
    {
        var clone = value.DeepClone();
        foreach (var map in ((JsonArray)clone).OfType<JsonObject>())
            foreach (var key in new[] { "aiHidden", "readerHidden" })
                if (Text(map[key]) is { } text && Boolean(text) is { } flag) map[key] = flag;
        return clone;
    }

    private static bool? Boolean(string text) => Normalize(text) switch
    {
        "true" or "yes" or "1" or "ja" or "是" => true,
        "false" or "no" or "0" or "nein" or "否" => false,
        _ => null
    };
    private static string? Text(JsonNode? value) => value is JsonValue scalar ? scalar.ToString().Trim() : null;
    private static List<string> ReadTags(Dictionary<ImportField, JsonNode> values)
        => values.Where(pair => pair.Key.Key == "tags" && pair.Key.Bucket is "builtIn" or "analysis")
            .SelectMany(pair => pair.Value.AsArray().GetValues<string>()).ToList();
    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Canonical(string key)
    {
        var normal = Normalize(key);
        return Aliases.TryGetValue(normal, out var alias) ? Normalize(alias) : normal;
    }
    private static string? Bucket(string key) => Normalize(key) switch
    {
        "customproperties" or "customprops" or "eigeneeigenschaften" or "自定义属性" => "customProperties",
        "fields" or "felder" or "字段" => "fields",
        "properties" => "properties",
        _ => null
    };

    private static JsonNode Inline(string text)
    {
        if (text.StartsWith('[') || text.StartsWith('"') || text.StartsWith('\''))
        {
            try { return ReadYaml(text) ?? JsonValue.Create(text); }
            catch (Exception exception) when (exception is YamlException or FormatException) { }
        }
        return JsonValue.Create(text);
    }

    private static JsonNode? ReadYaml(string text)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1) throw new FormatException("Expected one YAML document.");
        var budget = 10000;
        return YamlValue(yaml.Documents[0].RootNode, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance), 0, ref budget);
    }

    private static JsonNode? YamlValue(YamlNode node, HashSet<YamlNode> ancestors, int depth, ref int budget)
    {
        if (depth > 32 || --budget < 0 || !ancestors.Add(node)) throw new FormatException("YAML is too deeply nested or cyclic.");
        JsonNode? result;
        switch (node)
        {
            case YamlScalarNode scalar:
                result = scalar.Value == null || scalar.Style == ScalarStyle.Plain && (scalar.Value == "~" || scalar.Value.Equals("null", StringComparison.OrdinalIgnoreCase))
                    ? null : JsonValue.Create(scalar.Value);
                break;
            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var child in sequence.Children) array.Add(YamlValue(child, ancestors, depth + 1, ref budget));
                result = array;
                break;
            default:
                var mapping = (YamlMappingNode)node;
                var map = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: { } name } || map.ContainsKey(name)) throw new FormatException("Invalid YAML mapping key.");
                    map.Add(name, YamlValue(value, ancestors, depth + 1, ref budget));
                }
                result = map;
                break;
        }
        ancestors.Remove(node);
        return result;
    }
}
