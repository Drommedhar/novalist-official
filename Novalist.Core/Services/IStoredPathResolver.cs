namespace Novalist.Core.Services;

/// <summary>
/// Turns a path recorded in settings back into one that works right now.
///
/// A desktop path is its own address: what was written into the recent-projects
/// list last week still names the same folder today. A sandboxed platform breaks
/// that assumption twice over. On iOS a folder outside the app is only readable
/// while a security-scoped bookmark for it is active, so a path that is perfectly
/// correct reads as missing until the grant is resumed; and the container the app
/// itself lives in is re-created under a new UUID whenever the app is updated,
/// which rewrites the absolute path of every project stored inside it while the
/// settings file still names the old one.
///
/// Both look identical from Core: a path that is not there. The platform is the
/// only layer that can tell the difference, so it supplies one of these and the
/// workspace asks before it concludes anything.
/// </summary>
public interface IStoredPathResolver
{
    /// <summary>
    /// Where <paramref name="storedPath"/> lives now, having made it reachable
    /// (resuming a folder grant, following a moved container). Returns the same
    /// path when it never moved and only needed to be unlocked, and null when
    /// there is nothing this resolver can do for it.
    /// </summary>
    string? Resolve(string storedPath);
}
