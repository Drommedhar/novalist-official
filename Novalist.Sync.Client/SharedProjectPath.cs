namespace Novalist.Sync.Client;

public static class SharedProjectPath
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$",
          "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "extensions" };

    internal static readonly string[] MetadataFiles = ["project.json", "settings.json", "darlings.json", "gallery.json"];
    internal static readonly string[] MetadataDirectories = ["analysis", "wiki", "narration/voices"];

    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Contains('\\'))
            throw new InvalidDataException("Invalid shared project path.");
        var parts = path.Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(character => character < 32 || "<>:\"|?*".Contains(character)) || Reserved.Contains(part.Split('.')[0]))
                throw new InvalidDataException("Unsafe shared project path.");
            if (part.StartsWith('.') && part is not ".novalist" and not ".book" and not ".nvchapter.json")
                throw new InvalidDataException("Private project files cannot be shared.");
        }
        if (parts.Contains(".novalist") && path != ".novalist" && path != ".novalist/narration" &&
            !MetadataFiles.Any(file => path == ".novalist/" + file) &&
            !MetadataDirectories.Any(directory => path == ".novalist/" + directory || path.StartsWith(".novalist/" + directory + '/', StringComparison.Ordinal)))
            throw new InvalidDataException("Private project metadata cannot be shared.");
    }

    public static string Resolve(string root, string relative)
    {
        Validate(relative);
        var path = Path.GetFullPath(root);
        RejectLink(path);
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            RejectLink(path);
        }
        return path;
    }

    private static void RejectLink(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || ((info.Exists || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Shared project paths cannot follow symbolic links.");
    }
}
