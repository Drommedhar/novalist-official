#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed partial class HostedProjectWorkspace
{
    internal Task<IReadOnlyList<HostedProjectMember>> MembersAsync(CancellationToken ct)
    {
        var runtime = RequireOwner();
        return runtime.Session.Transport.ListProjectMembersAsync(runtime.Access.ProjectId, ct);
    }

    internal async Task RemoveMemberAsync(Guid member, CancellationToken ct)
    {
        var runtime = RequireOwner();
        var account = await accounts.RestoreAsync(runtime.Access.Server, ct)
            ?? throw new InvalidOperationException("Sign in before changing shared access.");
        using var device = await devices.GetOrCreateAsync(account.Authority, account.Subject, ct);
        using var access = await accessStore.LoadAsync(runtime.Access.Server, runtime.Access.ProjectId, device, ct)
            ?? throw new InvalidOperationException("The saved project credentials are missing.");
        await workspace.FlushSharedProjectFilesAsync();
        await runtime.Session.Updates.SynchronizeAsync(false, ct);
        var boundary = runtime.Session.Updates.LastServerSequence;
        await workspace.DetachSharedScenesAsync();
        var directory = Path.Combine(storage, "rotations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "checkpoint.zip");
        await using var archive = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await workspace.CaptureSharedProjectAsync(archive, ct);
        await archive.FlushAsync(ct);
        archive.Flush(true);
        archive.Position = 0;
        await using var transport = new HostedTransport(access.Server, device, () => account.GetAccessTokenAsync(CancellationToken.None));
        await transport.ConnectAsync(ct);
        await new HostedProjectRotation(storage, device, transport).RotateAsync(access, member, boundary, archive, ct);
        await ResumeAsync(ct);
    }

    internal async Task DeleteSharedProjectAsync(CancellationToken ct)
    {
        var runtime = RequireOwner();
        await runtime.Session.Transport.DeleteProjectAsync(runtime.Access.ProjectId, ct);
        await workspace.DetachSharedScenesAsync();
    }

    private Runtime RequireOwner()
    {
        var runtime = current ?? throw new InvalidOperationException("Open the shared project first.");
        if (!runtime.Access.IsOwner) throw new UnauthorizedAccessException("Only the project owner can manage shared access.");
        return runtime;
    }
}
#endif
