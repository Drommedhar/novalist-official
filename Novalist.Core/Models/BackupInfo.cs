namespace Novalist.Core.Models;

/// <summary>One archived copy of a project folder.</summary>
public sealed class BackupInfo
{
    /// <summary>Archive file name without the extension. Stable identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Absolute path of the archive.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>When the archive was written, in UTC.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Archive size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>What triggered it: "open", "close", "interval" or "manual".</summary>
    public string Trigger { get; init; } = string.Empty;

    /// <summary>
    /// Whether the writer named this archive and asked for it to be kept. A
    /// milestone survives retention: the point of marking "draft two" is that
    /// it is still there in six months, when a rotating backup would be long
    /// gone.
    /// </summary>
    public bool IsMilestone => Trigger.StartsWith(MilestonePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name the writer gave a milestone, or empty for an ordinary archive.
    /// </summary>
    public string Name => IsMilestone
        ? Trigger[MilestonePrefix.Length..].Replace('-', ' ').Trim()
        : string.Empty;

    /// <summary>Marks a milestone in the archive name, which is where it has to live to survive a copy.</summary>
    internal const string MilestonePrefix = "milestone-";
}
