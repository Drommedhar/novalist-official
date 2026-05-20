using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Utilities;
using Xunit;

namespace Novalist.Desktop.Tests.Utilities;

public class LogFileSinkTests
{
    [Fact]
    public void Write_CreatesFileWithHeader_ThenAppends()
    {
        using var dir = new TempDir();
        var sink = new LogFileSink(dir.Path);
        sink.Write("first line");
        sink.Write("second line");

        Assert.Equal(dir.Path, sink.Directory);
        var content = File.ReadAllText(sink.CurrentLogPath);
        Assert.Contains("session start", content);   // header written once
        Assert.Contains("No story content", content);
        Assert.Contains("first line", content);
        Assert.Contains("second line", content);
    }

    [Fact]
    public void Clear_DeletesLogs_AndResetsHeader()
    {
        using var dir = new TempDir();
        var sink = new LogFileSink(dir.Path);
        sink.Write("x");
        Assert.True(File.Exists(sink.CurrentLogPath));

        sink.Clear();
        Assert.False(File.Exists(sink.CurrentLogPath));

        // Header re-written after a clear.
        sink.Write("y");
        Assert.Contains("session start", File.ReadAllText(sink.CurrentLogPath));
    }

    [Fact]
    public void Clear_MissingDirectory_NoOp()
    {
        using var dir = new TempDir();
        var sub = Path.Combine(dir.Path, "nope");
        new LogFileSink(sub).Clear(); // no throw
    }

    [Fact]
    public void Write_InvalidDirectory_SwallowsError()
        => new LogFileSink("bad\0dir").Write("x"); // CreateDirectory throws -> Write catch, no throw

    [Fact]
    public void Write_RotateFailure_Swallowed()
    {
        if (!OperatingSystem.IsWindows())
            return; // file locks don't block Move the same way on Unix
        using var dir = new TempDir();
        var sink = new LogFileSink(dir.Path);
        sink.Write("seed"); // writes the session header first (so it's not re-written under lock)
        File.AppendAllText(sink.CurrentLogPath, new string('x', 5 * 1024 * 1024 + 10));
        // Exclusive lock so the rotation File.Move throws -> Rotate catch swallows it.
        using var hold = new FileStream(sink.CurrentLogPath, FileMode.Open, FileAccess.Read, FileShare.None);
        sink.Write("trigger"); // no throw despite failed rotate
    }

    [Fact]
    public void Write_RotatesOversizeFile_AndPrunesOld()
    {
        using var dir = new TempDir();
        var sink = new LogFileSink(dir.Path);
        // Pre-create the current day's file at >5MB so the next write rotates it.
        var current = sink.CurrentLogPath;
        File.WriteAllBytes(current, new byte[5 * 1024 * 1024 + 10]);
        // Plenty of old rolled files so pruning (keep 5) runs.
        for (int i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir.Path, $"novalist-2020-01-{i + 1:D2}.log");
            File.WriteAllText(p, "old");
            File.SetLastWriteTimeUtc(p, new DateTime(2020, 1, i + 1));
        }

        sink.Write("trigger rotation");

        var remaining = Directory.GetFiles(dir.Path, "novalist-*.log").Length;
        Assert.True(remaining <= 7); // pruned down (keeps newest 5 + rolled + new current)
        Assert.Contains("trigger rotation", File.ReadAllText(sink.CurrentLogPath));
    }
}

public class LogFacadeTests : IDisposable
{
    private readonly bool _origVerbose = Log.Verbose;

    public void Dispose()
    {
        Log.Verbose = _origVerbose;
        Log.SinkOverride = null;
        Log.EnableFileLogging(false);
    }

    [Fact]
    public void AllLevels_WriteToFile_WhenEnabled()
    {
        using var dir = new TempDir();
        Log.SinkOverride = new LogFileSink(dir.Path);
        Log.Verbose = true; // exercise the verbose Console.Error branches too
        Log.EnableFileLogging(true);

        Log.Debug("debug msg");
        Log.Info("info msg");
        Log.Warn("warn msg");
        Log.Error("error msg", new InvalidOperationException("boom"));

        var content = File.ReadAllText(Log.CurrentLogPath);
        Assert.Contains("debug msg", content);
        Assert.Contains("[INFO] info msg", content);
        Assert.Contains("[WARN] warn msg", content);
        Assert.Contains("[ERROR] error msg", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Disabled_DoesNotWrite()
    {
        using var dir = new TempDir();
        Log.SinkOverride = new LogFileSink(dir.Path);
        Log.EnableFileLogging(false);
        Log.Debug("should not be written");
        Assert.False(File.Exists(Log.CurrentLogPath));
    }

    [Fact]
    public void NotVerbose_StillWritesFileAndDebug()
    {
        using var dir = new TempDir();
        Log.SinkOverride = new LogFileSink(dir.Path);
        Log.Verbose = false;
        Log.EnableFileLogging(true);
        Log.Debug("quiet");
        Log.Info("quiet info");
        Assert.Contains("quiet", File.ReadAllText(Log.CurrentLogPath));
    }

    [Fact]
    public void ClearLogFiles_Works()
    {
        using var dir = new TempDir();
        Log.SinkOverride = new LogFileSink(dir.Path);
        Log.EnableFileLogging(true);
        Log.Warn("something");
        Assert.True(File.Exists(Log.CurrentLogPath));
        Log.ClearLogFiles();
        Assert.False(File.Exists(Log.CurrentLogPath));
    }

    [Fact]
    public void LogDirectory_IsDefault()
        => Assert.Equal(LogFileSink.DefaultDirectory, Log.LogDirectory);
}
