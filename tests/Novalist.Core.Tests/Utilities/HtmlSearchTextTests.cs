using System.Text.RegularExpressions;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class HtmlSearchTextTests
{
    [Theory]
    [InlineData("<p></p>", "^", "<p>X</p>")]
    [InlineData("<img src='a'>", "$", "<img src='a'>X")]
    [InlineData("", "^", "X")]
    [InlineData("one<strong>two</strong>three", "one|two|three", "X<strong>X</strong>X")]
    [InlineData("<p>one<br>two</p>", "one\\ntwo", "<p>X<br></p>")]
    [InlineData("1 < 2 &amp; 3", "2 & 3", "1 < X")]
    public void ReplacementPreservesMarkupAndUsesVisibleOffsets(string html, string pattern, string expected)
    {
        var result = new HtmlSearchText(html).Replace(new Regex(pattern), "X");
        Assert.Equal(expected, result.Html);
        Assert.Equal(Regex.Matches(new HtmlSearchText(html).Text, pattern).Count, result.Count);
    }

    private sealed class LegacyResolver : IStoredPathResolver
    {
        public string? Resolve(string storedPath) => storedPath;
    }

    [Fact]
    public void StoredPathResolversWithoutGrantsNeedNoReleaseImplementation()
    {
        IStoredPathResolver resolver = new LegacyResolver();
        var path = resolver.Resolve("project");
        resolver.Release(path!);
        Assert.Equal("project", path);
    }
}
