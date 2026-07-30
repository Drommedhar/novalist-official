using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// What a chapter is, as opposed to what it is called.
///
/// Novalist had one ladder - book, draft, chapter, scene - and every writer
/// emitted its headings its own way. So a prologue was a chapter, which meant
/// it was numbered as one, which meant the first real chapter was Chapter Two.
/// The only fix was to hide the heading and type "Prologue" into the prose,
/// where no table of contents could see it.
/// </summary>
public sealed class SectionType
{
    /// <summary>Stable identifier a layout maps against.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the writer calls it.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// False for a section that stands outside the count. A prologue is not
    /// Chapter One, and the chapter after it is.
    /// </summary>
    [JsonPropertyName("numbered")]
    public bool Numbered { get; set; } = true;
}

/// <summary>The types every book starts with.</summary>
public static class SectionTypes
{
    /// <summary>The type a chapter has when the writer has not said otherwise.</summary>
    public const string Chapter = "chapter";

    /// <summary>
    /// Built-ins. Numbered chapters, and the four things books put around them
    /// that are conventionally not numbered.
    /// </summary>
    public static IReadOnlyList<SectionType> Defaults { get; } =
    [
        new() { Key = Chapter, Name = "Chapter", Numbered = true },
        new() { Key = "prologue", Name = "Prologue", Numbered = false },
        new() { Key = "epilogue", Name = "Epilogue", Numbered = false },
        new() { Key = "interlude", Name = "Interlude", Numbered = false },
        new() { Key = "part", Name = "Part", Numbered = false }
    ];

    /// <summary>
    /// The type for a key, from the book's own list first and the built-ins
    /// after. An unknown key reads as an ordinary chapter rather than as
    /// nothing: a chapter that vanishes from the export because its type was
    /// deleted is the worst outcome available.
    /// </summary>
    public static SectionType Resolve(string? key, IEnumerable<SectionType>? bookTypes)
    {
        var wanted = (key ?? string.Empty).Trim();
        if (wanted.Length == 0) return Defaults[0];

        return (bookTypes ?? []).FirstOrDefault(
                   t => t.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? Defaults.FirstOrDefault(
                   t => t.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? Defaults[0];
    }

    /// <summary>Everything a picker should offer: the book's own, then the built-ins.</summary>
    public static IReadOnlyList<SectionType> All(IEnumerable<SectionType>? bookTypes)
    {
        var own = (bookTypes ?? []).ToList();
        var keys = own.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. own, .. Defaults.Where(d => !keys.Contains(d.Key))];
    }
}

/// <summary>
/// How one section type is set in one layout.
///
/// This is what makes a single draft compile to a paperback, an ebook and a
/// submission without editing the draft: the layout decides what a prologue
/// looks like, not the chapter.
/// </summary>
public sealed class SectionLayout
{
    [JsonPropertyName("typeKey")]
    public string TypeKey { get; set; } = string.Empty;

    /// <summary>
    /// The heading, with <c>{number}</c> and <c>{title}</c>. Empty falls back
    /// to the layout's own chapter format, so a layout only has to describe
    /// the types it treats differently.
    /// </summary>
    [JsonPropertyName("titleFormat")]
    public string TitleFormat { get; set; } = string.Empty;

    /// <summary>Null keeps the layout's numbering style.</summary>
    [JsonPropertyName("numberStyle")]
    public ChapterNumberStyle? NumberStyle { get; set; }

    /// <summary>Null keeps the layout's capitalisation.</summary>
    [JsonPropertyName("uppercase")]
    public bool? Uppercase { get; set; }
}
