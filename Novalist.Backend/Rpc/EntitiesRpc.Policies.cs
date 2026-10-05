using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    /// <summary>
    /// Writes a character's relationships and, for each row that names an
    /// existing character and carries an inverse role, adds the reciprocal
    /// relationship on that target and learns the role pair (ported from
    /// EntityEditorViewModel.SyncInverseRelationshipsAsync).
    /// </summary>
    /// <summary>
    /// Whether this entry may reach an AI model, and which of its sections are
    /// withheld. Read by the Codex panel; enforced for extensions by
    /// <see cref="Core.Services.AiContextPolicy"/>.
    /// </summary>
    [JsonRpcMethod("entities/getAiPolicy")]
    public async Task<AiPolicyDto> GetAiPolicyAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return new AiPolicyDto(
            (entity?.Ai ?? Core.Models.AiInclusion.WhenMentioned).ToString(),
            [.. SectionsOf(entity).Select((sec, index) =>
                new AiSectionDto(index, sec.Title, sec.AiHidden))]);
    }

    /// <summary>
    /// Sets the entry's inclusion, and which sections are withheld by index.
    /// Indices that no longer name a section are ignored rather than throwing:
    /// the panel's view of the sections can be one edit behind.
    /// </summary>
    [JsonRpcMethod("entities/setAiPolicy")]
    public async Task<AiPolicyDto> SetAiPolicyAsync(string type, string id, string inclusion, int[] hiddenSections)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.Ai = Enum.TryParse<Core.Models.AiInclusion>(inclusion, ignoreCase: true, out var parsed)
            ? parsed
            // An unknown value falls back to the default rather than to Never:
            // silently hiding an entry the writer expects the model to see is
            // the more surprising failure of the two.
            : Core.Models.AiInclusion.WhenMentioned;

        var hidden = (hiddenSections ?? []).ToHashSet();
        var sections = SectionsOf(entity);
        for (var i = 0; i < sections.Count; i++) sections[i].AiHidden = hidden.Contains(i);

        await SaveEntityAsync(entity);
        return await GetAiPolicyAsync(type, id);
    }

    /// <summary>
    /// Who among readers may see this entry, and which of its sections.
    ///
    /// A separate axis from the AI policy on purpose: a writer may be happy for
    /// a model to know the twist while planning and never for a reader to find
    /// it in a world page. One switch for both would force a choice nobody
    /// should have to make.
    /// </summary>
    [JsonRpcMethod("entities/getReaderPolicy")]
    public async Task<ReaderPolicyDto> GetReaderPolicyAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return new ReaderPolicyDto(
            entity?.ReaderHidden ?? false,
            [.. SectionsOf(entity).Select((sec, index) =>
                new ReaderSectionDto(index, sec.Title, sec.ReaderHidden))]);
    }

    /// <summary>
    /// Sets whether the entry, and which of its sections, are kept from
    /// readers. Indices that no longer name a section are ignored: the panel's
    /// view of the sections can be one edit behind.
    /// </summary>
    [JsonRpcMethod("entities/setReaderPolicy")]
    public async Task<ReaderPolicyDto> SetReaderPolicyAsync(
        string type, string id, bool hidden, int[] hiddenSections)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.ReaderHidden = hidden;
        var withheld = (hiddenSections ?? []).ToHashSet();
        var sections = SectionsOf(entity);
        for (var i = 0; i < sections.Count; i++) sections[i].ReaderHidden = withheld.Contains(i);

        await SaveEntityAsync(entity);
        return await GetReaderPolicyAsync(type, id);
    }

    /// <summary>The entry's rich-text sections, or none for a type that has
    /// no section support.</summary>
    private static List<Core.Models.EntitySection> SectionsOf(Core.Models.IEntityData? entity)
        => entity switch
        {
            CharacterData c => c.Sections,
            LocationData l => l.Sections,
            ItemData i => i.Sections,
            LoreData lo => lo.Sections,
            CustomEntityData ce => ce.Sections,
            _ => []
        };

    /// <summary>What this entry is like at particular points in the story.</summary>
    [JsonRpcMethod("entities/getStateOverrides")]
    public async Task<StateOverrideDto[]> GetStateOverridesAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return [.. (entity?.StateOverrides ?? []).Select(ToDto)];
    }

    /// <summary>
    /// Replaces the entry's state overrides. One that restates nothing is
    /// dropped, since an empty override would claim the entry differs at that
    /// point while saying nothing about how.
    /// </summary>
    [JsonRpcMethod("entities/setStateOverrides")]
    public async Task<StateOverrideDto[]> SetStateOverridesAsync(
        string type, string id, StateOverrideDto[] overrides)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.StateOverrides = [.. (overrides ?? [])
            .Select(o => new Core.Models.EntityStateOverride
            {
                Act = NullIfEmpty(o.Act),
                Chapter = o.Chapter ?? string.Empty,
                Scene = NullIfEmpty(o.Scene),
                Name = NullIfEmpty(o.Name),
                Description = NullIfEmpty(o.Description),
                Note = NullIfEmpty(o.Note),
                Gone = o.Gone,
                Fields = o.Fields is { Count: > 0 }
                    ? o.Fields.Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? string.Empty)
                    : null
            })
            .Where(o => o.HasValues)];

        await SaveEntityAsync(entity);
        return await GetStateOverridesAsync(type, id);
    }

    /// <summary>
    /// What the entry is like in the given context. Returns the restated values
    /// and the scope they came from, so a reader can be told they are seeing
    /// the entry at a point in the story rather than in general.
    /// </summary>
    [JsonRpcMethod("entities/resolveState")]
    public async Task<ResolvedStateDto> ResolveStateAsync(
        string type, string id, string? act, string? chapterGuid,
        string? chapterTitle, string? sceneTitle)
    {
        var entity = await FindEntityAsync(type, id);
        var resolved = Core.Services.EntityStateResolver.Resolve(
            entity?.StateOverrides ?? [], act, chapterGuid, chapterTitle, sceneTitle);

        return new ResolvedStateDto(
            resolved.Name, resolved.Description,
            resolved.Fields.ToDictionary(kv => kv.Key, kv => kv.Value),
            resolved.Note, resolved.ScopeLabel, resolved.IsOverridden);
    }

    private static StateOverrideDto ToDto(Core.Models.EntityStateOverride o)
        => new(o.Act, o.Chapter, o.Scene, o.Name, o.Description,
            o.Fields?.ToDictionary(kv => kv.Key, kv => kv.Value), o.Note, o.Gone);

    /// <summary>How an entry's name is recognised in prose.</summary>
    [JsonRpcMethod("entities/getMatchSettings")]
    public async Task<MatchSettingsDto> GetMatchSettingsAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        var match = entity?.Match ?? new Core.Models.EntityMatchSettings();
        return new MatchSettingsDto(
            match.CaseSensitive,
            match.MatchPlurals,
            match.Exclusions.ToArray(),
            match.IgnoredSceneIds.ToArray());
    }

    /// <summary>
    /// Replaces an entry's match settings. Blank exclusions are dropped so a
    /// half-typed row cannot silently suppress every detection.
    /// </summary>
    [JsonRpcMethod("entities/setMatchSettings")]
    public async Task<MatchSettingsDto> SetMatchSettingsAsync(
        string type, string id, bool caseSensitive, bool matchPlurals,
        string[] exclusions, string[] ignoredSceneIds)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.Match = new Core.Models.EntityMatchSettings
        {
            CaseSensitive = caseSensitive,
            MatchPlurals = matchPlurals,
            Exclusions = (exclusions ?? [])
                .Select(e => (e ?? string.Empty).Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IgnoredSceneIds = (ignoredSceneIds ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        await SaveEntityAsync(entity);
        return await GetMatchSettingsAsync(type, id);
    }
}
