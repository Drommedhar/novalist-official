using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One Codex entry cleared for a model, with the withheld sections
/// already removed.</summary>
public sealed record AiContextEntry(
    string Id,
    string TypeKey,
    string Name,
    AiInclusion Inclusion,
    IReadOnlyList<EntitySection> Sections);

/// <summary>
/// Decides which Codex entries an extension is allowed to put in front of an AI
/// model, and which parts of them.
///
/// This lives in core rather than in each extension on purpose. An extension
/// assembling its own context is the thing the writer cannot audit: they set an
/// entry to Never precisely because they do not trust something else to decide.
/// Making the host compute the allowed set means honouring the policy is the
/// path of least resistance rather than an extension author's good intention.
/// </summary>
public static class AiContextPolicy
{
    /// <summary>
    /// Filters entries down to what may be sent for a scene.
    ///
    /// <paramref name="mentionedIds"/> is the set the scene actually names.
    /// An entry set to <see cref="AiInclusion.Always"/> comes along whether or
    /// not it is in there; one set to <see cref="AiInclusion.Never"/> never does.
    /// </summary>
    public static IReadOnlyList<AiContextEntry> Allowed(
        IEnumerable<AiContextEntry> entries, IReadOnlySet<string> mentionedIds)
        => [.. entries
            .Where(e => e.Inclusion switch
            {
                AiInclusion.Never => false,
                AiInclusion.Always => true,
                _ => mentionedIds.Contains(e.Id)
            })
            .Select(Redact)];

    /// <summary>
    /// Drops the sections the writer withheld. Applied on the way out rather
    /// than at the call site, so an entry that reaches a model has already lost
    /// its hidden parts and cannot leak them by being passed somewhere else.
    /// </summary>
    public static AiContextEntry Redact(AiContextEntry entry)
        => entry with { Sections = [.. entry.Sections.Where(s => !s.AiHidden)] };

    /// <summary>Whether an entry may be sent at all, ignoring mentions. Useful
    /// for a caller assembling context outside a scene.</summary>
    public static bool MaySend(AiInclusion inclusion) => inclusion != AiInclusion.Never;
}
