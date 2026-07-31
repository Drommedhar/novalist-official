using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Invented names, offline. Naming is the highest-frequency thing that stops a
/// draft, and Novalist had no help for it beyond aliases.
/// </summary>
public sealed class NamesRpc
{
    /// <summary>The name sets that ship, for the picker to list.</summary>
    [JsonRpcMethod("names/sets")]
    public string[] Sets() => [.. NameGenerator.Sets.Select(s => s.Key)];

    /// <summary>
    /// Names from one set. The same seed gives the same list back, so a name
    /// the writer liked and did not write down can be asked for again.
    /// </summary>
    [JsonRpcMethod("names/generate")]
    public string[] Generate(
        string setKey, int count = 12, int obscurity = 50, int seed = 1, string? startsWith = null)
        => [.. NameGenerator.Generate(setKey, count, obscurity, seed, startsWith)];
}
