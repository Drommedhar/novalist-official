using Foundation;

namespace Novalist.Mobile.Services;

/// <summary>Folders inside the app's own container, resolved fresh every time.</summary>
public static class AppFolders
{
    /// <summary>
    /// Novalist's Documents folder - what the Files app shows as "On My iPhone
    /// (or iPad) -> Novalist", because Info.plist declares UIFileSharingEnabled
    /// and LSSupportsOpeningDocumentsInPlace.
    ///
    /// It is where new projects go by default, and it is readable without any
    /// security-scoped grant, which is what makes those projects survive a launch
    /// the writer's own folders would not. Asked for each time rather than cached
    /// across a session: an install writes the container under a new UUID, and a
    /// path remembered from the last one is exactly the bug this folder exists to
    /// avoid.
    /// </summary>
    public static string Documents
    {
        get
        {
            var dirs = NSSearchPath.GetDirectories(
                NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User, true);
            return dirs.Length > 0 ? dirs[0] : string.Empty;
        }
    }
}
