namespace Novalist.Core.Services;

/// <summary>
/// Abstraction over ZIP creation and extraction so backup logic stays testable
/// without reaching for the real compression stack.
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Archives <paramref name="sourceDirectory"/> into <paramref name="destinationZipPath"/>.
    /// Any directory whose name matches an entry of <paramref name="excludedDirectoryNames"/>
    /// is skipped wherever it appears in the tree. Returns the number of files written.
    /// </summary>
    Task<int> CreateFromDirectoryAsync(
        string sourceDirectory,
        string destinationZipPath,
        IReadOnlyCollection<string> excludedDirectoryNames);

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <paramref name="destinationDirectory"/>,
    /// overwriting files that already exist. Returns the number of entries restored.
    /// </summary>
    Task<int> ExtractToDirectoryAsync(string zipPath, string destinationDirectory);
}
