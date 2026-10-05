namespace Novalist.Core.Services;

/// <summary>Respects file sharing before removing rendered audio.</summary>
internal static class NarrationFileCleanup
{
    internal static void Delete(string path)
    {
        // Unix permits unlinking an open file, so File.Delete alone can remove
        // a clip's URL while a FileStream reader is playing it. Acquire exclusive
        // access first and keep it through deletion; closing a probe before
        // deleting would leave a gap in which another reader could open the file.
        using var file = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}
