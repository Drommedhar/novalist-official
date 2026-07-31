using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>A word found by a lookup, and which language it belongs to.</summary>
public sealed record ConlangHit(string LanguageId, string LanguageName, ConlangWord Word);

/// <summary>
/// The invented languages of a project and their dictionaries.
///
/// Project-wide rather than per book: a language outlives the volume it was
/// coined for, and a trilogy whose second book cannot see the first book's
/// words is a dictionary that has to be typed twice.
///
/// The thing a lexicon is actually for is looking a word up mid-sentence and
/// finding out whether it has already been coined - so the lookup matches both
/// directions, the word and the meaning, and it does not care about case.
/// </summary>
public sealed class ConlangService
{
    private readonly IProjectService _projects;

    public ConlangService(IProjectService projects)
    {
        _projects = projects;
    }

    private List<ConlangLanguage>? Languages => _projects.CurrentProject?.Languages;

    /// <summary>Every language, in the order they were made.</summary>
    public IReadOnlyList<ConlangLanguage> GetAll() => Languages ?? [];

    /// <summary>Adds a language and returns it, or null with no project open.</summary>
    public async Task<ConlangLanguage?> CreateAsync(string name)
    {
        var list = Languages;
        if (list == null) return null;

        var language = new ConlangLanguage
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Language" : name.Trim()
        };
        list.Add(language);
        await _projects.SaveProjectAsync().ConfigureAwait(false);
        return language;
    }

    /// <summary>Renames a language, or rewrites what is said about it.</summary>
    public async Task<bool> UpdateAsync(string languageId, string? name, string? description)
    {
        var language = Languages?.FirstOrDefault(l => l.Id == languageId);
        if (language == null) return false;

        if (!string.IsNullOrWhiteSpace(name)) language.Name = name.Trim();
        if (description != null) language.Description = description;
        await _projects.SaveProjectAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Removes a language and every word in it. The only call here that loses
    /// anything, so the caller is expected to have asked first.
    /// </summary>
    public async Task<bool> DeleteAsync(string languageId)
    {
        var list = Languages;
        if (list == null || list.All(l => l.Id != languageId)) return false;

        list.RemoveAll(l => l.Id == languageId);
        await _projects.SaveProjectAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Adds a word, or rewrites one that is already there. An empty id creates.
    /// Returns the word as stored, or null when the language is unknown.
    /// </summary>
    public async Task<ConlangWord?> SaveWordAsync(string languageId, ConlangWord word)
    {
        var language = Languages?.FirstOrDefault(l => l.Id == languageId);
        if (language == null || word == null) return null;

        var existing = string.IsNullOrEmpty(word.Id)
            ? null
            : language.Words.FirstOrDefault(w => w.Id == word.Id);

        var stored = existing ?? new ConlangWord();
        stored.Word = (word.Word ?? string.Empty).Trim();
        stored.Meaning = (word.Meaning ?? string.Empty).Trim();
        stored.PartOfSpeech = (word.PartOfSpeech ?? string.Empty).Trim();
        stored.Pronunciation = (word.Pronunciation ?? string.Empty).Trim();
        stored.Notes = word.Notes ?? string.Empty;
        if (existing == null) language.Words.Add(stored);

        await _projects.SaveProjectAsync().ConfigureAwait(false);
        return stored;
    }

    /// <summary>Removes one word. False when neither it nor its language is there.</summary>
    public async Task<bool> DeleteWordAsync(string languageId, string wordId)
    {
        var language = Languages?.FirstOrDefault(l => l.Id == languageId);
        if (language == null || language.Words.All(w => w.Id != wordId)) return false;

        language.Words.RemoveAll(w => w.Id == wordId);
        await _projects.SaveProjectAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Words matching a query, across every language or within one.
    ///
    /// Both directions on purpose: a writer mid-sentence either has the
    /// invented word and wants the meaning, or has the meaning and wants to
    /// know whether they already coined a word for it. A lookup that only did
    /// the first is half a dictionary.
    /// </summary>
    public IReadOnlyList<ConlangHit> Lookup(string query, string? languageId = null)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0) return [];

        var hits = new List<ConlangHit>();
        foreach (var language in Languages ?? [])
        {
            if (languageId != null && language.Id != languageId) continue;
            foreach (var word in language.Words)
            {
                if (Matches(word.Word, text) || Matches(word.Meaning, text))
                    hits.Add(new ConlangHit(language.Id, language.Name, word));
            }
        }

        // An exact word first: somebody typing a coined word in full wants that
        // word, not the six entries whose meanings mention it.
        return [.. hits
            .OrderByDescending(h => string.Equals(h.Word.Word, text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(h => h.Word.Word, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static bool Matches(string? value, string query)
        => !string.IsNullOrEmpty(value)
            && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
