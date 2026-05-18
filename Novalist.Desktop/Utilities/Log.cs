using System;
using System.Diagnostics;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Lightweight log facade. Debug/Info go to the trace listener (visible in
/// a debugger) and additionally to stderr when NOVALIST_VERBOSE=1. Without
/// that flag, Release builds stay quiet — Debug.WriteLine is a no-op there
/// because of [Conditional("DEBUG")], so prior to this gate, every Log.Debug
/// call simply vanished in shipped binaries.
/// </summary>
public static class Log
{
    private static readonly bool _verbose =
        Environment.GetEnvironmentVariable("NOVALIST_VERBOSE") == "1";

    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        if (_verbose) Console.Error.WriteLine(message);
    }

    public static void Info(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
        if (_verbose) Console.Error.WriteLine($"[INFO] {message}");
    }

    public static void Warn(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[WARN] {message}");
        Console.Error.WriteLine($"[WARN] {message}");
    }

    public static void Error(string message, Exception? ex = null)
    {
        var line = ex == null ? $"[ERROR] {message}" : $"[ERROR] {message} :: {ex}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.Error.WriteLine(line);
    }
}
