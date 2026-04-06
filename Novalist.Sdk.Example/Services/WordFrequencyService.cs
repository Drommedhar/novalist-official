using System.Text.RegularExpressions;

namespace Novalist.Sdk.Example;

/// <summary>
/// Analyzes word frequency in text content.
/// </summary>
public sealed class WordFrequencyService
{
    private bool _isDirty = true;
    private List<WordFrequencyEntry> _cached = [];

    public void Clear()
    {
        _cached.Clear();
        _isDirty = true;
    }

    public void MarkDirty() => _isDirty = true;

    public List<WordFrequencyEntry> Analyze(string text)
    {
        var words = Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 2)
            .GroupBy(w => w)
            .Select(g => new WordFrequencyEntry { Word = g.Key, Count = g.Count() })
            .OrderByDescending(e => e.Count)
            .Take(100)
            .ToList();

        _cached = words;
        _isDirty = false;
        return words;
    }

    public List<WordFrequencyEntry> GetCached() => _cached;
    public bool IsDirty => _isDirty;
}

public sealed class WordFrequencyEntry
{
    public string Word { get; init; } = string.Empty;
    public int Count { get; init; }
}
