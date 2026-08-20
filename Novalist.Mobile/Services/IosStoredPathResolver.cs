using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

/// <summary>
/// The iOS answer to "this path has stopped working" - see
/// <see cref="IStoredPathResolver"/> for why the workspace has to ask.
///
/// Two things break a path that was correct when it was written, and this tries
/// them in the order they cost:
///
///   1. A folder the writer picked is only readable while its security-scoped
///      grant is active. Resolving its bookmark resumes the grant and reports
///      where the folder is now, both at once.
///   2. A project inside Novalist's own Documents folder is not bookmarked and
///      does not need to be - but the container it sits in is re-created under a
///      new UUID whenever the app is updated, so the stored path names a
///      directory that no longer exists while the project itself is untouched.
///      Everything after "/Documents/" survives that; only what is in front of it
///      moved, and this install knows where that is.
///
/// Returning null means neither applied, and the caller is free to conclude
/// whatever the filesystem told it.
/// </summary>
public sealed class IosStoredPathResolver : IStoredPathResolver
{
    public string? Resolve(string storedPath)
    {
        if (string.IsNullOrEmpty(storedPath)) return null;
        return SecurityScopedFolders.ResolveCurrentPath(storedPath)
            ?? InThisInstallsDocuments(storedPath);
    }

    private static string? InThisInstallsDocuments(string storedPath)
    {
        const string marker = "/Documents/";
        var cut = storedPath.LastIndexOf(marker, StringComparison.Ordinal);
        if (cut < 0) return null;

        var documents = AppFolders.Documents;
        if (string.IsNullOrEmpty(documents)) return null;

        var candidate = Path.Combine(documents, storedPath[(cut + marker.Length)..]);
        if (string.Equals(candidate, storedPath, StringComparison.Ordinal)) return null;
        return Directory.Exists(candidate) ? candidate : null;
    }
}
