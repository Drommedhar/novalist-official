using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>Where a manuscript went.</summary>
public static class SubmissionStatuses
{
    public const string Sent = "sent";
    public const string Rejected = "rejected";

    /// <summary>They asked for more - a partial or the full manuscript.</summary>
    public const string Requested = "requested";
    public const string Accepted = "accepted";
    public const string Withdrawn = "withdrawn";

    /// <summary>The writer has decided the silence is the answer.</summary>
    public const string NoReply = "noReply";

    public static readonly string[] All =
        [Sent, Requested, Accepted, Rejected, Withdrawn, NoReply];

    /// <summary>
    /// True while this submission is still out. A duplicate send only matters
    /// against one of these: sending again after a rejection is a new attempt,
    /// not a mistake.
    /// </summary>
    public static bool IsOpen(string? status)
        => string.IsNullOrWhiteSpace(status)
           || status.Equals(Sent, StringComparison.OrdinalIgnoreCase)
           || status.Equals(Requested, StringComparison.OrdinalIgnoreCase);

    /// <summary>An unknown status reads as still out rather than as resolved.</summary>
    public static string Normalise(string? status)
        => All.FirstOrDefault(s => s.Equals(status?.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? Sent;
}

/// <summary>
/// One submission of this book to one recipient.
///
/// Novalist produced submission-ready material - the Exposé, the Shunn layout -
/// and recorded nothing about where any of it was sent or what came back. So
/// the one thing a writer must not do, send the same manuscript to the same
/// agent twice, was the one thing the app could not help with.
/// </summary>
public sealed class Submission
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The agent, publisher, magazine or contest.</summary>
    [JsonPropertyName("recipient")]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>What was sent: a query, three chapters, the full.</summary>
    [JsonPropertyName("material")]
    public string Material { get; set; } = string.Empty;

    /// <summary>
    /// When it went, as the writer typed it. Free text rather than a date so a
    /// half-remembered "March" can be recorded instead of nothing.
    /// </summary>
    [JsonPropertyName("sentOn")]
    public string SentOn { get; set; } = string.Empty;

    /// <summary>One of <see cref="SubmissionStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = SubmissionStatuses.Sent;

    /// <summary>When they answered, if they did.</summary>
    [JsonPropertyName("respondedOn")]
    public string RespondedOn { get; set; } = string.Empty;

    /// <summary>Whatever else is worth remembering about it.</summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}
