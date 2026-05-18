using System;
using System.Diagnostics;
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
            var psi = new ProcessStartInfo("ldconfig", "-p")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                if (output.Contains(WebKitSoName, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { /* fall through to filesystem probe */ }

        string[] paths =
        {
            $"/usr/lib/x86_64-linux-gnu/{WebKitSoName}",
            $"/usr/lib64/{WebKitSoName}",
            $"/usr/lib/{WebKitSoName}",
            $"/lib/x86_64-linux-gnu/{WebKitSoName}",
        };
        return paths.Any(File.Exists);
    }

    public static bool IsPkexecAvailable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, "pkexec")))
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

        var psi = new ProcessStartInfo("pkexec")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new InstallResult(false,
                $"Could not invoke pkexec: {ex.Message}. " +
                "Install polkit, or run the printed command in a terminal as root.");
        }

        if (proc is null)
            return new InstallResult(false, "pkexec failed to start.");

        using (proc)
        {
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) output?.Report(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) output?.Report(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new InstallResult(false, "Cancelled.");
            }

            if (proc.ExitCode == 0)
                return new InstallResult(true, "Installation complete.");

            // pkexec exit code 126 = auth dismissed/declined, 127 = auth could not be obtained.
            var reason = proc.ExitCode switch
            {
                126 => "Authentication was dismissed.",
                127 => "Authentication failed.",
                _   => $"Install command exited with code {proc.ExitCode}.",
            };
            return new InstallResult(false, reason);
        }
    }

    private static (LinuxDistro distro, string name) DetectDistro()
    {
        const string path = "/etc/os-release";
        if (!File.Exists(path)) return (LinuxDistro.Unknown, "Unknown Linux");

        string id = string.Empty, idLike = string.Empty, pretty = "Linux";
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("ID=", StringComparison.Ordinal))
                    id = TrimQuotes(line[3..]);
                else if (line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                    idLike = TrimQuotes(line[8..]);
                else if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    pretty = TrimQuotes(line[12..]);
            }
        }
        catch { return (LinuxDistro.Unknown, "Unknown Linux"); }

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

    private static (string command, string package) GetInstallInfo(LinuxDistro distro) => distro switch
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
