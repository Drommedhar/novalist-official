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

    [JsonRpcMethod("spell/removeWord")]
    public async Task<string[]> RemoveWordAsync(string word)
    {
        if (Words.RemoveAll(w => string.Equals(w, word, StringComparison.Ordinal)) == 0)
            return [.. Words];

        await _workspace.Settings.SaveAsync();
        return [.. Words];
    }
}
