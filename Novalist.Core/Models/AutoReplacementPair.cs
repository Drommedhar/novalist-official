using System.Text.Json.Serialization;
using Novalist.Core.Services;

namespace Novalist.Core.Models;

/// <summary>
/// One thing that gets substituted as the writer types.
///
/// A literal rule fires when the characters in <see cref="Start"/> are the ones
/// just typed. A rule whose start and end are the same trigger with different
/// replacements alternates between them, which is how a quotation mark knows
/// whether it is opening or closing.
///
/// A regex rule matches <see cref="Start"/> as a pattern against the text
/// ending at the caret, and <see cref="StartReplace"/> may refer back to what
/// it captured as <c>$1</c>. <see cref="End"/> and <see cref="EndReplace"/>
/// carry nothing for a regex rule: there is no alternating form of a pattern.
/// </summary>
public class AutoReplacementPair
{
    /// <summary>Literal trigger, or the pattern when <see cref="Kind"/> is regex.</summary>
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    /// <summary>What lands. A regex rule may use <c>$1</c> for a capture.</summary>
    [JsonPropertyName("startReplace")]
    public string StartReplace { get; set; } = string.Empty;

    [JsonPropertyName("endReplace")]
    public string EndReplace { get; set; } = string.Empty;

    /// <summary>
    /// <c>literal</c> or <c>regex</c>. Absent in every settings file written
    /// before custom rules existed, so literal is the default and an older file
    /// keeps working untouched.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = AutoReplacementKinds.Literal;

    /// <summary>True when this rule's start is a pattern rather than characters.</summary>
    [JsonIgnore]
    public bool IsRegex =>
        string.Equals(Kind, AutoReplacementKinds.Regex, StringComparison.OrdinalIgnoreCase);
}

public static class AutoReplacementKinds
{
    public const string Literal = "literal";
    public const string Regex = "regex";
}

public static class AutoReplacementDefaults
{
    private static readonly AutoReplacementPair[] CommonReplacements =
    [
        new() { Start = "--", End = "--", StartReplace = "\u2014", EndReplace = "\u2014" },
        new() { Start = "...", End = "...", StartReplace = "\u2026", EndReplace = "\u2026" }
    ];

    private static readonly Dictionary<string, AutoReplacementPair[]> LanguagePresets = new()
    {
        ["en"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201C", EndReplace = "\u201D" },
            .. CommonReplacements
        ],
        ["de-low"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["de-guillemet"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00BB", EndReplace = "\u00AB" },
            .. CommonReplacements
        ],
        ["fr"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB\u00A0", EndReplace = "\u00A0\u00BB" },
            .. CommonReplacements
        ],
        ["es"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["it"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["pt"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["ru"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["pl"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["cs"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["sk"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["nl"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201C", EndReplace = "\u201D" },
            .. CommonReplacements
        ],
        // Simplified Chinese sets horizontal dialogue in the same curly marks
        // English does. It is here because this list is the only way to say what
        // language a book is written in, and everything downstream reads that:
        // which analysis pack runs, what an export declares, and which language
        // the book is read aloud in. With no entry, a Chinese novel was
        // tokenised as English and read in an English accent.
        ["zh-CN"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201C", EndReplace = "\u201D" },
            .. CommonReplacements
        ],
        // Corner brackets, which DialogueScanner.QuoteRegex now recognises. It
        // did not before, so a book written in them had no dialogue at all as
        // far as the app was concerned - and typing marks nothing can read back
        // would have been worse than not offering the language.
        ["ja"] = [
            new() { Start = "'", End = "'", StartReplace = "\u300C", EndReplace = "\u300D" },
            .. CommonReplacements
        ],
        ["ko"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201C", EndReplace = "\u201D" },
            .. CommonReplacements
        ],
    };

    public static List<string> AvailableLanguages => [.. LanguagePresets.Keys];

    public static List<AutoReplacementPair> GetPreset(string language)
    {
        if (LanguagePresets.TryGetValue(language, out var pairs))
            return pairs.Select(p => new AutoReplacementPair
            {
                Start = p.Start,
                End = p.End,
                StartReplace = p.StartReplace,
                EndReplace = p.EndReplace
            }).ToList();

        return GetPreset("en");
    }
}
