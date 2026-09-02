using System.Text.Json;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Content-safe diagnostic logging for the backend. Stderr remains available to
/// the Electron main process; when the writer opts in, the same allowlisted
/// lines are also written to a rotating file after redaction.
/// </summary>
internal static class Log
{
    internal static bool Verbose { get; set; } =
        Environment.GetEnvironmentVariable("NOVALIST_VERBOSE") == "1";

    private static readonly object StateGate = new();
    private static LogFileSink? _sinkOverride;

    private static LogFileSink? _sink;
    private static bool _fileEnabled;

    internal static void SetSinkOverride(LogFileSink? sink)
    {
        lock (StateGate)
            _sinkOverride = sink;
    }

    private static LogFileSink Sink
    {
        get
        {
            lock (StateGate)
                return SinkUnsafe();
        }
    }

    private static LogFileSink SinkUnsafe() => _sinkOverride ?? (_sink ??= new LogFileSink());

    internal static string LogDirectory => Sink.Directory;
    internal static string CurrentLogPath => Sink.CurrentLogPath;

    /// <summary>Points the logger at this host's data root and restores its
    /// persisted opt-in before startup work begins.</summary>
    internal static void Configure(string? settingsDirectory)
    {
        var root = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        var enabled = ReadEnabled(Path.Combine(root, "settings.json"));
        lock (StateGate)
        {
            _sink = new LogFileSink(Path.Combine(root, "logs"));
            _fileEnabled = enabled;
        }
    }

    internal static void SetDirectory(string directory)
    {
        lock (StateGate)
            _sink = new LogFileSink(directory);
    }

    internal static void EnableFileLogging(bool enabled)
    {
        lock (StateGate)
        {
            if (enabled) _ = SinkUnsafe();
            _fileEnabled = enabled;
        }
    }

    internal static int ClearLogFiles() => Sink.Clear();

    private static bool ReadEnabled(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.TryGetProperty("diagnosticLoggingEnabled", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch
        {
            // A damaged settings file must default to private, off-by-default logging.
            return false;
        }
    }

    private static void ToFile(string line)
    {
        LogFileSink? sink;
        lock (StateGate)
        {
            if (!_fileEnabled) return;
            sink = SinkUnsafe();
        }

        sink.Write(LogRedactor.Scrub(line));
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

    public static void Error(string message, Exception? exception = null)
    {
        // Exception.Message and Exception.ToString() routinely contain paths,
        // document titles, snippets of malformed input, and other user data.
        // The diagnostic log only needs the failure's shape; callers add the
        // content-free operation/stage beside it.
        var line = exception == null
            ? $"[ERROR] {message}"
            : $"[ERROR] {message} :: type={exception.GetType().FullName}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.Error.WriteLine(line);
        ToFile(line);
    }
}
