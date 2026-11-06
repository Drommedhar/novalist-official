namespace Novalist.Core.Services;

public interface ICancellableArchiveService : IArchiveService
{
    Task<int> CreateFromDirectoryAsync(string sourceDirectory, string destinationZipPath,
        IReadOnlyCollection<string> excludedDirectoryNames, CancellationToken cancellationToken);
}
