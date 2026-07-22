using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class UnavailableProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReportsNonZeroExitWithMessage_AndNoOutput()
    {
        var runner = new UnavailableProcessRunner();

        var (exit, output, error) = await runner.RunAsync("git", null, "--version");

        Assert.NotEqual(0, exit);
        Assert.Equal(string.Empty, output);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public async Task RunAsync_Cancellable_ReportsNonZeroExitWithMessage()
    {
        var runner = new UnavailableProcessRunner();

        var (exit, output, error) =
            await runner.RunAsync("git", "/tmp", CancellationToken.None, "status");

        Assert.NotEqual(0, exit);
        Assert.Equal(string.Empty, output);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
