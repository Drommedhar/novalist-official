using System.Reflection;
using System.Text.Json;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class SettingsRpc
{
    /// <summary>Reports the diagnostic-log directory and newest log file (if any).</summary>
    [JsonRpcMethod("settings/logInfo")]
    public LogInfoDto LogInfo()
    {
        Directory.CreateDirectory(LogsDirectory);
        return ResolveLogInfo(LogsDirectory);
    }

    /// <summary>Deletes every <c>*.log</c> file in the diagnostic-log directory.</summary>
    [JsonRpcMethod("settings/clearLogs")]
    public int ClearLogs()
    {
        Log.SetDirectory(LogsDirectory);
        return Log.ClearLogFiles();
    }

    /// <summary>Test seam: overrides the diagnostic-log directory (null = workspace data root).</summary>
    internal static string? LogsDirectoryOverride { get; set; }

    /// <summary>Diagnostic-log directory, matching the desktop shell's convention.</summary>
    internal string LogsDirectory => LogsDirectoryOverride
        ?? Path.Combine(_workspace.SettingsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Novalist"),
            "logs");

    internal static LogInfoDto ResolveLogInfo(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new LogInfoDto(directory, null);
        }
        var newest = new DirectoryInfo(directory)
            .EnumerateFiles("*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return new LogInfoDto(directory, newest?.FullName);
    }

    internal static int ClearLogFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
        {
            File.Delete(file);
            count++;
        }
        return count;
    }
}
