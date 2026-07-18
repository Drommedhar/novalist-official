using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>In-app version control over the project folder.</summary>
public sealed class GitRpc
{
    private readonly Workspace _workspace;
    private readonly GitService _git = new();
    private bool _initialized;

    public GitRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private async Task EnsureInitializedAsync()
    {
        var root = _workspace.Projects.ProjectRoot
            ?? throw new InvalidOperationException("No project open.");
        if (!_initialized)
        {
            await _git.InitializeAsync(root);
            _initialized = true;
        }
    }

    [JsonRpcMethod("git/status")]
    public async Task<GitStatusDto?> StatusAsync()
    {
        await EnsureInitializedAsync();
        var info = await _git.GetStatusAsync();
        if (info == null) return null;
        return new GitStatusDto(
            info.BranchName,
            info.HasRemote,
            info.AheadBy,
            info.BehindBy,
            info.ChangedFiles
                .Select(f => new GitFileDto(f.RelativePath, f.DisplayStatus.ToString(), f.IsStaged))
                .ToArray());
    }

    [JsonRpcMethod("git/commit")]
    public async Task<string?> CommitAsync(string[] relativePaths, string message)
    {
        await EnsureInitializedAsync();
        return await _git.CommitAsync(relativePaths, message);
    }

    [JsonRpcMethod("git/push")]
    public async Task<string?> PushAsync()
    {
        await EnsureInitializedAsync();
        return await _git.PushAsync();
    }

    [JsonRpcMethod("git/pull")]
    public async Task<string?> PullAsync()
    {
        await EnsureInitializedAsync();
        return await _git.PullAsync();
    }

    [JsonRpcMethod("git/discard")]
    public async Task<string?> DiscardAsync(string[] relativePaths)
    {
        await EnsureInitializedAsync();
        return await _git.DiscardChangesAsync(relativePaths);
    }
}

public sealed record GitStatusDto(
    string BranchName,
    bool HasRemote,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitFileDto> ChangedFiles);

public sealed record GitFileDto(string RelativePath, string Status, bool IsStaged);
