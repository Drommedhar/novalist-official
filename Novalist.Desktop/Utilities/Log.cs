using System;
using System.Diagnostics;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Lightweight log facade. Debug/Info go to the trace listener (visible in
/// a debugger) and additionally to stderr when NOVALIST_VERBOSE=1. Without
/// that flag, Release builds stay quiet — Debug.WriteLine is a no-op there
/// because of [Conditional("DEBUG")], so prior to this gate, every Log.Debug
/// call simply vanished in shipped binaries.
///
/// When the user opts in (Settings → Diagnostics), every line is also written
/// to a rotating text file under %APPDATA%/Novalist/logs. That file is
/// content-safe: callers must only pass structured, non-content data
/// (allowlist), and every line is additionally scrubbed by <see cref="LogRedactor"/>
/// as a backstop before it is written. See CLAUDE.md "Diagnostic log must never
/// contain story content".
/// </summary>
public static class Log
{
    // Internal seams for tests: route the sink to a temp dir and toggle verbose
    // output without depending on a process-start environment variable.
    internal static bool Verbose { get; set; } =
        Environment.GetEnvironmentVariable("NOVALIST_VERBOSE") == "1";

    internal static LogFileSink? SinkOverride { get; set; }

    private static LogFileSink? _sink;
    private static volatile bool _fileEnabled;

    private static LogFileSink Sink => SinkOverride ?? (_sink ??= new LogFileSink());

    /// <summary>Directory where diagnostic logs live (whether or not enabled).</summary>
    public static string LogDirectory => LogFileSink.DefaultDirectory;

    /// <summary>Path of the current day's diagnostic log file.</summary>
    public static string CurrentLogPath => Sink.CurrentLogPath;

    /// <summary>
    /// Turns the diagnostic file sink on or off. Live — no restart required.
    /// </summary>
    public static void EnableFileLogging(bool enabled)
    {
        if (enabled)
            _ = Sink;
        _fileEnabled = enabled;
    }

    /// <summary>Deletes all diagnostic log files.</summary>
    public static void ClearLogFiles() => Sink.Clear();

    private static void ToFile(string line)
    {
        if (_fileEnabled)
            Sink.Write(LogRedactor.Scrub(line));
    }

    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        if (Verbose) Console.Error.WriteLine(message);
        ToFile(message);
    }

    public static void Info(string message)
    {
        var line = $"[INFO] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        if (Verbose) Console.Error.WriteLine(line);
        ToFile(line);
    }

    public static void Warn(string message)
    {
        var line = $"[WARN] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.Error.WriteLine(line);
        ToFile(line);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var line = ex == null ? $"[ERROR] {message}" : $"[ERROR] {message} :: {ex}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.Error.WriteLine(line);
        ToFile(line);
    }
}
