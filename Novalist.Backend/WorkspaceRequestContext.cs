using System.Globalization;
using StreamJsonRpc;

namespace Novalist.Backend;

/// <summary>Transport-owned identity; never inferred from a renderer's method arguments.</summary>
internal sealed class WorkspaceRequestIdentity(string owner, long epoch, string? flushToken)
{
    internal string Owner { get; } = owner;
    internal long Epoch { get; set; } = epoch;
    internal string? FlushToken { get; } = flushToken;

    internal static WorkspaceRequestIdentity? Parse(RequestId id)
    {
        if (id.String is not string value || !value.StartsWith("nl/", StringComparison.Ordinal)) return null;
        var parts = value.Split('/');
        if (parts.Length != 5
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || string.IsNullOrEmpty(parts[4]))
            throw new InvalidOperationException("Invalid workspace request identity.");
        return new(parts[1], epoch, parts[4] == "-" ? null : parts[4]);
    }
}

/// <summary>A request may yield its serial lease while every window drains writes.</summary>
internal sealed class WorkspaceGateLease(SemaphoreSlim gate)
{
    internal bool Held { get; private set; } = true;
    internal bool Active { get; set; } = true;

    internal void Yield()
    {
        if (!Held) return;
        Held = false;
        gate.Release();
    }

    internal async Task ReacquireAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        Held = true;
    }
}

internal static class WorkspaceRequestContext
{
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();
    internal static WorkspaceRequestIdentity? Identity => CurrentFrame.Value?.Identity;
    internal static WorkspaceGateLease? Lease => CurrentFrame.Value?.Lease is { Active: true } lease ? lease : null;

    internal static IDisposable Enter(WorkspaceRequestIdentity? identity, WorkspaceGateLease? lease)
    {
        var previous = CurrentFrame.Value;
        CurrentFrame.Value = new(identity, lease);
        return new Restore(() => CurrentFrame.Value = previous);
    }

    private sealed record Frame(WorkspaceRequestIdentity? Identity, WorkspaceGateLease? Lease);
    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
