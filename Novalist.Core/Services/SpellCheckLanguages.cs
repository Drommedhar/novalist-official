namespace Novalist.Core.Services;

/// <summary>
/// Turns the configured spell-check language list into the one the platform
/// actually loads.
///
/// An empty list means "follow the writing language" rather than "no spell
/// check": a writer who never opened the setting still gets their own language
/// underlined, which is the whole point of the feature being on by default.
/// </summary>
public static class SpellCheckLanguages
{
    public static IReadOnlyList<string> Resolve(
        IEnumerable<string>? configured, string writingLanguage)
    {
        var cleaned = (configured ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > 0) return cleaned;

        var fallback = writingLanguage.Trim();
        return fallback.Length > 0 ? [fallback] : ["en"];
    }
}
