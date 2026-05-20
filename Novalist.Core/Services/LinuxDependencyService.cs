using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Core.Services;

public enum LinuxDistro
{
    Unknown,
    Debian,
    Fedora,
    Arch,
    OpenSuse,
    Void,
    Alpine,
}

public sealed record LinuxDependencyInfo(
    LinuxDistro Distro,
    string DistroName,
    bool WebKitInstalled,
    string InstallCommand,
    string PackageName);

public sealed record InstallResult(bool Success, string Message);

public static class LinuxDependencyService
{
    private const string WebKitSoName = "libwebkit2gtk-4.1.so.0";

    private static readonly string[] WebKitProbePaths =
    {
        $"/usr/lib/x86_64-linux-gnu/{WebKitSoName}",
        $"/usr/lib64/{WebKitSoName}",
        $"/usr/lib/{WebKitSoName}",
        $"/lib/x86_64-linux-gnu/{WebKitSoName}",
    };

    // Seams (defaults hit the real OS; tests swap them). Reset after each test.
    internal static IProcessRunner ProcessRunner { get; set; } = new ProcessRunner();
    internal static Func<string, bool> FileExists { get; set; } = File.Exists;
    internal static Func<string?> ReadOsRelease { get; set; } = () => ReadOsReleaseFrom("/etc/os-release");
    internal static Func<string> GetPathEnv { get; set; } =
        () => Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

    public static LinuxDependencyInfo Detect()
    {
        var (distro, name) = DetectDistro();
        var installed = IsWebKitInstalled();
        var (cmd, pkg) = GetInstallInfo(distro);
        return new LinuxDependencyInfo(distro, name, installed, cmd, pkg);
    }

    public static bool IsWebKitInstalled()
    {
        // ldconfig answers "does the dynamic linker know about this SONAME"
        // which is exactly what dlopen would consult, so it's the most
        // reliable check across distros and arches.
        try
        {
            var (_, output, _) = ProcessRunner.RunAsync("ldconfig", null, "-p").GetAwaiter().GetResult();
            if (output.Contains(WebKitSoName, StringComparison.Ordinal))
                return true;
        }
        catch { /* fall through to filesystem probe */ }

        return WebKitProbePaths.Any(FileExists);
    }

    public static bool IsPkexecAvailable()
    {
        foreach (var dir in GetPathEnv().Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (FileExists(Path.Combine(dir, "pkexec")))
                return true;
        }
        return false;
    }

    public static async Task<InstallResult> InstallAsync(
        string command,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new InstallResult(false, "No install command available for this distribution.");

        int exitCode;
        string captured;
        try
        {
            var result = await ProcessRunner.RunAsync("pkexec", null, ct, "sh", "-c", command);
            exitCode = result.ExitCode;
            captured = string.Join('\n', new[] { result.Output, result.Error }
                .Where(s => !string.IsNullOrEmpty(s)));
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            return new InstallResult(false,
                $"Could not invoke pkexec: {ex.Message}. " +
                "Install polkit, or run the printed command in a terminal as root.");
        }

        foreach (var line in captured.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            output?.Report(line);

        if (exitCode == 0)
            return new InstallResult(true, "Installation complete.");

        // pkexec exit code 126 = auth dismissed/declined, 127 = auth could not be obtained.
        var reason = exitCode switch
        {
            126 => "Authentication was dismissed.",
            127 => "Authentication failed.",
            _ => $"Install command exited with code {exitCode}.",
        };
        return new InstallResult(false, reason);
    }

    internal static string? ReadOsReleaseFrom(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            // Missing or unreadable — treated as "no distro info".
            return null;
        }
    }

    private static (LinuxDistro distro, string name) DetectDistro()
    {
        var content = ReadOsRelease();
        return content == null ? (LinuxDistro.Unknown, "Unknown Linux") : ParseOsRelease(content);
    }

    internal static (LinuxDistro distro, string name) ParseOsRelease(string content)
    {
        string id = string.Empty, idLike = string.Empty, pretty = "Linux";
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim('\r');
            if (line.StartsWith("ID=", StringComparison.Ordinal))
                id = TrimQuotes(line[3..]);
            else if (line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                idLike = TrimQuotes(line[8..]);
            else if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                pretty = TrimQuotes(line[12..]);
        }

        var hay = (id + " " + idLike).ToLowerInvariant();
        if (hay.Contains("debian") || hay.Contains("ubuntu") || hay.Contains("mint") || hay.Contains("pop"))
            return (LinuxDistro.Debian, pretty);
        if (hay.Contains("fedora") || hay.Contains("rhel") || hay.Contains("centos") || hay.Contains("nobara"))
            return (LinuxDistro.Fedora, pretty);
        if (hay.Contains("arch") || hay.Contains("manjaro") || hay.Contains("cachyos") || hay.Contains("endeavouros"))
            return (LinuxDistro.Arch, pretty);
        if (hay.Contains("opensuse") || hay.Contains("suse"))
            return (LinuxDistro.OpenSuse, pretty);
        if (hay.Contains("void"))
            return (LinuxDistro.Void, pretty);
        if (hay.Contains("alpine"))
            return (LinuxDistro.Alpine, pretty);
        return (LinuxDistro.Unknown, pretty);
    }

    private static string TrimQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0])
            return s[1..^1];
        return s;
    }

    internal static (string command, string package) GetInstallInfo(LinuxDistro distro) => distro switch
    {
        LinuxDistro.Debian =>
            ("apt-get update && apt-get install -y libwebkit2gtk-4.1-0",
             "libwebkit2gtk-4.1-0"),
        LinuxDistro.Fedora =>
            ("dnf install -y webkit2gtk4.1",
             "webkit2gtk4.1"),
        LinuxDistro.Arch =>
            ("pacman -Sy --noconfirm webkit2gtk-4.1",
             "webkit2gtk-4.1"),
        LinuxDistro.OpenSuse =>
            ("zypper install -y libwebkit2gtk-4_1-0",
             "libwebkit2gtk-4_1-0"),
        LinuxDistro.Void =>
            ("xbps-install -Sy webkit2gtk",
             "webkit2gtk"),
        LinuxDistro.Alpine =>
            ("apk add webkit2gtk-4.1",
             "webkit2gtk-4.1"),
        _ => (string.Empty, WebKitSoName),
    };
}
