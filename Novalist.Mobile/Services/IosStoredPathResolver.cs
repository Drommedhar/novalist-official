using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

/// <summary>
/// Resolves external bookmarks and recorded app-owned projects after a container move.
/// </summary>
public sealed class IosStoredPathResolver : IStoredPathResolver
{
    internal static string? ActiveProjectPath { get; set; }

    internal static OwnedProjectPaths OwnedPaths => new(AppFolders.Documents,
        Path.Combine(Microsoft.Maui.Storage.FileSystem.Current.AppDataDirectory, "owned-project-paths.json"));

    public string? Resolve(string storedPath)
    {
        if (string.IsNullOrEmpty(storedPath)) return null;
        return SecurityScopedFolders.ResolveCurrentPath(storedPath)
            ?? OwnedPaths.Resolve(storedPath);
    }

    public void Release(string resolvedPath)
    {
        if (!string.Equals(resolvedPath, ActiveProjectPath, StringComparison.Ordinal))
            SecurityScopedFolders.EndAccess(resolvedPath);
    }
}
