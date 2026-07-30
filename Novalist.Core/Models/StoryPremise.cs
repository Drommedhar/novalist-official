using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// The book in one line, then one paragraph, then one summary per act.
///
/// Novalist has always had a Snowflake-shaped setup wizard in the codebase and
/// no way to reach it, and nowhere for its answers to live. A premise that only
/// exists in the writer's head cannot be checked against the book, and a
/// paragraph pasted into a scene is prose the export would print.
/// </summary>
public sealed class StoryPremise
{
    /// <summary>One sentence: a character wants something but something stops them.</summary>
    [JsonPropertyName("logline")]
    public string Logline { get; set; } = string.Empty;

    /// <summary>The premise opened out: world, stakes, inciting incident, rough climax.</summary>
    [JsonPropertyName("paragraph")]
    public string Paragraph { get; set; } = string.Empty;

    /// <summary>
    /// A summary per act, keyed by the act name the chapters use, so the ladder
    /// stays attached to the structure rather than assuming three acts.
    /// </summary>
    [JsonPropertyName("acts")]
    public Dictionary<string, string> Acts { get; set; } = [];

    // ── The pitch ──
    //
    // Every one of these is asked for by name on a query letter, a submission
    // form or a retailer page, and every one of them lived in a document
    // somewhere outside Novalist - which is how a comparable title gets quoted
    // from memory and a genre gets described three different ways in three
    // different submissions.

    /// <summary>Genre as a shop would file it.</summary>
    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    /// <summary>Who it is for - age band, readership, the shelf it sits on.</summary>
    [JsonPropertyName("audience")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Comparable titles: the two or three books an agent is asked for.</summary>
    [JsonPropertyName("comparables")]
    public string Comparables { get; set; } = string.Empty;

    /// <summary>Where and when it is set.</summary>
    [JsonPropertyName("setting")]
    public string Setting { get; set; } = string.Empty;

    /// <summary>
    /// The back-cover copy: what a reader is told to make them open it. Not the
    /// synopsis - a blurb withholds the ending on purpose.
    /// </summary>
    [JsonPropertyName("blurb")]
    public string Blurb { get; set; } = string.Empty;

    /// <summary>
    /// The one-page synopsis, ending included, which is the opposite decision
    /// from the blurb and the reason both are here.
    /// </summary>
    [JsonPropertyName("synopsis")]
    public string Synopsis { get; set; } = string.Empty;
}
