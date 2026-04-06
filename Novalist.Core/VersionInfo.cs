using System.Reflection;

namespace Novalist.Core;

/// <summary>
/// Provides the application version derived from the assembly metadata.
/// </summary>
public static class VersionInfo
{
    private static readonly Lazy<string> _version = new(ReadVersion);

    /// <summary>
    /// Semantic version string (e.g. "1.2.0" or "0.0.1-dev").
    /// </summary>
    public static string Version => _version.Value;

    /// <summary>
    /// Returns <c>true</c> when running a local/dev build (version suffix "dev").
    /// </summary>
    public static bool IsDev => Version.Contains("-dev", StringComparison.Ordinal);

    /// <summary>
    /// Compares the host version against a minimum version string (e.g. from an extension manifest).
    /// Returns <c>true</c> when the host version is equal to or newer than <paramref name="minVersion"/>.
    /// Pre-release suffixes are ignored for the comparison.
    /// </summary>
    public static bool IsCompatibleWith(string minVersion)
    {
        if (string.IsNullOrWhiteSpace(minVersion))
            return true;

        var hostParts = ParseVersionParts(Version);
        var requiredParts = ParseVersionParts(minVersion);

        for (var i = 0; i < 3; i++)
        {
            var h = i < hostParts.Length ? hostParts[i] : 0;
            var r = i < requiredParts.Length ? requiredParts[i] : 0;
            if (h > r) return true;
            if (h < r) return false;
        }

        return true; // equal
    }

    private static string ReadVersion()
    {
        var attr = typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attr?.InformationalVersion is { Length: > 0 } ver)
        {
            // Strip the git hash appended by the SDK (e.g. "1.2.0+abc123")
            var plusIndex = ver.IndexOf('+');
            return plusIndex >= 0 ? ver[..plusIndex] : ver;
        }

        return typeof(VersionInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static int[] ParseVersionParts(string version)
    {
        // Strip pre-release suffix (e.g. "-dev", "-beta.1") and git hash
        var dashIndex = version.IndexOf('-');
        if (dashIndex >= 0) version = version[..dashIndex];
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0) version = version[..plusIndex];

        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }
}
