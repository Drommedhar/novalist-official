using System;
using System.Diagnostics;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Lightweight log facade. All sites that previously called
/// <c>Console.WriteLine</c> / <c>Debug.WriteLine</c> / <c>Console.Error.WriteLine</c>
/// route here so the implementation can change later without churn.
/// </summary>
public static class Log
{
    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }

    public static void Info(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
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
