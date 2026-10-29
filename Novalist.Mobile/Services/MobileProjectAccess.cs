using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public abstract class MobileProjectAccess : IStoredPathResolver, IDisposable
{
    public abstract IFileService Files { get; }
    public virtual IArchiveService Archives { get; } = new ArchiveService();
    public abstract string DefaultRoot { get; }
    public abstract string? Resolve(string storedPath);
    public abstract bool BeginAccess(string path);
    public abstract void Release(string path);
    public abstract Task SetActiveAsync(string? path);
    public abstract Task<string?> PickFolderAsync();
    public abstract Task<string> ReadAssetAsync(string path, string mimeType);
    public abstract void Dispose();

    public static MobileProjectAccess Create()
    {
#if IOS
        return new IosProjectAccess();
#elif ANDROID
        return new AndroidProjectAccess();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
