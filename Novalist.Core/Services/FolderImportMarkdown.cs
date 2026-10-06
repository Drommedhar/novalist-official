using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

internal sealed partial class FolderImportParser
{
    private sealed record RawChapter(string Title, List<RawField> Fields);

    [GeneratedRegex(@"^[-*+]\s+\*\*(.+?)\*\*\s*(?:\((?:Order|Reihenfolge):\s*\d+\))?:\s*$")]
    private static partial Regex ChapterLabel();
    [GeneratedRegex(@"!\[\[(?<wiki>[^\]\r\n]+)\]\]|!\[(?<alt>[^\]\r\n]*)\]\((?<path><[^>\r\n]+>|[^)\r\n]+)\)")]
    private static partial Regex ImageLink();
    [GeneratedRegex(@"^\d+\s*[-–—]\s*")]
    private static partial Regex ChapterNumber();
    [GeneratedRegex(@"^(?:(?<length>kurz|lang|mittellang|schulterlang|short|long|medium|shoulder-length)(?:e[rmns]?)?\s+)?(?:(?<color>dunkelbraun|hellbraun|braun|schwarz|blond|weiß|weiss|grau|rot|dark brown|light brown|brown|black|blonde?|white|grey|gray|red)(?:e[rmns]?)?\s+)?(?:haar|haare|hair)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HairPhrase();
    [GeneratedRegex(@"^(?<color>blau|braun|grün|grau|schwarz|blue|brown|green|grey|gray|black|hazel)(?:e[rmns]?)?\s+(?:augen|eyes)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EyePhrase();
    [GeneratedRegex(@"^(?:(?:etwas|leicht|slightly)\s+)?(?:übergewichtig|schlank|kräftig|athletisch|muskulös|overweight|slim|athletic|muscular)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuildPhrase();

    private static bool MetadataHeading(string title) => Normalize(title) is
        "generalinformation" or "furtherinformation" or "chapterrelevantinformation" or "images" or "bilder" or "relationships" or "beziehungen";

    private static bool TableHeader(string[] cells) =>
        cells.All(cell => cell.All(character => character is '-' or ':' or ' '))
        || Normalize(cells[0]) is "field" or "property" or "key" or "feld" or "eigenschaft" or "字段"
            && Normalize(cells[1]) is "value" or "wert" or "值";

    private static List<EntityImage> ReadImages(string line)
    {
        var images = new List<EntityImage>();
        foreach (Match match in ImageLink().Matches(line))
        {
            var path = match.Groups["wiki"].Success ? match.Groups["wiki"].Value.Split('|')[0] : match.Groups["path"].Value.Trim('<', '>');
            var label = Label().Match(line);
            var name = label.Success ? label.Groups["key"].Value.Trim() : line[..match.Index].Trim().TrimStart('-', '*', '+').Trim().TrimEnd(':').Trim();
            var alt = match.Groups["alt"].Value;
            images.Add(new EntityImage { Name = name.Length > 0 ? name : alt.Length > 0 ? alt : Path.GetFileNameWithoutExtension(path), Path = path, Alt = alt });
        }
        return images;
    }

    private List<CharacterOverride> ReadChapterOverrides(List<RawChapter> chapters, ImportDefinition definition)
    {
        var overrides = new List<CharacterOverride>();
        foreach (var chapter in chapters)
        {
            var raw = chapter.Fields.Select(field => Canonical(field.Key) == "relationship" ? field with { Key = "role" } : field).ToList();
            var values = Bind(raw, definition);
            ReadAppearance(raw, definition, values);
            var matches = schema.Chapters.Where(candidate => candidate.Guid == chapter.Title || candidate.Title.Equals(chapter.Title, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
                matches = schema.Chapters.Where(candidate => ChapterNumber().Replace(candidate.FolderName, string.Empty).Equals(chapter.Title, StringComparison.OrdinalIgnoreCase)).ToArray();
            // Order in an old source may no longer match the current manuscript.
            // Unmatched or ambiguous chapter names remain names, never guessed IDs.
            var result = new CharacterOverride { Chapter = matches.Length == 1 ? matches[0].Guid : chapter.Title };
            var applied = false;
            foreach (var (field, value) in values)
            {
                if (field is ReflectedImportField reflected && field.Bucket == "builtIn" && field.ValueType == typeof(string)
                    && typeof(CharacterOverride).GetProperty(reflected.Property.Name) is { } property)
                {
                    property.SetValue(result, value.GetValue<string>());
                    applied = true;
                }
                else if (field.Bucket == "customProperties")
                {
                    (result.CustomProperties ??= [])[field.Key] = value.GetValue<string>();
                    applied = true;
                }
            }
            if (applied) overrides.Add(result);
        }
        return overrides;
    }

    private static void ReadAppearance(List<RawField> raw, ImportDefinition definition, Dictionary<ImportField, JsonNode> values)
    {
        var detected = new Dictionary<string, HashSet<string>>();
        void Add(string key, string value)
        {
            if (value.Length == 0) return;
            if (!detected.TryGetValue(key, out var choices)) detected[key] = choices = new(StringComparer.OrdinalIgnoreCase);
            choices.Add(value);
        }
        foreach (var field in raw.Where(field => field.Bucket == null && Normalize(field.Key) is "appearance" or "erscheinung" or "aussehen"))
        {
            if (Text(field.Value) is not { } text) continue;
            foreach (var clause in text.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var hair = HairPhrase().Match(clause);
                if (hair.Success) { Add("hairLength", hair.Groups["length"].Value); Add("hairColor", hair.Groups["color"].Value); }
                var eyes = EyePhrase().Match(clause);
                if (eyes.Success) Add("eyeColor", eyes.Groups["color"].Value);
                if (BuildPhrase().IsMatch(clause)) Add("build", clause);
            }
        }
        foreach (var (key, choices) in detected)
            if (choices.Count == 1) values.TryAdd(definition.Fields[key], JsonValue.Create(choices.Single()));
    }
}
