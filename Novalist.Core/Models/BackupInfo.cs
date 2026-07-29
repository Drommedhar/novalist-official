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
}
