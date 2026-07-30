using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The book's own completion list.
///
/// The @-mention picker completes Codex names in scene prose and nothing else,
/// which leaves out everything a secondary world is full of and the Codex is
/// not: a settled spelling of a place, a rank, a coined verb, a phrase that has
/// to read the same way every time. Those get retyped slightly differently and
/// the inconsistency surfaces in copy-edit.
/// </summary>
public sealed class CompletionRpc
{
    private readonly Workspace _workspace;

    public CompletionRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("completion/get")]
    public CompletionListDto Get()
    {
        var list = _workspace.Projects.ActiveBook?.Completions ?? new CompletionList();
        return new CompletionListDto([.. list.Words], list.EffectiveTrigger);
    }

    [JsonRpcMethod("completion/save")]
    public async Task<CompletionListDto> SaveAsync(string[]? words, int? trigger = null)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.Completions.Words = CompletionList.Clean(words);
        if (trigger.HasValue) book.Completions.Trigger = trigger.Value;
        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }

    /// <summary>
    /// Adds the Codex's names to the list in one go.
    ///
    /// A writer who wants their cast completed in a research note or the Exposé
    /// - where the @-mention picker does not reach - would otherwise retype
    /// every name into this list by hand, which is exactly the work it exists
    /// to remove.
    /// </summary>
    [JsonRpcMethod("completion/addCodexNames")]
    public async Task<CompletionListDto> AddCodexNamesAsync()
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var entities = new Core.Services.EntityService(_workspace.Projects);
        var names = new List<string>(book.Completions.Words);
        foreach (var character in await entities.LoadCharactersAsync()) names.Add(character.DisplayName);
        foreach (var place in await entities.LoadLocationsAsync()) names.Add(place.DisplayName);
        foreach (var item in await entities.LoadItemsAsync()) names.Add(item.DisplayName);
        foreach (var lore in await entities.LoadLoreAsync()) names.Add(lore.DisplayName);
        foreach (var typeDef in entities.GetCustomEntityTypes())
            foreach (var entity in await entities.LoadCustomEntitiesAsync(typeDef.TypeKey))
                names.Add(entity.DisplayName);

        book.Completions.Words = CompletionList.Clean(names);
        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }
}

/// <summary>The completion list as the renderer and the editor read it.</summary>
public sealed record CompletionListDto(IReadOnlyList<string> Words, int Trigger);
