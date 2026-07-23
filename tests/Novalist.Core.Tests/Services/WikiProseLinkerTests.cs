using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class WikiProseLinkerTests
{
    private static Dictionary<string, (string Id, string TypeKey)> Resolve(
        params (string Name, string Id, string Type)[] entries)
    {
        var map = new Dictionary<string, (string Id, string TypeKey)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, id, type) in entries)
            map[name] = (id, type);
        return map;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Linkify_NullOrEmpty_ReturnsSameString(string? content)
        => Assert.Equal(content ?? string.Empty, WikiProseLinker.Linkify(content, Resolve()));

    [Fact]
    public void Linkify_NoNames_ReturnsContentUnchanged()
        => Assert.Equal("Just some prose.", WikiProseLinker.Linkify("Just some prose.", Resolve()));

    [Fact]
    public void Linkify_BareName_BecomesEntityLink()
    {
        var result = WikiProseLinker.Linkify("Aldric drew his sword.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("[Aldric](nventity:character/c1) drew his sword.", result);
    }

    [Fact]
    public void Linkify_MatchIsCaseInsensitiveButPreservesOriginalText()
    {
        var result = WikiProseLinker.Linkify("ALDRIC shouted.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("[ALDRIC](nventity:character/c1) shouted.", result);
    }

    [Fact]
    public void Linkify_DoesNotMatchInsideLargerWord()
    {
        // "Ann" must not light up inside "Announcement".
        var result = WikiProseLinker.Linkify("The Announcement came.", Resolve(("Ann", "c1", "character")));
        Assert.Equal("The Announcement came.", result);
    }

    [Fact]
    public void Linkify_PrefersLongerNameAtSamePosition()
    {
        var map = Resolve(("Aldric", "c1", "character"), ("Aldric Vane", "c2", "character"));
        var result = WikiProseLinker.Linkify("Aldric Vane rode north.", map);
        Assert.Equal("[Aldric Vane](nventity:character/c2) rode north.", result);
    }

    [Fact]
    public void Linkify_SkipsSelfEntity()
    {
        var result = WikiProseLinker.Linkify("Aldric thinks of Aldric.", Resolve(("Aldric", "c1", "character")), "c1");
        Assert.Equal("Aldric thinks of Aldric.", result);
    }

    [Fact]
    public void Linkify_UnresolvedNameStaysPlain()
        => Assert.Equal("Nobody here.", WikiProseLinker.Linkify("Nobody here.", Resolve(("Aldric", "c1", "character"))));

    [Fact]
    public void Linkify_ExplicitWikiLink_Resolves()
    {
        var result = WikiProseLinker.Linkify("Meet [[Aldric]] soon.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("Meet [Aldric](nventity:character/c1) soon.", result);
    }

    [Fact]
    public void Linkify_ExplicitWikiLink_WithDisplayText()
    {
        var result = WikiProseLinker.Linkify("Meet [[Aldric|the knight]] soon.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("Meet [the knight](nventity:character/c1) soon.", result);
    }

    [Fact]
    public void Linkify_ExplicitWikiLink_EmptyDisplayFallsBackToTarget()
    {
        var result = WikiProseLinker.Linkify("Meet [[Aldric|]] soon.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("Meet [Aldric](nventity:character/c1) soon.", result);
    }

    [Fact]
    public void Linkify_ExplicitWikiLink_Unresolved_StripsBrackets()
        => Assert.Equal("Meet Ghost soon.", WikiProseLinker.Linkify("Meet [[Ghost]] soon.", Resolve()));

    [Fact]
    public void Linkify_ExplicitWikiLink_Self_StripsBracketsNoLink()
    {
        var result = WikiProseLinker.Linkify("[[Aldric]] muses.", Resolve(("Aldric", "c1", "character")), "c1");
        Assert.Equal("Aldric muses.", result);
    }

    [Fact]
    public void Linkify_InlineCode_IsProtected()
    {
        var result = WikiProseLinker.Linkify("Use `Aldric` verbatim.", Resolve(("Aldric", "c1", "character")));
        Assert.Equal("Use `Aldric` verbatim.", result);
    }

    [Fact]
    public void Linkify_FencedCode_IsProtected()
    {
        var content = "Before Aldric.\n```\nAldric stays raw\n```\nAfter Aldric.";
        var result = WikiProseLinker.Linkify(content, Resolve(("Aldric", "c1", "character")));
        Assert.Equal(
            "Before [Aldric](nventity:character/c1).\n```\nAldric stays raw\n```\nAfter [Aldric](nventity:character/c1).",
            result);
    }

    [Fact]
    public void Linkify_ExistingMarkdownLink_IsProtected()
    {
        var content = "See [Aldric](https://example.com) online.";
        var result = WikiProseLinker.Linkify(content, Resolve(("Aldric", "c1", "character")));
        Assert.Equal(content, result);
    }

    [Fact]
    public void Linkify_MarkdownImage_IsProtected()
    {
        var content = "![Aldric](portrait.png) hangs here.";
        var result = WikiProseLinker.Linkify(content, Resolve(("Aldric", "c1", "character")));
        Assert.Equal("![Aldric](portrait.png) hangs here.", result);
    }

    [Fact]
    public void Linkify_LinksAcrossMultipleEntities()
    {
        var map = Resolve(("Aldric", "c1", "character"), ("Harbour", "l1", "location"));
        var result = WikiProseLinker.Linkify("Aldric sailed to Harbour.", map);
        Assert.Equal("[Aldric](nventity:character/c1) sailed to [Harbour](nventity:location/l1).", result);
    }
}
