using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class IosProjectAccess : MobileProjectAccess
{
    private readonly IosFileAccessCoordinator _coordinator = new();
    private readonly IosStoredPathResolver _paths = new();
    public override IFileService Files { get; } = new CoordinatedFileService(new IosFileAccessCoordinator());
    public override string DefaultRoot => AppFolders.Documents;
    public override string? Resolve(string storedPath) => _paths.Resolve(storedPath);
    public override bool BeginAccess(string path) => SecurityScopedFolders.BeginAccess(path);
    public override void Release(string path) => _paths.Release(path);
    public override Task<string?> PickFolderAsync() => SecurityScopedFolders.PickFolderAsync();

    public override Task<string> ReadAssetAsync(string path, string mimeType)
        => _coordinator.ReadAsync(path, file => MobileAssetReader.ReadDataUri(file, mimeType));

    public override async Task SetActiveAsync(string? path)
    {
        IosStoredPathResolver.ActiveProjectPath = path;
        SecurityScopedFolders.ReleaseExcept(path);
        if (!string.IsNullOrEmpty(path))
            await IosStoredPathResolver.OwnedPaths.RecordAsync(path).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        IosStoredPathResolver.ActiveProjectPath = null;
        SecurityScopedFolders.ReleaseExcept(null);
    }
}
