using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>One word of an invented language.</summary>
public sealed class ConlangWord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The word as it is written in the language.</summary>
    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    /// <summary>What it means, in the language the book is written in.</summary>
    [JsonPropertyName("meaning")]
    public string Meaning { get; set; } = string.Empty;

    /// <summary>Noun, verb, whatever the writer uses. Free text: an invented
    /// language need not have the parts of speech English does.</summary>
    [JsonPropertyName("partOfSpeech")]
    public string PartOfSpeech { get; set; } = string.Empty;

    /// <summary>How it sounds, however the writer chooses to write that -
    /// IPA, a rhyme, or a note to themselves.</summary>
    [JsonPropertyName("pronunciation")]
    public string Pronunciation { get; set; } = string.Empty;

    /// <summary>Anything else: etymology, register, who says it.</summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// An invented language and its dictionary.
///
/// Building one meant hand-rolling a custom entity type, which gets a writer a
/// list of entries and none of the thing a lexicon is for: looking a word up
/// while drafting, and finding out whether they have already coined it.
/// </summary>
public sealed class ConlangLanguage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Who speaks it, how it sounds, whatever the writer wants said
    /// about the language rather than about one of its words.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("words")]
    public List<ConlangWord> Words { get; set; } = [];
}
