#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed record SavedHostedProjectDto(string Server, Guid ProjectId, string? RecoveryPath);

internal sealed partial class HostedProjectWorkspace
{
    private sealed record SavedHostedProject(int Version, Guid SessionId, Guid ProjectId, Uri Server,
        Guid DeviceId, string Root, string LocalProjectId, CheckpointReference Checkpoint, string? RecoveryPath);

    internal async Task<SavedHostedProjectDto?> SavedAsync()
    {
        var saved = await FindSavedAsync();
        return saved is null ? null : new(saved.Server.AbsoluteUri, saved.ProjectId, saved.RecoveryPath);
    }

    private async Task<SavedHostedProject?> FindSavedAsync()
    {
        if (workspace.Projects.ProjectRoot is not { } root) return null;
        var path = RegistryPath(root);
        if (!File.Exists(path)) return null;
        var saved = JsonSerializer.Deserialize<SavedHostedProject>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("Invalid saved collaboration context.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (saved.Version != 2 || saved.SessionId == Guid.Empty || saved.ProjectId == Guid.Empty || saved.DeviceId == Guid.Empty ||
            !Path.GetFullPath(saved.Root).Equals(Path.GetFullPath(root), comparison) || saved.LocalProjectId != ReadProjectId(root))
            throw new InvalidDataException("The saved collaboration context belongs to a different local project.");
        return saved;
    }

    private string RegistryPath(string root)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return Path.Combine(storage, "projects", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) + ".json");
    }

    private async Task RememberAsync(SavedHostedProject record)
    {
        var json = JsonSerializer.Serialize(record);
        await workspace.FileService.WriteTextAsync(Path.Combine(storage, "sessions", record.SessionId.ToString("N"), "session.json"), json);
        await workspace.FileService.WriteTextAsync(RegistryPath(record.Root), json);
    }

    private static string ReadProjectId(string root)
    {
        using var project = JsonDocument.Parse(File.ReadAllBytes(SharedProjectPath.Resolve(root, ".novalist/project.json")));
        return project.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("The project identity is missing.");
    }

    internal async Task<HostedSessionDto> ResumeAsync(CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        var saved = await FindSavedAsync() ?? throw new InvalidOperationException("This local project has no saved hosted connection.");
        var runtime = await ReopenAsync(saved, ct);
        try
        {
            var archivePath = Path.Combine(storage, "sessions", saved.SessionId.ToString("N"), "checkpoint.zip");
            if (runtime.Id != saved.SessionId) File.Copy(archivePath, Path.Combine(storage, "sessions", runtime.Id.ToString("N"), "checkpoint.zip"));
            await using var archive = File.OpenRead(archivePath);
            using var restore = await RetainedProjectRestore.PrepareAsync(saved.Root, archive, storage, ct);
            runtime.RecoveryPath = restore.RecoveryPath;
            await RememberAsync(runtime, saved.Root, ct);
            await runtime.Session.StartAsync(createProject: false, ct);
            try
            {
                await restore.ApplyAsync(workspace.FileService, ct);
                await workspace.OpenProjectAsync(saved.Root);
                archive.Position = 0;
                await workspace.AttachSharedProjectAsync(runtime.Id, runtime.Session.Updates, restore.State, archive, ct);
            }
            catch
            {
                await restore.RollbackAsync(workspace.FileService);
                await workspace.OpenProjectAsync(saved.Root);
                throw;
            }
        }
        catch { await runtime.DisposeAsync(); throw; }
        current = runtime;
        return runtime.View();
    }

    private async Task<Runtime> ReopenAsync(SavedHostedProject saved, CancellationToken ct)
    {
        var account = await accounts.RestoreAsync(saved.Server, ct) ?? throw new InvalidOperationException("Sign in to the saved collaboration server first.");
        var device = await devices.GetOrCreateAsync(account.Authority, account.Subject, ct);
        HostedProjectAccess? access = null;
        HostedProjectSession? session = null;
        try
        {
            if (device.Id != saved.DeviceId) throw new InvalidOperationException("Sign in with the account that joined this local project.");
            access = await accessStore.LoadAsync(saved.Server, saved.ProjectId, device, ct)
                ?? throw new InvalidOperationException("This device no longer has the saved project credentials.");
            var id = saved.SessionId;
            session = await HostedProjectSession.OpenAsync(storage, id, access, device, () => account.GetAccessTokenAsync(CancellationToken.None));
            if (session.IsClosed)
            {
                await session.DisposeAsync();
                id = Guid.NewGuid();
                session = await HostedProjectSession.OpenAsync(storage, id, access, device, () => account.GetAccessTokenAsync(CancellationToken.None));
            }
            return new(id, device, access, session, account.DisplayName) { Checkpoint = saved.Checkpoint };
        }
        catch
        {
            if (session is not null) await session.DisposeAsync();
            access?.Dispose();
            device.Dispose();
            throw;
        }
    }
}
#endif
