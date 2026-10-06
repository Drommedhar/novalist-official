using System.Text;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class WikiRpc
{
    [JsonRpcMethod("wiki/generatorAvailable")]
    public bool GeneratorAvailable()
        => _workspace.ExtensionHostOrNull?.IsArticleGeneratorAvailable ?? false;

    /// <summary>Generates (via the first enabled extension article generator) and
    /// caches an AI summary for the entity, returning it. Null when no generator
    /// is available; an error field when generation failed.</summary>
    [JsonRpcMethod("wiki/regenerate")]
    public async Task<WikiRegenerateResultDto?> RegenerateAsync(
        string type, string id, CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsArticleGeneratorAvailable)
            return null;

        var (core, _, dossier) = await BuildCoreAndDossierAsync(type, id);
        var result = await host.GenerateArticleAsync(
            new ArticleGenerationRequest
            {
                TypeKey = core.TypeKey,
                EntityId = core.Id,
                EntityName = core.Title,
                Context = dossier
            },
            cancellationToken);
        if (result == null) return null;

        if (!string.IsNullOrEmpty(result.Error))
            return new WikiRegenerateResultDto(null, result.Error, null);

        var generatedAt = DateTime.UtcNow.ToString("o");
        await _cache.WriteAsync(id, new WikiArticleCacheEntry
        {
            Summary = result.Summary,
            GeneratedAt = generatedAt,
            InputHash = WikiArticleCache.ComputeInputHash(dossier)
        });
        Log.Info($"wiki/regenerate type={type} id={id} len={result.Summary.Length}.");
        return new WikiRegenerateResultDto(result.Summary, null, generatedAt);
    }

    /// <summary>
    /// Writes one section of a Codex entry rather than the whole summary.
    ///
    /// The Wiki summary is regenerated whole or not at all, which is the wrong
    /// unit for the way an entry actually gets filled in: the writer is happy
    /// with the history and wants another go at the appearance. Sections have
    /// been ordered, titled blocks in the data model all along - the title is
    /// the writer's own words and the best statement there is of what belongs
    /// in it.
    ///
    /// Returns the prose without writing it. Generated text is wrong a fair
    /// amount of the time, and a section overwritten in place is found out
    /// about later, by which point the thing it replaced is gone.
    /// </summary>
    [JsonRpcMethod("entities/generateSection")]
    public async Task<WikiRegenerateResultDto?> GenerateSectionAsync(
        string type, string id, string sectionTitle, string currentContent,
        CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsArticleGeneratorAvailable) return null;
        if (string.IsNullOrWhiteSpace(sectionTitle)) return null;

        var (core, _, dossier) = await BuildCoreAndDossierAsync(type, id);
        var result = await host.GenerateArticleAsync(
            new ArticleGenerationRequest
            {
                TypeKey = core.TypeKey,
                EntityId = core.Id,
                EntityName = core.Title,
                Context = dossier,
                SectionTitle = sectionTitle,
                SectionContent = currentContent ?? string.Empty,
            },
            cancellationToken);
        if (result == null) return null;

        return string.IsNullOrEmpty(result.Error)
            ? new WikiRegenerateResultDto(result.Summary, null, null)
            : new WikiRegenerateResultDto(null, result.Error, null);
    }

    /// <summary>Loads and builds just what the AI generator needs: the entity
    /// core, its ordered appearances, and the plain-text dossier prompt context.</summary>
    private async Task<(ArticleCore Core, WikiAppearanceDto[] Appearances, string Dossier)>
        BuildCoreAndDossierAsync(string type, string id)
    {
        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var items = await _entities.LoadItemsAsync();
        var lore = await _entities.LoadLoreAsync();

        var customTypes = new List<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)>();
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            customTypes.Add((typeDef.TypeKey, await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey)));

        var resolve = EntityResolveIndex.Build(characters, locations, items, lore, customTypes);
        var appearanceIndex = await new AppearanceIndexService(_workspace.Projects).BuildAsync(characters);
        var core = BuildCore(type, id, new ArticleEntities(characters, locations, items, lore, customTypes), resolve);
        var appearances = SortAppearances(appearanceIndex.TryGetValue(id, out var raw) ? raw : []);
        return (core, appearances, BuildDossier(core, appearances));
    }

    /// <summary>Flattens the deterministic article into a plain-text dossier used
    /// as the AI generator's prompt context. Content-only; never logged.</summary>
    private static string BuildDossier(ArticleCore core, WikiAppearanceDto[] appearances)
    {
        var sb = new StringBuilder();
        // Spell out given vs. family name so the model refers to the subject
        // correctly and never mistakes a shared surname for a separate person.
        if (core.Character != null && core.Character.Surname.Length > 0)
            sb.AppendLine($"Name: {core.Title} (given name: {core.Character.Name}; family name: {core.Character.Surname})");
        else
            sb.AppendLine($"Name: {core.Title}");
        sb.AppendLine($"Type: {core.CustomTypeLabel ?? core.TypeKey}");
        if (core.Aliases.Length > 0)
            sb.AppendLine($"Also known as: {string.Join(", ", core.Aliases)}");
        if (core.Description != null)
            sb.AppendLine($"Description: {core.Description}");

        foreach (var f in core.Infobox.Fields)
        {
            var label = f.LiteralLabel ?? Humanize(f.LabelKey
                ?? throw new InvalidOperationException("An infobox field must have a label."));
            sb.AppendLine($"- {label}: {f.Value}");
        }

        foreach (var s in core.Sections)
        {
            var text = TextDiff.StripHtml(s.Content).Trim();
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(s.Title))
                sb.AppendLine($"## {s.Title}");
            if (text.Length > 0)
                sb.AppendLine(text);
        }

        if (core.Relationships.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Relationships:");
            foreach (var r in core.Relationships)
                sb.AppendLine($"- {r.Role}: {string.Join(", ", r.Targets.Select(t => t.Name))}");
        }

        if (appearances.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Appearances (in story order):");
            foreach (var a in appearances)
            {
                var date = a.StoryDate.Length > 0 ? $"{a.StoryDate} — " : string.Empty;
                var synopsis = a.Synopsis != null ? $": {a.Synopsis}" : string.Empty;
                sb.AppendLine($"- {date}{a.ChapterTitle} / {a.SceneTitle}{synopsis}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Turns an i18n field key ("entityEditor.eyeColor") into a readable
    /// dossier label ("Eye Color"), dropping the UI-only "Placeholder"/"Plain"
    /// suffixes some keys carry (e.g. "rolePlaceholder" -> "Role").</summary>
    private static string Humanize(string labelKey)
    {
        var seg = labelKey.Contains('.') ? labelKey[(labelKey.LastIndexOf('.') + 1)..] : labelKey;
        if (seg.EndsWith("Placeholder", StringComparison.Ordinal))
            seg = seg[..^"Placeholder".Length];
        if (seg.EndsWith("Plain", StringComparison.Ordinal))
            seg = seg[..^"Plain".Length];
        var sb = new StringBuilder();
        foreach (var ch in seg)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
            sb.Append(ch);
        }
        var text = sb.ToString();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
