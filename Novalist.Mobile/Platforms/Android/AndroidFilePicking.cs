using Android.Webkit;

namespace Novalist.Mobile.Services;

public static class AndroidFilePicking
{
    public static FilePickerFileType ManuscriptTypes(IEnumerable<string> extensions)
        => new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = extensions.Select(extension =>
                MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream").Distinct()
        });

    public static async Task<string?> PickAsync(PickOptions options, TemporaryAccessRegistry access, bool manuscript = false)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options));
        if (manuscript && result != null) ManuscriptFileSelection.RequireStandalone(result.FileName);
        return result == null ? null : await StageAsync(result, access).ConfigureAwait(false);
    }

    public static async Task<string> StageAsync(FileResult result, TemporaryAccessRegistry access)
    {
        var directory = Path.Combine(FileSystem.CacheDirectory, "picked-files", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(result.FileName));
        void Release()
        {
            File.Delete(destination);
            Directory.Delete(directory);
        }
        try
        {
            await using var input = await result.OpenReadAsync().ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output).ConfigureAwait(false);
        }
        catch { Release(); throw; }
        if (!access.TryRetain(destination, Release)) throw new OperationCanceledException("The picker window was closed.");
        return destination;
    }
}
