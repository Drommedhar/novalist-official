using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// In-world date range attached to a scene, chapter, act, or timeline event.
/// String-based so it works with custom calendars as well as Gregorian dates.
/// Parsing / arithmetic is delegated to <see cref="Novalist.Core.Services.IInWorldCalendarService"/>.
/// </summary>
public class StoryDateRange
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasValue
        => !string.IsNullOrWhiteSpace(Start)
           || !string.IsNullOrWhiteSpace(End)
           || !string.IsNullOrWhiteSpace(Note);

    public StoryDateRange Clone() => new() { Start = Start, End = End, Note = Note };
}
