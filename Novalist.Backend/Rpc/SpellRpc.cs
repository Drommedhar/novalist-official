using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The writer's own dictionary.
///
/// These live in the settings file rather than in the platform's dictionary so
/// a made-up name learned once travels with the settings instead of having to
/// be learned again on the next machine. The platform checker is told about
/// them at startup and whenever the list changes.
/// </summary>
public sealed class SpellRpc
{
    private readonly Workspace _workspace;

    public SpellRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private List<string> Words => _workspace.Settings.Settings.SpellCheckCustomWords;

    [JsonRpcMethod("spell/words")]
    public async Task<string[]> GetWordsAsync()
    {
        await _workspace.Settings.LoadAsync();
        return [.. Words];
    }

    /// <summary>Adds a word. Returns the full list so the caller can push it
    /// straight at the platform checker without a second read.</summary>
    [JsonRpcMethod("spell/addWord")]
    public async Task<string[]> AddWordAsync(string word)
    {
        var clean = word.Trim();
        if (clean.Length == 0) return [.. Words];
        if (Words.Contains(clean, StringComparer.Ordinal)) return [.. Words];

        Words.Add(clean);
        await _workspace.Settings.SaveAsync();
        return [.. Words];
    }

    /// <summary>
    /// Every name the Codex holds, plus the writer's own words.
    ///
    /// A secondary-world manuscript is a wall of red underlines otherwise: the
    /// Codex knows every name in the book and the checker knew none of them, so
    /// each one had to be taught by hand before the underlines meant anything.
    /// Names are not stored in the settings - they follow the Codex, so a
    /// renamed character stops being spelled the old way.
    /// </summary>
    [JsonRpcMethod("spell/dictionary")]
    public async Task<string[]> GetDictionaryAsync()
    {
        await _workspace.Settings.LoadAsync();
        var names = new List<string>();
        // The dictionary is fetched at startup, before any project is open -
        // asking the Codex then would throw and take spell check with it.
        if (!_workspace.Projects.IsProjectLoaded)
            return [.. Words.Where(w => w.Length > 1).Distinct(StringComparer.Ordinal)];

        var entities = new Core.Services.EntityService(_workspace.Projects);
        // A character's surname is a separate field, and their aliases are
        // names the prose actually uses - both are words a checker will flag.
        foreach (var character in await entities.LoadCharactersAsync())
        {
            names.Add(Core.Services.EntityResolveIndex.Compose(character.Name, character.Surname));
            names.AddRange(character.Aliases);
        }
        names.AddRange((await entities.LoadLocationsAsync()).Select(l => l.Name));
        names.AddRange((await entities.LoadItemsAsync()).Select(i => i.Name));
        names.AddRange((await entities.LoadLoreAsync()).Select(l => l.Name));

        return [.. Words
            .Concat(names.SelectMany(SplitName))
            .Where(w => w.Length > 1)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// A name is checked word by word, so "Mira Vance" has to teach the checker
    /// both halves - and a one-letter fragment teaches it nothing.
    /// </summary>
    private static IEnumerable<string> SplitName(string name)
        => name.Split([' ', '-', '’', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim());

    [JsonRpcMethod("spell/removeWord")]
    public async Task<string[]> RemoveWordAsync(string word)
    {
        if (Words.RemoveAll(w => string.Equals(w, word, StringComparison.Ordinal)) == 0)
            return [.. Words];

        await _workspace.Settings.SaveAsync();
        return [.. Words];
    }
}
