using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// The kinds of page a book carries around the story itself. These are typed
/// rather than free text because each one is laid out differently: a copyright
/// page is small and left-aligned, a dedication is centred and italic, a half
/// title is one line on its own page. Faking them as chapters gives every one of
/// them chapter treatment, which is what this replaces.
/// </summary>
public enum BookMatterKind
{
    HalfTitle,
    TitlePage,
    Copyright,
    Dedication,
    Epigraph,
    TableOfContents,
    Foreword,
    Preface,
    Prologue,
    Epilogue,
    Afterword,
    Acknowledgments,
    AboutTheAuthor,
    AlsoBy,
    Custom
}

/// <summary>Where a matter element sits relative to the story.</summary>
public enum BookMatterPlacement
{
    Front,
    Back
}

/// <summary>
/// One front- or back-matter page. Content is the same HTML the scene editor
/// produces, so these are written with the tools the writer already knows.
/// </summary>
public class BookMatterElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BookMatterKind Kind { get; set; } = BookMatterKind.Custom;

    [JsonPropertyName("placement")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BookMatterPlacement Placement { get; set; } = BookMatterPlacement.Front;

    /// <summary>
    /// Heading shown on the page. Empty means use the kind's conventional name,
    /// which is what most books do; a half title and a dedication conventionally
    /// show no heading at all.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>
    /// Whether this page is included in the exported book. Off is useful for a
    /// page that exists but is not ready, without deleting the text.
    /// </summary>
    [JsonPropertyName("included")]
    public bool Included { get; set; } = true;

    /// <summary>
    /// Whether the page appears in the generated table of contents. Front matter
    /// conventionally does not list itself, and a half title never appears.
    /// </summary>
    [JsonPropertyName("inTableOfContents")]
    public bool InTableOfContents { get; set; }

    /// <summary>
    /// Kinds that conventionally carry no visible heading: the page is the
    /// content. Used when <see cref="Title"/> is empty.
    /// </summary>
    public static bool ShowsHeadingByDefault(BookMatterKind kind) => kind switch
    {
        BookMatterKind.HalfTitle => false,
        BookMatterKind.TitlePage => false,
        BookMatterKind.Copyright => false,
        BookMatterKind.Dedication => false,
        BookMatterKind.Epigraph => false,
        _ => true
    };

    /// <summary>
    /// Whether a kind belongs at the front or the back when it is created. The
    /// writer can move it, but the default should be right nearly always.
    /// </summary>
    public static BookMatterPlacement DefaultPlacement(BookMatterKind kind) => kind switch
    {
        BookMatterKind.Epilogue => BookMatterPlacement.Back,
        BookMatterKind.Afterword => BookMatterPlacement.Back,
        BookMatterKind.Acknowledgments => BookMatterPlacement.Back,
        BookMatterKind.AboutTheAuthor => BookMatterPlacement.Back,
        BookMatterKind.AlsoBy => BookMatterPlacement.Back,
        _ => BookMatterPlacement.Front
    };

    /// <summary>
    /// Whether a kind is normally listed in the table of contents. Prologue and
    /// epilogue are story content and are listed; a copyright page is not.
    /// </summary>
    public static bool ListedInTableOfContentsByDefault(BookMatterKind kind) => kind switch
    {
        BookMatterKind.Foreword => true,
        BookMatterKind.Preface => true,
        BookMatterKind.Prologue => true,
        BookMatterKind.Epilogue => true,
        BookMatterKind.Afterword => true,
        BookMatterKind.Acknowledgments => true,
        BookMatterKind.AboutTheAuthor => true,
        _ => false
    };
}
