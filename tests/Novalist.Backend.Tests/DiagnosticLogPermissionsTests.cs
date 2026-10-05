using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class DiagnosticLogPermissionsTests
{
    [Fact]
    public void RotationFailureKeepsAppendingToAWritableLogInAReadOnlyUnixDirectory()
    {
        if (OperatingSystem.IsWindows()) return;

        using var directory = new TempDir();
        var sink = new LogFileSink(directory.Combine("logs"));
        sink.Write("before rotation");
        File.AppendAllText(sink.CurrentLogPath, new string('x', 5 * 1024 * 1024));
        var originalMode = File.GetUnixFileMode(sink.Directory);
        File.SetUnixFileMode(sink.Directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            sink.Write("after blocked rotation");

            var content = DiagnosticLogSnapshot.Read(sink.CurrentLogPath);
            Assert.Contains("before rotation", content);
            Assert.Contains("after blocked rotation", content);
            Assert.Single(Directory.GetFiles(sink.Directory, "*.log"));
        }
        finally
        {
            File.SetUnixFileMode(sink.Directory, originalMode);
        }
    }
}
