using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One finished writing sprint.
///
/// Novalist's smallest unit was a calendar day, so it could not answer "how did
/// this sitting go" - which is the only question a writer asks while they are
/// still in the chair.
/// </summary>
public sealed class WritingSprint
{
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>How long the sprint actually ran, which is not the target when
    /// it was stopped early.</summary>
    [JsonPropertyName("seconds")]
    public int Seconds { get; set; }

    /// <summary>What the writer aimed at, in minutes. Zero for an open-ended
    /// sprint with no clock to beat.</summary>
    [JsonPropertyName("targetMinutes")]
    public int TargetMinutes { get; set; }

    [JsonPropertyName("words")]
    public int Words { get; set; }

    /// <summary>
    /// Words per minute over the sprint. Zero for a sprint too short to divide
    /// by: a five-second sprint that produced one word is not a 12 wpm pace,
    /// it is noise.
    /// </summary>
    [JsonIgnore]
    public int WordsPerMinute
        => Seconds < MinimumSecondsForPace ? 0 : (int)Math.Round(Words * 60.0 / Seconds);

    /// <summary>Below this, a pace figure says more about the arithmetic than
    /// about the writing.</summary>
    public const int MinimumSecondsForPace = 30;
}
