using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class AndroidProjectAccess : MobileProjectAccess
{
    private readonly DocumentTreeMounts _mounts = new(Path.Combine(FileSystem.AppDataDirectory, "Documents"), uri => new AndroidDocumentTree(uri));
    private readonly DocumentTreeFileService _files;
    public override IFileService Files => _files;
    public override IArchiveService Archives { get; }
    public override string DefaultRoot { get; } = Path.Combine(FileSystem.AppDataDirectory, "Projects");
    public AndroidProjectAccess()
    {
        _files = new DocumentTreeFileService(_mounts, new FileService());
        Archives = new DocumentTreeArchiveService(Files, IsExternalPath, FileSystem.CacheDirectory);
    }

    public bool IsExternalPath(string path) => _mounts.IsExternalPath(path);

    public override string? Resolve(string storedPath)
    {
        if (!IsExternalPath(storedPath)) return new PrivateProjectPaths(DefaultRoot).Resolve(storedPath);
        try { return _mounts.Resolve(storedPath) != null ? Path.GetFullPath(storedPath) : null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    public override bool BeginAccess(string path)
    {
        if (Resolve(path) == null) return false;
        _files.InvalidateReadCache(path);
        return true;
    }
    public override void Release(string path) { }
    public override void Dispose() { }

    public override Task SetActiveAsync(string? path) => Task.Run(() =>
    {
        if (!string.IsNullOrEmpty(path) && !BeginAccess(path))
            throw new UnauthorizedAccessException("Choose the project folder again to grant access.");
    });

    public override async Task<string> ReadAssetAsync(string path, string mimeType)
    {
        if (Resolve(path) == null) throw new UnauthorizedAccessException("Project access is required.");
        return $"data:{mimeType};base64,{Convert.ToBase64String(await Files.ReadBytesAsync(path).ConfigureAwait(false))}";
    }

    public override async Task<string?> PickFolderAsync()
    {
        var uri = await AndroidDocumentPicker.PickAsync().ConfigureAwait(false);
        return uri == null ? null : await Task.Run(() => _mounts.Register(uri)).ConfigureAwait(false);
    }
}
