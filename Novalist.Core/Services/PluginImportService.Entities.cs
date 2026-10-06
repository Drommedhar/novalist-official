using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class PluginImportService
{
    // ── Character Sheet ─────────────────────────────────────────────

    private static CharacterData ParseCharacterSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new CharacterData();

        // Extract name from H1
        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
        {
            var fullName = titleMatch.Groups[1].Value.Trim();
            var parts = fullName.Split(' ', 2);
            data.Name = parts[0];
            data.Surname = parts.Length > 1 ? parts[1] : string.Empty;
        }

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Surname = NonEmpty(ParseSheetField(sheet, "Surname"), data.Surname);
        data.Gender = ParseSheetField(sheet, "Gender");
        data.Age = ParseSheetField(sheet, "Age");
        data.Role = ParseSheetField(sheet, "Role");
        data.Group = ParseSheetField(sheet, "Group");
        data.EyeColor = ParseSheetField(sheet, "EyeColor");
        data.HairColor = ParseSheetField(sheet, "HairColor");
        data.HairLength = ParseSheetField(sheet, "HairLength");
        data.Height = ParseSheetField(sheet, "Height");
        data.Build = ParseSheetField(sheet, "Build");
        data.SkinTone = ParseSheetField(sheet, "SkinTone");
        data.DistinguishingFeatures = ParseSheetField(sheet, "DistinguishingFeatures");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var charNextSections = new[] { "Images:", "CustomProperties:", "Sections:", "ChapterOverrides:" };

        // Relationships
        var rels = ParseListSection(sheet, "Relationships", charNextSections);
        data.Relationships = rels.Select(r => new EntityRelationship
        {
            Role = r.key,
            Target = StripWikilink(r.value)
        }).ToList();

        // Images
        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:", "ChapterOverrides:" });

        // Custom properties
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:", "ChapterOverrides:" });

        // Sections
        data.Sections = ParseSections(sheet, new[] { "ChapterOverrides:" });

        ParseCharacterOverrides(sheet, data);

        return data;
    }

    // ── Location Sheet ──────────────────────────────────────────────

    private static LocationData ParseLocationSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new LocationData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Type = ParseSheetField(sheet, "Type");
        data.Parent = StripWikilink(ParseSheetField(sheet, "Parent")); // Will be resolved to ID later
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        // Description (multi-line)
        var descNextSections = new[] { "Type:", "Images:", "Relationships:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Item Sheet ──────────────────────────────────────────────────

    private static ItemData ParseItemSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new ItemData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Type = ParseSheetField(sheet, "Type");
        data.Origin = ParseSheetField(sheet, "Origin");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var descNextSections = new[] { "Origin:", "Type:", "Images:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Lore Sheet ──────────────────────────────────────────────────

    private static LoreData ParseLoreSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new LoreData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Category = NonEmpty(ParseSheetField(sheet, "Category"), "Other");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var descNextSections = new[] { "Category:", "Images:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Wikilink Resolution ─────────────────────────────────────────

    private static Dictionary<string, string> BuildEntityNameLookup(
        List<CharacterData> characters,
        List<LocationData> locations,
        List<ItemData> items,
        List<LoreData> lore)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in characters)
        {
            var fullName = string.IsNullOrEmpty(c.Surname) ? c.Name : $"{c.Name} {c.Surname}";
            lookup.TryAdd(fullName, c.Id);
            if (!string.IsNullOrEmpty(c.Name))
                lookup.TryAdd(c.Name, c.Id);
        }

        foreach (var l in locations)
            if (!string.IsNullOrEmpty(l.Name))
                lookup.TryAdd(l.Name, l.Id);

        foreach (var i in items)
            if (!string.IsNullOrEmpty(i.Name))
                lookup.TryAdd(i.Name, i.Id);

        foreach (var l in lore)
            if (!string.IsNullOrEmpty(l.Name))
                lookup.TryAdd(l.Name, l.Id);

        return lookup;
    }

    internal static void ResolveWikilinkReferences(
        List<CharacterData> characters,
        List<LocationData> locations,
        Dictionary<string, string> entityNameToId)
    {
        // Relationship targets stay as display names (standalone uses names, not IDs).
        // Only strip leftover wikilink brackets from chapter override relationships.
        foreach (var c in characters)
        {
            foreach (var co in c.ChapterOverrides)
            {
                if (co.Relationships != null)
                {
                    foreach (var rel in co.Relationships)
                    {
                        rel.Target = StripWikilink(rel.Target);
                    }
                }
            }
        }

        // Resolve location parent references (standalone uses entity IDs for parent)
        foreach (var l in locations)
        {
            if (!string.IsNullOrEmpty(l.Parent))
            {
                var name = StripWikilink(l.Parent);
                if (entityNameToId.TryGetValue(name, out var id))
                    l.Parent = id;
            }
        }
    }

    private static void ParseCharacterOverrides(string sheet, CharacterData data)
    {
        // Chapter overrides
        var overridesMatch = Regex.Match(sheet, @"^\s*ChapterOverrides:\s*([\s\S]*)$", RegexOptions.Multiline);
        if (overridesMatch.Success)
        {
            var overridesText = overridesMatch.Groups[1].Value;
            var chapterBlocks = Regex.Split(overridesText, @"^\s*Chapter:[ \t]*", RegexOptions.Multiline)
                .Where(b => !string.IsNullOrWhiteSpace(b)).ToArray();

            foreach (var block in chapterBlocks)
            {
                var lines = block.Split('\n');
                var co = new CharacterOverride { Chapter = lines[0].Trim() };

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var match = Regex.Match(line, @"^[-*]\s*(.+?)\s*:\s*(.*)$");
                    if (!match.Success) continue;
                    var key = match.Groups[1].Value.Trim().ToLowerInvariant();
                    var val = match.Groups[2].Value.Trim();

                    switch (key)
                    {
                        case "act": co.Act = val; break;
                        case "scene": co.Scene = val; break;
                        case "name": co.Name = val; break;
                        case "surname": co.Surname = val; break;
                        case "gender": co.Gender = val; break;
                        case "age": co.Age = val; break;
                        case "role": co.Role = val; break;
                        case "eyecolor": co.EyeColor = val; break;
                        case "haircolor": co.HairColor = val; break;
                        case "hairlength": co.HairLength = val; break;
                        case "height": co.Height = val; break;
                        case "build": co.Build = val; break;
                        case "skintone": co.SkinTone = val; break;
                        case "distinguishingfeatures": co.DistinguishingFeatures = val; break;
                    }
                }

                data.ChapterOverrides.Add(co);
            }
        }

    }
}
