using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Novalist.Mobile.Services;

public static class ExportSharing
{
    public static async Task<string> ShareAsync(string path)
    {
        // Android returns before the recipient reads the file. Keep an independent
        // cache copy after ExportFiles releases the original export directory.
        var directory = Path.Combine(FileSystem.CacheDirectory, "shared-files", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var shared = Path.Combine(directory, Path.GetFileName(path));
        File.Copy(path, shared);
        await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            File = new ShareFile(shared)
        })).ConfigureAwait(false);
        return "handed-off";
    }
}
