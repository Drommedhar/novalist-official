using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class DiagnosticLogTests : IDisposable
{
    private readonly bool _originalVerbose = Log.Verbose;

    public void Dispose()
    {
        Log.EnableFileLogging(false);
        Log.SetSinkOverride(null);
        Log.Verbose = _originalVerbose;
    }

    [Fact]
    public void FileSink_WritesHeader_Appends_ClearsAndRestarts()
    {
        using var root = new TempDir();
        var sink = new LogFileSink(root.Combine("logs"));

        sink.Write("first line");
        sink.Write("second line");

        Assert.Equal(root.Combine("logs"), sink.Directory);
        var content = File.ReadAllText(sink.CurrentLogPath);
        Assert.Contains("session start", content);
        Assert.Contains("No story content", content);
        Assert.Contains("first line", content);
        Assert.Contains("second line", content);

        Assert.Equal(1, sink.Clear());
        Assert.False(File.Exists(sink.CurrentLogPath));
        sink.Write("after clear");
        Assert.Contains("session start", File.ReadAllText(sink.CurrentLogPath));
    }

    [Fact]
    public void FileSink_MissingOrInvalidDirectories_AreBestEffort()
    {
        using var root = new TempDir();
        Assert.Equal(LogFileSink.DefaultDirectory, new LogFileSink().Directory);
        var missing = new LogFileSink(root.Combine("missing"));
        Assert.Equal(0, missing.Clear());

        var invalid = new LogFileSink("bad\0dir");
        invalid.Write("not written");
        Assert.Equal(0, invalid.Clear());
    }

    [Fact]
    public void FileSink_RotatesAnOversizeLog()
    {
        using var root = new TempDir();
        var sink = new LogFileSink(root.Combine("logs"));
        Directory.CreateDirectory(sink.Directory);
        File.WriteAllBytes(sink.CurrentLogPath, new byte[5 * 1024 * 1024 + 1]);
        for (var index = 0; index < 8; index++)
        {
            var old = root.Combine("logs", $"novalist-2020-01-{index + 1:D2}.log");
            File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, index + 1));
        }

        sink.Write("after rotation");

        Assert.Contains("after rotation", File.ReadAllText(sink.CurrentLogPath));
        Assert.True(Directory.GetFiles(sink.Directory, "novalist-*.log").Length <= 6);

        if (OperatingSystem.IsWindows())
        {
            File.AppendAllText(sink.CurrentLogPath, new string('x', 5 * 1024 * 1024 + 1));
            using var hold = new FileStream(
                sink.CurrentLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            sink.Write("rotation temporarily blocked");
        }
    }

    [Fact]
    public void Facade_WritesEveryLevelOnlyWhenEnabled()
    {
        using var root = new TempDir();
        Log.SetSinkOverride(new LogFileSink(root.Combine("logs")));
        Log.Verbose = true;
        Log.EnableFileLogging(true);

        Log.Debug("debug state=ready");
        Log.Info("info count=1");
        Log.Warn("warn type=Example");
        Log.Error("error state=failed");
        Log.Error("error with type", new InvalidOperationException("private manuscript sentence"));

        Assert.Equal(root.Combine("logs"), Log.LogDirectory);
        var content = File.ReadAllText(Log.CurrentLogPath);
        Assert.Contains("debug state=ready", content);
        Assert.Contains("[INFO] info count=1", content);
        Assert.Contains("[WARN] warn type=Example", content);
        Assert.Contains("[ERROR] error state=failed", content);
        Assert.Contains("type=System.InvalidOperationException", content);
        Assert.DoesNotContain("private manuscript sentence", content);

        Assert.Equal(1, Log.ClearLogFiles());
        Log.EnableFileLogging(false);
        Log.Info("must remain off");
        Assert.False(File.Exists(Log.CurrentLogPath));
    }

    [Fact]
    public void Configure_RestoresPersistedOptIn_AndDefaultsOffSafely()
    {
        using var root = new TempDir();
        File.WriteAllText(root.Combine("settings.json"), """{"diagnosticLoggingEnabled":true}""");

        Log.Configure(root.Path);
        Log.Info("startup state=ready");

        Assert.Equal(root.Combine("logs"), Log.LogDirectory);
        Assert.True(File.Exists(Log.CurrentLogPath));

        using var off = new TempDir();
        File.WriteAllText(off.Combine("settings.json"), """{"language":"en"}""");
        Log.Configure(off.Path);
        Log.Info("not opted in");
        Assert.False(Directory.Exists(off.Combine("logs")));

        using var damaged = new TempDir();
        File.WriteAllText(damaged.Combine("settings.json"), "{");
        Log.Configure(damaged.Path);
        Log.Info("damaged settings");
        Assert.False(Directory.Exists(damaged.Combine("logs")));

        using var missing = new TempDir();
        Log.Configure(missing.Path);
        Log.Info("missing settings");
        Assert.False(Directory.Exists(missing.Combine("logs")));
    }

    [Theory]
    [InlineData(@"Opened C:\Users\jane\My Novel\scene.json now", "jane", "<path>.json")]
    [InlineData(@"file at \\server\share\book\ch1.novalist", "book", "<path>")]
    [InlineData("read /home/user/Project/scene.txt done", "Project", "<path>.txt")]
    [InlineData("nav file:///C:/secret/book.json end", "secret", "<path>")]
    public void Redactor_RemovesPaths(string input, string privatePart, string marker)
    {
        var result = LogRedactor.ScrubCore(input);
        Assert.DoesNotContain(privatePart, result);
        Assert.Contains(marker, result);
    }

    [Fact]
    public void Redactor_DropsBarePathsAndLongTokens_ButLeavesSafeLines()
    {
        var bare = LogRedactor.ScrubCore(@"dir C:\Users\jane\SecretFolder here");
        Assert.DoesNotContain("SecretFolder", bare);
        Assert.Contains("<path>", bare);

        var blob = new string('x', 200);
        var longToken = LogRedactor.ScrubCore($"data {blob} end");
        Assert.Contains("<redacted:200>", longToken);
        Assert.DoesNotContain(blob, longToken);

        const string safe = "Loaded project state=Ready count=5 id=abc-123";
        Assert.Equal(safe, LogRedactor.ScrubCore(safe));
        Assert.Equal(string.Empty, LogRedactor.Scrub(string.Empty));
    }
}
