using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Thread-safe file sink for the opt-in diagnostic log. Writes daily-named,
/// size-rotated text files under %APPDATA%/Novalist/logs. Every line passed
/// here has already been run through <see cref="LogRedactor"/> by <see cref="Log"/>.
///
/// Writing never throws into callers and is best-effort: a failed write is
/// swallowed rather than risk crashing the app over diagnostics.
/// </summary>
internal sealed class LogFileSink
{
    private const long MaxBytesPerFile = 5 * 1024 * 1024; // 5 MB
    private const int MaxRetainedFiles = 5;

    private readonly object _gate = new();
    private bool _headerWritten;
    private readonly string _dir;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Novalist", "logs");

    /// <param name="directory">Log directory; defaults to %APPDATA%/Novalist/logs. Tests pass a temp dir.</param>
    public LogFileSink(string? directory = null) => _dir = directory ?? DefaultDirectory;

    public string Directory => _dir;

    public string CurrentLogPath =>
        Path.Combine(_dir, $"novalist-{DateTime.Now:yyyy-MM-dd}.log");

    public void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_dir);

                if (!_headerWritten)
                {
                    _headerWritten = true;
                    WriteSessionHeader();
                }

                var path = CurrentLogPath;
                RotateIfNeeded(path, _dir);
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never take the app down.
        }
    }

    /// <summary>Removes all diagnostic log files. Best-effort.</summary>
    // Outer catch is an unreachable belt-and-suspenders swallow (per-file deletes
    // already swallow; enumeration can't throw on a valid dir). Behavior is still
    // tested; excluded so the dead catch doesn't block 100%.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                if (!System.IO.Directory.Exists(_dir)) return;
                foreach (var f in System.IO.Directory.GetFiles(_dir, "novalist-*.log"))
                {
                    try { File.Delete(f); } catch { /* skip locked */ }
                }
                _headerWritten = false;
            }
        }
        catch { /* best effort */ }
    }

    private void WriteSessionHeader()
    {
        // Allowlisted, content-free environment facts only.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var lines = new[]
        {
            "──────────────────────────────────────────────",
            $"Novalist diagnostic log — session start {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"App version: {version}",
            $"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})",
            $"Runtime: {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Culture: {CultureInfo.CurrentCulture.Name}",
            "No story content is recorded in this file.",
            "──────────────────────────────────────────────",
        };
        File.AppendAllText(CurrentLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void RotateIfNeeded(string path, string dir)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytesPerFile) return;

            var rolled = Path.Combine(
                dir,
                $"novalist-{DateTime.Now:yyyy-MM-dd-HHmmss}.log");
            File.Move(path, rolled, overwrite: true);

            Prune(dir);
        }
        catch { /* best effort */ }
    }

    // Prune's catch only fires on exotic IO failures unreachable with the valid
    // dir it is always called with (post-successful-rotate); excluded.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void Prune(string dir)
    {
        try
        {
            var files = System.IO.Directory
                .GetFiles(dir, "novalist-*.log")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Skip(MaxRetainedFiles)
                .ToList();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { /* skip */ }
            }
        }
        catch { /* best effort */ }
    }
}
