using System.Globalization;
using System.Text.RegularExpressions;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class DashboardRpc
{
    // Ported verbatim from the Avalonia DashboardViewModel so results match.
    internal static List<(string Phrase, int Count)> FindEchoPhrases(string text, int minWords, int threshold)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var clean = Regex.Replace(text, "<[^>]+>", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim().ToLowerInvariant();
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < minWords) return [];

        var phraseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var n = minWords; n <= Math.Min(minWords + 1, 4); n++)
        {
            for (var i = 0; i <= words.Length - n; i++)
            {
                var phrase = string.Join(' ', words.Skip(i).Take(n));
                if (IsStopPhrase(phrase)) continue;
                phraseCounts.TryGetValue(phrase, out var count);
                phraseCounts[phrase] = count + 1;
            }
        }

        return phraseCounts
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    internal static bool IsStopPhrase(string phrase)
    {
        var words = phrase.Split(' ');
        var stopCount = words.Count(w => StopWords.Contains(w));
        return stopCount >= words.Length - 1;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "is", "was", "are", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "it", "its", "he", "she",
        "him", "her", "his", "they", "them", "their", "this", "that", "these",
        "those", "i", "you", "we", "not", "no", "if", "so", "as", "from"
    };
}
