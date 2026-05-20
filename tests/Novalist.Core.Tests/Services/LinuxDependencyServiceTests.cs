using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class LinuxDependencyServiceTests : IDisposable
{
    private readonly IProcessRunner _origRunner = LinuxDependencyService.ProcessRunner;
    private readonly Func<string, bool> _origFileExists = LinuxDependencyService.FileExists;
    private readonly Func<string?> _origReadOsRelease = LinuxDependencyService.ReadOsRelease;
    private readonly Func<string> _origPathEnv = LinuxDependencyService.GetPathEnv;

    public void Dispose()
    {
        LinuxDependencyService.ProcessRunner = _origRunner;
        LinuxDependencyService.FileExists = _origFileExists;
        LinuxDependencyService.ReadOsRelease = _origReadOsRelease;
        LinuxDependencyService.GetPathEnv = _origPathEnv;
    }

    private static FakeProcessRunner NoLdconfig() => new(_ => (0, "", ""));

    [Theory]
    [InlineData("ID=ubuntu", LinuxDistro.Debian)]
    [InlineData("ID=debian", LinuxDistro.Debian)]
    [InlineData("ID=linuxmint", LinuxDistro.Debian)]
    [InlineData("ID=pop", LinuxDistro.Debian)]
    [InlineData("ID=fedora", LinuxDistro.Fedora)]
    [InlineData("ID=rhel", LinuxDistro.Fedora)]
    [InlineData("ID=centos", LinuxDistro.Fedora)]
    [InlineData("ID=nobara", LinuxDistro.Fedora)]
    [InlineData("ID=arch", LinuxDistro.Arch)]
    [InlineData("ID=manjaro", LinuxDistro.Arch)]
    [InlineData("ID=cachyos", LinuxDistro.Arch)]
    [InlineData("ID=endeavouros", LinuxDistro.Arch)]
    [InlineData("ID=opensuse-tumbleweed", LinuxDistro.OpenSuse)]
    [InlineData("ID=suse", LinuxDistro.OpenSuse)]
    [InlineData("ID=void", LinuxDistro.Void)]
    [InlineData("ID=alpine", LinuxDistro.Alpine)]
    [InlineData("ID=plan9", LinuxDistro.Unknown)]
    public void ParseOsRelease_DetectsDistro(string content, LinuxDistro expected)
        => Assert.Equal(expected, LinuxDependencyService.ParseOsRelease(content).distro);

    [Fact]
    public void ParseOsRelease_UsesIdLikeAndPrettyName_AndTrimsQuotes()
    {
        var content = "ID=neon\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME='KDE neon'\r\n";
        var (distro, name) = LinuxDependencyService.ParseOsRelease(content);
        Assert.Equal(LinuxDistro.Debian, distro);   // matched via ID_LIKE
        Assert.Equal("KDE neon", name);             // single-quotes trimmed, \r stripped
    }

    [Fact]
    public void ParseOsRelease_NoPrettyName_DefaultsToLinux()
        => Assert.Equal("Linux", LinuxDependencyService.ParseOsRelease("ID=void").name);

    [Theory]
    [InlineData(LinuxDistro.Debian, "libwebkit2gtk-4.1-0")]
    [InlineData(LinuxDistro.Fedora, "webkit2gtk4.1")]
    [InlineData(LinuxDistro.Arch, "webkit2gtk-4.1")]
    [InlineData(LinuxDistro.OpenSuse, "libwebkit2gtk-4_1-0")]
    [InlineData(LinuxDistro.Void, "webkit2gtk")]
    [InlineData(LinuxDistro.Alpine, "webkit2gtk-4.1")]
    public void GetInstallInfo_PerDistro(LinuxDistro distro, string expectedPackage)
        => Assert.Equal(expectedPackage, LinuxDependencyService.GetInstallInfo(distro).package);

    [Fact]
    public void GetInstallInfo_Unknown_EmptyCommand()
    {
        var (cmd, _) = LinuxDependencyService.GetInstallInfo(LinuxDistro.Unknown);
        Assert.Equal(string.Empty, cmd);
    }

    [Fact]
    public void Detect_AssemblesInfo()
    {
        LinuxDependencyService.ReadOsRelease = () => "ID=fedora\nPRETTY_NAME=Fedora";
        LinuxDependencyService.ProcessRunner = NoLdconfig();
        LinuxDependencyService.FileExists = _ => false;

        var info = LinuxDependencyService.Detect();
        Assert.Equal(LinuxDistro.Fedora, info.Distro);
        Assert.False(info.WebKitInstalled);
        Assert.Equal("webkit2gtk4.1", info.PackageName);
    }

    [Fact]
    public void Detect_NoOsRelease_Unknown()
    {
        LinuxDependencyService.ReadOsRelease = () => null;
        LinuxDependencyService.ProcessRunner = NoLdconfig();
        LinuxDependencyService.FileExists = _ => false;
        Assert.Equal(LinuxDistro.Unknown, LinuxDependencyService.Detect().Distro);
    }

    [Fact]
    public void Detect_DefaultOsReleaseReader_MissingFile_Unknown()
    {
        // Leave ReadOsRelease at its default (reads /etc/os-release). On the
        // Windows test runner that file is absent -> null -> Unknown. Exercises
        // the default seam + ReadOsReleaseFrom's catch path.
        LinuxDependencyService.ProcessRunner = NoLdconfig();
        LinuxDependencyService.FileExists = _ => false;
        Assert.Equal(LinuxDistro.Unknown, LinuxDependencyService.Detect().Distro);
    }

    [Fact]
    public void ReadOsReleaseFrom_ExistingFile_ReturnsContent()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "os-release");
        File.WriteAllText(path, "ID=arch");
        Assert.Equal("ID=arch", LinuxDependencyService.ReadOsReleaseFrom(path));
    }

    [Fact]
    public void ReadOsReleaseFrom_MissingFile_ReturnsNull()
        => Assert.Null(LinuxDependencyService.ReadOsReleaseFrom(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));

    [Fact]
    public void IsWebKitInstalled_LdconfigReportsSoName_True()
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (0, "libwebkit2gtk-4.1.so.0 => /usr/lib", ""));
        Assert.True(LinuxDependencyService.IsWebKitInstalled());
    }

    [Fact]
    public void IsWebKitInstalled_NoLdconfigMatch_FileProbeTrue()
    {
        LinuxDependencyService.ProcessRunner = NoLdconfig();
        LinuxDependencyService.FileExists = _ => true;
        Assert.True(LinuxDependencyService.IsWebKitInstalled());
    }

    [Fact]
    public void IsWebKitInstalled_NoMatchAnywhere_False()
    {
        LinuxDependencyService.ProcessRunner = NoLdconfig();
        LinuxDependencyService.FileExists = _ => false;
        Assert.False(LinuxDependencyService.IsWebKitInstalled());
    }

    [Fact]
    public void IsWebKitInstalled_LdconfigThrows_FallsBackToProbe()
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (0, "", "")) { Throw = true };
        LinuxDependencyService.FileExists = _ => false;
        Assert.False(LinuxDependencyService.IsWebKitInstalled());
    }

    [Fact]
    public void IsPkexecAvailable_FoundOnPath()
    {
        LinuxDependencyService.GetPathEnv = () => "/usr/bin:/bin";
        LinuxDependencyService.FileExists = p => p.Replace('\\', '/').EndsWith("/bin/pkexec");
        Assert.True(LinuxDependencyService.IsPkexecAvailable());
    }

    [Fact]
    public void IsPkexecAvailable_NotFound()
    {
        LinuxDependencyService.GetPathEnv = () => "/usr/bin";
        LinuxDependencyService.FileExists = _ => false;
        Assert.False(LinuxDependencyService.IsPkexecAvailable());
    }

    [Fact]
    public async Task InstallAsync_EmptyCommand_Fails()
    {
        var result = await LinuxDependencyService.InstallAsync("  ");
        Assert.False(result.Success);
        Assert.Contains("No install command", result.Message);
    }

    [Fact]
    public async Task InstallAsync_LaunchFailure_ReturnsMessage()
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (0, "", "")) { Throw = true };
        var result = await LinuxDependencyService.InstallAsync("apt install x");
        Assert.False(result.Success);
        Assert.Contains("Could not invoke pkexec", result.Message);
    }

    [Fact]
    public async Task InstallAsync_Cancelled_ReturnsCancelled()
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (0, "", ""))
        {
            ThrowException = new OperationCanceledException()
        };
        var result = await LinuxDependencyService.InstallAsync("apt install x");
        Assert.False(result.Success);
        Assert.Equal("Cancelled.", result.Message);
    }

    [Fact]
    public async Task InstallAsync_Success_ReportsOutput()
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (0, "line1\nline2", "warn"));
        var reported = new List<string>();
        var progress = new Progress<string>(reported.Add);

        var result = await LinuxDependencyService.InstallAsync("apt install x", progress);

        Assert.True(result.Success);
        Assert.Equal("Installation complete.", result.Message);
        // Progress callbacks are posted asynchronously; give them a moment.
        await Task.Delay(50);
        Assert.Contains("line1", reported);
    }

    [Theory]
    [InlineData(126, "Authentication was dismissed.")]
    [InlineData(127, "Authentication failed.")]
    [InlineData(1, "Install command exited with code 1.")]
    public async Task InstallAsync_NonZeroExit_MapsReason(int exit, string expected)
    {
        LinuxDependencyService.ProcessRunner = new FakeProcessRunner(_ => (exit, "", ""));
        var result = await LinuxDependencyService.InstallAsync("apt install x");
        Assert.False(result.Success);
        Assert.Equal(expected, result.Message);
    }
}
