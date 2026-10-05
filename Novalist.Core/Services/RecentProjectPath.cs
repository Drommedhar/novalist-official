using System.Runtime.InteropServices;

namespace Novalist.Core.Services;

/// <summary>Identifies recent projects using the case policy of their filesystem.</summary>
internal static class RecentProjectPath
{
    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // Backslashes remain legal filename characters on Unix.
        var text = path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        text = OperatingSystem.IsWindows() ? RemoveLeadingDriveSeparator(text, Path.DirectorySeparatorChar) : text;
        try
        {
            text = Path.GetFullPath(text);
        }
        catch (ArgumentException)
        {
            // Preserve invalid entries without passing embedded nulls to native code.
            text = text.TrimEnd(Path.DirectorySeparatorChar);
            return OperatingSystem.IsWindows() ? text.ToLowerInvariant() : text;
        }

        var ignoreCase = IsCaseInsensitive(text);
        text = Path.TrimEndingDirectorySeparator(text);
        return ignoreCase ? text.ToLowerInvariant() : text;
    }

    internal static string RemoveLeadingDriveSeparator(string path, char separator)
    {
        // A separator before a Windows drive letter is never meaningful.
        return path.Length > 2 && path[0] == separator && char.IsLetter(path[1]) && path[2] == ':'
            ? path[1..]
            : path;
    }

    internal static bool IsCaseInsensitive(string path) => OperatingSystem.IsWindows()
        || OperatingSystem.IsMacOS() && !path.Contains('\0') && IsCaseInsensitive(path, QueryVolume);

    internal static bool IsCaseInsensitive(string path, Func<string, (long Value, int Error)> query)
    {
        var current = path;
        while (true)
        {
            var (value, error) = query(current);
            if (value > 0 || value < 0 && error != 2) return false;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) return value == 0;
            // An absent mounted volume must not inherit the host volume's policy.
            if (value < 0 && parent.Equals("/Volumes", StringComparison.OrdinalIgnoreCase))
                return false;
            // ENOENT permits trying an existing ancestor. Also check every known
            // ancestor: the names of insensitive mounts can have sensitive parents.
            current = parent;
        }
    }

    // This adapter invokes Darwin's ABI and cannot execute in Windows/Linux coverage runs.
    // All case policy, error handling, and ancestor traversal remain covered above.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static (long Value, int Error) QueryVolume(string path)
    {
        // Darwin sys/unistd.h: _PC_CASE_SENSITIVE = 11. The call only reads metadata.
        var value = PathConf(path, 11);
        return (value, Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", EntryPoint = "pathconf", SetLastError = true)]
    private static extern nint PathConf([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int name);
}
