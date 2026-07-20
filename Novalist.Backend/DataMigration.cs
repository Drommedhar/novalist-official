namespace Novalist.Backend;

/// <summary>
/// One-time migration of the pre-unification data location. The .NET default
/// (<c>ApplicationData/Novalist</c>, i.e. <c>~/.config/Novalist</c> on macOS)
/// differs from Electron's <c>userData</c> (<c>~/Library/Application Support/
/// Novalist</c>), which the app now uses as the single data root. When the
/// target root is fresh, copies the legacy directory into it so existing
/// settings and installed extensions are preserved. No-op on Windows/Linux where
/// the two locations coincide.
/// </summary>
public static class DataMigration
{
    public static void MigrateLegacyIfNeeded(string? targetRoot, string? legacyRoot = null)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
            return;

        legacyRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        var target = Path.GetFullPath(targetRoot);
        var legacy = Path.GetFullPath(legacyRoot);

        if (string.Equals(target, legacy, StringComparison.Ordinal))
            return;                                  // same location (Windows/Linux)
        if (!Directory.Exists(legacy))
            return;                                  // no legacy data to migrate

        // Only migrate into a target the backend has not populated yet, so we
        // never overwrite live settings or a real extensions install.
        if (File.Exists(Path.Combine(target, "settings.json")))
            return;
        var targetExts = Path.Combine(target, "Extensions");
        if (Directory.Exists(targetExts) && Directory.EnumerateFileSystemEntries(targetExts).Any())
            return;

        CopyRecursive(legacy, target);
    }

    private static void CopyRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: true);
    }
}
