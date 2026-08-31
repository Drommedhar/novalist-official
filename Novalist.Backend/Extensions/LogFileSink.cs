using System.Globalization;
using System.Reflection;

namespace Novalist.Backend.Extensions;

/// <summary>Thread-safe, size-rotated sink for the opt-in diagnostic log.</summary>
internal sealed class LogFileSink
{
    private const long MaxBytesPerFile = 5 * 1024 * 1024;
    private const int MaxRetainedFiles = 5;

    private readonly object _gate = new();
    private readonly string _directory;
    private bool _headerWritten;

    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Novalist",
        "logs");

    internal LogFileSink(string? directory = null) =>
        _directory = directory ?? DefaultDirectory;

    internal string Directory => _directory;
    internal string CurrentLogPath =>
        Path.Combine(_directory, $"novalist-{DateTime.Now:yyyy-MM-dd}.log");

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                if (!_headerWritten)
                {
                    _headerWritten = true;
                    WriteSessionHeader();
                }

                var path = CurrentLogPath;
                RotateIfNeeded(path, _directory);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics are best-effort and must never take down the app.
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal int Clear()
    {
        try
        {
            lock (_gate)
            {
                if (!System.IO.Directory.Exists(_directory)) return 0;
                var count = 0;
                foreach (var file in System.IO.Directory.GetFiles(_directory, "*.log"))
                {
                    try
                    {
                        File.Delete(file);
                        count++;
                    }
                    catch
                    {
                        // Skip a file held by another process.
                    }
                }
                _headerWritten = false;
                return count;
            }
        }
        catch
        {
            return 0;
        }
    }

    private void WriteSessionHeader()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var lines = new[]
        {
            "----------------------------------------------",
            $"Novalist diagnostic log - session start {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"App version: {version}",
            $"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})",
            $"Runtime: {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Culture: {CultureInfo.CurrentCulture.Name}",
            "No story content is recorded in this file.",
            "----------------------------------------------"
        };
        File.AppendAllText(CurrentLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void RotateIfNeeded(string path, string directory)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytesPerFile) return;

            var rolled = Path.Combine(
                directory,
                $"novalist-{DateTime.Now:yyyy-MM-dd-HHmmss}.log");
            File.Move(path, rolled, overwrite: true);
            Prune(directory);
        }
        catch
        {
            // Keep appending if rotation is temporarily unavailable.
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void Prune(string directory)
    {
        try
        {
            var files = System.IO.Directory
                .GetFiles(directory, "novalist-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(MaxRetainedFiles);
            foreach (var file in files)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Retention is best-effort.
        }
    }
}
