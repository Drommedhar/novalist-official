namespace Novalist.Mobile.Services;

/// <summary>Bounds the allocations made by the base64 webview asset bridge.</summary>
public static class MobileAssetReader
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static string ReadDataUri(string path, string mimeType)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw TooLarge();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw TooLarge();
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static IOException TooLarge()
        => new("This media file is too large for the mobile preview (16 MiB limit).");
}
