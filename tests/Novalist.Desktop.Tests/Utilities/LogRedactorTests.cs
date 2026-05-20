using Novalist.Desktop.Utilities;
using Xunit;

namespace Novalist.Desktop.Tests.Utilities;

public class LogRedactorTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Scrub_NullOrEmpty_Passthrough(string? input)
        => Assert.Equal(input, LogRedactor.Scrub(input!));

    [Fact]
    public void Scrub_WindowsPath_CollapsedToExtension()
    {
        var result = LogRedactor.Scrub(@"Opened C:\Users\jane\My Novel\scene.json now");
        Assert.DoesNotContain("jane", result);
        Assert.DoesNotContain("My Novel", result);
        Assert.Contains("<path>.json", result);
    }

    [Fact]
    public void Scrub_UncPath_Collapsed()
    {
        var result = LogRedactor.Scrub(@"file at \\server\share\book\ch1.novalist");
        Assert.DoesNotContain("book", result);
        Assert.Contains("<path>", result);
    }

    [Fact]
    public void Scrub_PosixPath_CollapsedToExtension()
    {
        var result = LogRedactor.Scrub("read /home/user/Project/scene.txt done");
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("Project", result);
        Assert.Contains("<path>.txt", result);
    }

    [Fact]
    public void Scrub_FileUrl_Collapsed()
    {
        var result = LogRedactor.Scrub("nav file:///C:/secret/book.json end");
        Assert.DoesNotContain("secret", result);
        Assert.Contains("<path>", result);
    }

    [Fact]
    public void Scrub_PathWithoutExtension_DropsName()
    {
        var result = LogRedactor.Scrub(@"dir C:\Users\jane\SecretFolder here");
        Assert.DoesNotContain("SecretFolder", result);
        Assert.Contains("<path>", result);
    }

    [Fact]
    public void Scrub_LongToken_Redacted()
    {
        var blob = new string('x', 200);
        var result = LogRedactor.Scrub($"data {blob} end");
        Assert.Contains("<redacted:200>", result);
        Assert.DoesNotContain(blob, result);
    }

    [Fact]
    public void Scrub_NormalText_Unchanged()
    {
        const string line = "Loaded project state=Ready count=5 id=abc-123";
        Assert.Equal(line, LogRedactor.Scrub(line));
    }

    [Fact]
    public void Scrub_PreservesStackTraceNamespaces()
    {
        const string line = "at Novalist.Core.Services.ProjectService.LoadProjectAsync";
        Assert.Equal(line, LogRedactor.Scrub(line));
    }
}
