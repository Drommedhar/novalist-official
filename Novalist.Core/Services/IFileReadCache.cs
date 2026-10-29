namespace Novalist.Core.Services;

/// <summary>Controls content reuse within an asynchronous file-reading operation.</summary>
public interface IFileReadCache
{
    /// <summary>Allows validated content reuse until the returned scope is disposed.</summary>
    IDisposable UseCachedReads();

    /// <summary>Requires fresh reads, including when nested inside a cached-read scope.</summary>
    IDisposable UseFreshReads();
}
