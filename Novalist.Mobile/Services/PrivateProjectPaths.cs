using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class PrivateProjectPaths(string root) : IStoredPathResolver
{
    public string? Resolve(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || !Path.IsPathFullyQualified(storedPath)) return null;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storedPath));
        return normalizedPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? normalizedPath : null;
    }
}
