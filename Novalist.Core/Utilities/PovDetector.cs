using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Utilities;

/// <summary>
/// Best-effort scene POV detection from plain text + character codex. Mirrors
/// the heuristic used by the context sidebar so the corkboard / outliner show
/// the same value when no manual override is set.
/// </summary>
public static class PovDetector
{
    private static readonly Regex FirstPersonRegex = new(
        @"\b(i|me|my|mine|myself|we|us|our|ours|ourselves)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Detect(string content, IReadOnlyList<CharacterData> characters, string firstPersonLabel = "First person")
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var bestCharacter = (Character: (CharacterData?)null, Count: 0);

        foreach (var character in characters)
        {
            var aliases = GetAliases(character);
            var maxCount = 0;
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;

                var pattern = new Regex(
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var count = pattern.Matches(content).Count;
                if (count > maxCount)
                    maxCount = count;
            }

            if (maxCount > bestCharacter.Count)
                bestCharacter = (character, maxCount);
        }

        if (bestCharacter.Character != null && bestCharacter.Count > 0)
            return bestCharacter.Character.DisplayName;

        if (FirstPersonRegex.Matches(content).Count >= 4)
            return firstPersonLabel;

        return string.Empty;
    }

    private static IEnumerable<string> GetAliases(CharacterData character)
    {
        if (!string.IsNullOrWhiteSpace(character.Name))
            yield return character.Name;
        if (!string.IsNullOrWhiteSpace(character.DisplayName))
            yield return character.DisplayName;
        if (!string.IsNullOrWhiteSpace(character.Surname))
            yield return character.Surname;
    }
}
