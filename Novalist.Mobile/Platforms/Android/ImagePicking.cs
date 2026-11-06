namespace Novalist.Mobile.Services;

public static class ImagePicking
{
    private static readonly string[] PortableExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

    public static Task<string?> PickImageAsync(string title, string photosLabel, string filesLabel, string cancelLabel, TemporaryAccessRegistry access)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("No window is available for choosing an image.");
            var choice = await page.DisplayActionSheetAsync(title, cancelLabel, null, photosLabel, filesLabel);
            string? path;
            if (choice == filesLabel)
                path = await AndroidFilePicking.PickAsync(new PickOptions { PickerTitle = title, FileTypes = FilePickerFileType.Images }, access);
            else if (choice == photosLabel)
            {
                var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = title, SelectionLimit = 1, RotateImage = true });
                var photo = photos.FirstOrDefault();
                path = photo == null ? null : await AndroidFilePicking.StageAsync(photo, access);
            }
            else return null;
            return path == null ? null : await NormalizeAsync(path, access);
        });

    private static async Task<string> NormalizeAsync(string path, TemporaryAccessRegistry access)
    {
        if (PortableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return path;
        var temporary = Path.Combine(FileSystem.CacheDirectory, "normalized-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            using var bitmap = await Android.Graphics.BitmapFactory.DecodeFileAsync(path)
                ?? throw new InvalidDataException("The selected image cannot be decoded.");
            var format = Android.Graphics.Bitmap.CompressFormat.Jpeg
                ?? throw new InvalidOperationException("JPEG encoding is unavailable.");
            await using (var output = File.Create(temporary))
            {
                if (!await bitmap.CompressAsync(format, 90, output))
                    throw new IOException("The selected image could not be converted.");
            }
            return await AndroidFilePicking.StageAsync(new FileResult(temporary), access);
        }
        finally
        {
            File.Delete(temporary);
            access.Release(path);
        }
    }
}
