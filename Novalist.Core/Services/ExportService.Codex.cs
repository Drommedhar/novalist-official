using System.Text.RegularExpressions;
using System.Text;
using Novalist.Core.Models;
using XBrushes = PdfSharpCore.Drawing.XBrushes;
using XFont = PdfSharpCore.Drawing.XFont;
using XFontStyle = PdfSharpCore.Drawing.XFontStyle;
using XGraphics = PdfSharpCore.Drawing.XGraphics;
using XImage = PdfSharpCore.Drawing.XImage;
using XPoint = PdfSharpCore.Drawing.XPoint;
using XUnit = PdfSharpCore.Drawing.XUnit;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    // ─── Codex (markdown / PDF with images) ──────────────────────────

    /// <summary>Entity kind prefixes used by <see cref="ExportOptions.SelectedEntityKeys"/>.</summary>
    private const string CodexCharacterKind = "character";
    private const string CodexLocationKind = "location";
    private const string CodexItemKind = "item";
    private const string CodexLoreKind = "lore";

    /// <summary>The codex entities selected for export, already ordered by name.</summary>
    private sealed class CodexContent
    {
        public List<CharacterData> Characters { get; init; } = [];
        public List<LocationData> Locations { get; init; } = [];
        public List<ItemData> Items { get; init; } = [];
        public List<LoreData> Lore { get; init; } = [];
    }

    private static async Task<CodexContent> CompileCodexAsync(ExportOptions options, IEntityService entityService)
    {
        var keys = options.SelectedEntityKeys is null
            ? null
            : new HashSet<string>(options.SelectedEntityKeys, StringComparer.OrdinalIgnoreCase);

        bool Included(string kind, string id) => keys is null || keys.Contains($"{kind}:{id}");

        var byName = System.StringComparer.CurrentCultureIgnoreCase;
        var characters = await entityService.LoadCharactersAsync();
        var locations = await entityService.LoadLocationsAsync();
        var items = await entityService.LoadItemsAsync();
        var lore = await entityService.LoadLoreAsync();

        return new CodexContent
        {
            Characters = characters.Where(c => Included(CodexCharacterKind, c.Id))
                .OrderBy(c => c.DisplayName, byName).ToList(),
            Locations = locations.Where(l => Included(CodexLocationKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList(),
            Items = items.Where(i => Included(CodexItemKind, i.Id))
                .OrderBy(i => i.Name, byName).ToList(),
            Lore = lore.Where(l => Included(CodexLoreKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList()
        };
    }

    /// <summary>
    /// Resolves a fixed codex label in the user's language, falling back to
    /// English when the caller supplied no translation for it.
    /// </summary>
    private static string Label(ExportOptions options, string key, string fallback)
        => options.Labels is not null && options.Labels.TryGetValue(key, out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text
            : fallback;

    /// <summary>Field rows rendered for a character, in display order, skipping empty values.</summary>
    private static IEnumerable<KeyValuePair<string, string>> CharacterFields(CharacterData c, ExportOptions options)
    {
        // In date age mode the Age field only restates the birth date, so it is
        // dropped rather than printed as a bare date.
        var age = string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase) ? string.Empty : c.Age;

        var fixedFields = new (string Key, string Fallback, string Value)[]
        {
            ("role", "Role", c.Role),
            ("age", "Age", age),
            ("gender", "Gender", c.Gender),
            ("group", "Group", c.Group),
            ("eyes", "Eyes", c.EyeColor),
            ("hair", "Hair", c.HairColor),
            ("height", "Height", c.Height),
            ("build", "Build", c.Build),
            ("skin", "Skin", c.SkinTone),
            ("notable", "Notable", c.DistinguishingFeatures)
        };

        foreach (var (key, fallback, value) in fixedFields)
            if (!string.IsNullOrWhiteSpace(value))
                yield return new KeyValuePair<string, string>(Label(options, key, fallback), value);

        if (c.CustomProperties is { Count: > 0 })
            foreach (var kv in c.CustomProperties)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    /// <summary>Field rows rendered for a location / item / lore entry.</summary>
    private static IEnumerable<KeyValuePair<string, string>> GenericFields(
        string type, string description, Dictionary<string, string>? customProps, ExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(type))
            yield return new KeyValuePair<string, string>(Label(options, "type", "Type"), type);
        if (!string.IsNullOrWhiteSpace(description))
            yield return new KeyValuePair<string, string>(Label(options, "description", "Description"), description);
        if (customProps is { Count: > 0 })
            foreach (var kv in customProps)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    public async Task ExportCodexAsync(ExportOptions options, string outputPath)
    {
        if (_entityService == null)
        {
            await File.WriteAllTextAsync(outputPath, "Codex export requires entity service.", Encoding.UTF8);
            return;
        }

        var outputDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var imagesFolderName = SanitizeFolderName(baseName) + "_images";
        var imagesAbsDir = Path.Combine(outputDir, imagesFolderName);
        if (outputDir.Length > 0) Directory.CreateDirectory(outputDir);

        var copyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? CopyImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (copyMap.TryGetValue(relativePath, out var existing)) return existing;
            var abs = _entityService.GetImageFullPath(relativePath);
            if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs)) return null;
            var fileName = Path.GetFileName(abs);
            var dest = Path.Combine(imagesAbsDir, fileName);
            int n = 1;
            while (File.Exists(dest) &&
                   !FilesEqual(abs, dest))
            {
                fileName = $"{Path.GetFileNameWithoutExtension(abs)}_{n}{Path.GetExtension(abs)}";
                dest = Path.Combine(imagesAbsDir, fileName);
                n++;
            }
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(imagesAbsDir);
                File.Copy(abs, dest, overwrite: false);
            }
            var rel = imagesFolderName + "/" + fileName;
            copyMap[relativePath] = rel;
            return rel;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {options.Title ?? "Codex"}");
        if (!string.IsNullOrWhiteSpace(options.Author)) sb.AppendLine($"_by {options.Author}_");
        sb.AppendLine();

        var content = await CompileCodexAsync(options, _entityService);

        if (content.Characters.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "characters", "Characters")}");
            foreach (var c in content.Characters)
                AppendCharacter(sb, c, options, CopyImage);
        }

        if (content.Locations.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "locations", "Locations")}");
            foreach (var l in content.Locations)
                AppendGenericEntity(sb, l, l.Name, GenericFields(l.Type, l.Description, l.CustomProperties, options), options, CopyImage);
        }

        if (content.Items.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "items", "Items")}");
            foreach (var it in content.Items)
                AppendGenericEntity(sb, it, it.Name, GenericFields(it.Type, it.Description, it.CustomProperties, options), options, CopyImage);
        }

        if (content.Lore.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "lore", "Lore")}");
            foreach (var lo in content.Lore)
                AppendGenericEntity(sb, lo, lo.Name, GenericFields(lo.Category, lo.Description, lo.CustomProperties, options), options, CopyImage);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);

    }

    // The catch is a TOCTOU safety net: callers verify both files exist before
    // calling, so FileInfo.Length cannot realistically throw — excluded as the
    // catch line is not deterministically reachable.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length;
        }
        catch { return false; }
    }

    private static string SanitizeFolderName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var clean = new string(arr).Trim();
        return string.IsNullOrEmpty(clean) ? "codex" : clean;
    }

    private static void AppendCharacter(StringBuilder sb, CharacterData c, ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {c.DisplayName}");
        if (c.Images is { Count: > 0 } && options.IncludesPart("images"))
        {
            foreach (var img in c.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in CharacterFields(c, options))
                sb.AppendLine($"- **{field.Key}:** {field.Value}");

        if (c.Relationships is { Count: > 0 } && options.IncludesPart("relationships"))
        {
            sb.AppendLine();
            sb.AppendLine($"**{Label(options, "relationships", "Relationships")}**");
            foreach (var r in c.Relationships)
                sb.AppendLine($"- {r.Role}: {r.Target}");
        }

        if (c.Sections is { Count: > 0 })
        {
            foreach (var s in c.Sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }

    private static void AppendGenericEntity(StringBuilder sb, IEntityData entity, string name,
        IEnumerable<KeyValuePair<string, string>> fields,
        ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {name}");
        if (entity.Images is { Count: > 0 } && options.IncludesPart("images"))
            foreach (var img in entity.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in fields)
                sb.AppendLine($"- **{field.Key}:** {field.Value}");
        if (entity.Sections is { Count: > 0 })
        {
            foreach (var s in entity.Sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }
}
