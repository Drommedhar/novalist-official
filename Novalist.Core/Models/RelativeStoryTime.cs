using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>The unit a relative story time is measured in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoryTimeUnit
{
    Minutes,
    Hours,
    Days,
    Weeks
}

/// <summary>
/// "The next morning", said in a way the app can count with.
///
/// Novalist stored absolute dates and nothing else, so a writer who knows a
/// scene happens two hours after the last one - and does not know or care which
/// day that is - had to either invent a date or leave it blank. Blank meant the
/// scene fell out of the Calendar and the Timeline entirely, which is how a
/// whole book ends up undated.
///
/// Deliberately relative to the previous scene rather than to an arbitrary
/// anchor. That is how a writer thinks about it, and it means inserting a scene
/// in the middle shifts everything after it the way the story actually works.
/// </summary>
public sealed class RelativeStoryTime
{
    /// <summary>
    /// How much later. Negative is allowed: a scene can be an hour *before* the
    /// one printed ahead of it, which is what a cut-back is.
    /// </summary>
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("unit")]
    public StoryTimeUnit Unit { get; set; } = StoryTimeUnit.Hours;

    /// <summary>How long this converts to, in minutes.</summary>
    [JsonIgnore]
    public int TotalMinutes => Unit switch
    {
        StoryTimeUnit.Minutes => Amount,
        StoryTimeUnit.Hours => Amount * 60,
        StoryTimeUnit.Days => Amount * 60 * 24,
        // Weeks is the last arm rather than a case with an unreachable default
        // after it: the enum is exhaustive and a dead branch cannot be tested.
        _ => Amount * 60 * 24 * 7
    };
}
