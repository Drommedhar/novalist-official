using Novalist.Backend.Extensions;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    /// <summary>Whether an extension offers an enabled entity extractor. Drives the
    /// Inspector's "find new entries in this scene" affordance.</summary>
    [JsonRpcMethod("entities/extractorAvailable")]
    public bool ExtractorAvailable()
        => _workspace.ExtensionHostOrNull?.IsEntityExtractorAvailable ?? false;

    /// <summary>
    /// Asks an extension to propose Codex entries for the people, places, and
    /// things a scene mentions that are not in the Codex yet. Returns proposals
    /// only — nothing is written until the caller creates them via
    /// <c>entities/create</c>. Names the project already knows, and proposals with
    /// an unknown type key, are filtered out here rather than trusted.
    /// </summary>
    [JsonRpcMethod("entities/extractFromScene")]
    public async Task<EntityProposalsDto> ExtractFromSceneAsync(
        string chapterGuid, string sceneId, CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsEntityExtractorAvailable)
            return new EntityProposalsDto([], null);

        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var prose = TextDiff.StripHtml(
            await _workspace.Projects.ReadSceneContentAsync(chapter, scene));
        if (string.IsNullOrWhiteSpace(prose))
            return new EntityProposalsDto([], null);

        var known = await BuildKnownNamesAsync();
        var typeKeys = new List<string> { "character", "location", "item", "lore" };
        typeKeys.AddRange(_entities.GetCustomEntityTypes().Select(t => t.TypeKey));

        var result = await host.ExtractEntitiesAsync(
            new EntityExtractionRequest
            {
                Context = prose,
                KnownNames = known.ToArray(),
                AvailableTypeKeys = typeKeys
            },
            cancellationToken);
        if (result == null) return new EntityProposalsDto([], null);

        if (!string.IsNullOrEmpty(result.Error))
            return new EntityProposalsDto([], result.Error);

        var proposals = result.Proposals
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Where(p => typeKeys.Contains(p.TypeKey, StringComparer.OrdinalIgnoreCase))
            .Where(p => !known.Contains(EntityResolveIndex.Normalize(p.Name)))
            .GroupBy(p => EntityResolveIndex.Normalize(p.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(p => new EntityProposalDto(p.TypeKey, p.Name.Trim(), p.Detail))
            .ToArray();

        Log.Info($"entities/extractFromScene returned={result.Proposals.Count} kept={proposals.Length}.");
        return new EntityProposalsDto(proposals, null);
    }

    /// <summary>Every name and alias the Codex already knows, normalized — used to
    /// drop redundant proposals.</summary>
    private async Task<HashSet<string>> BuildKnownNamesAsync()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var normalized = EntityResolveIndex.Normalize(value);
            if (normalized.Length > 0) known.Add(normalized);
        }

        foreach (var c in await _entities.LoadCharactersAsync())
        {
            Add(Compose(c.Name, c.Surname));
            Add(c.Name);
            foreach (var alias in c.Aliases) Add(alias);
        }
        foreach (var l in await _entities.LoadLocationsAsync())
        {
            Add(l.Name);
            foreach (var alias in l.Aliases) Add(alias);
        }
        foreach (var i in await _entities.LoadItemsAsync())
        {
            Add(i.Name);
            foreach (var alias in i.Aliases) Add(alias);
        }
        foreach (var l in await _entities.LoadLoreAsync())
        {
            Add(l.Name);
            foreach (var alias in l.Aliases) Add(alias);
        }
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            foreach (var e in await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey))
            {
                Add(e.Name);
                foreach (var alias in e.Aliases) Add(alias);
            }
        return known;
    }

    [JsonRpcMethod("entities/relationshipSuggestions")]
    public async Task<RelationshipSuggestionsDto> RelationshipSuggestionsAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        // Every entry, not only the characters. A place is owned by somebody, a
        // relic belongs to a house, a law binds a city - and offering only
        // character names to a location's relationship row is why nobody ever
        // filled one in.
        var everyName = characters.Select(c => Compose(c.Name, c.Surname))
            .Concat((await _entities.LoadLocationsAsync()).Select(l => l.Name))
            .Concat((await _entities.LoadItemsAsync()).Select(i => i.Name))
            .Concat((await _entities.LoadLoreAsync()).Select(l => l.Name));

        foreach (var type in _entities.GetCustomEntityTypes())
            everyName = everyName.Concat(
                (await _entities.LoadCustomEntitiesAsync(type.TypeKey)).Select(e => e.Name));

        var names = everyName
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // Roles already used anywhere, so a second location can be filed the
        // same way as the first rather than by remembering the wording.
        var roles = characters.SelectMany(c => c.Relationships.Select(r => r.Role))
            .Concat((await _entities.LoadLocationsAsync()).SelectMany(l => l.Relationships.Select(r => r.Role)))
            .Concat((await _entities.LoadItemsAsync()).SelectMany(i => i.Relationships.Select(r => r.Role)))
            .Concat((await _entities.LoadLoreAsync()).SelectMany(l => l.Relationships.Select(r => r.Role)))
            .Concat(_workspace.Settings.Settings.RelationshipPairs.Keys)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RelationshipSuggestionsDto(names, roles);
    }
}
