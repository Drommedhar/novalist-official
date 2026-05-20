using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class PluginImportServiceHelperTests
{
    [Fact]
    public void ConvertMarkdownToHtml_Empty_ReturnsEmptyBody()
        => Assert.Equal("<html><head></head><body><p></p></body></html>",
            PluginImportService.ConvertMarkdownToHtml("  "));

    [Fact]
    public void ConvertMarkdownToHtml_WrapsBodyAndConvertsFormatting()
    {
        var html = PluginImportService.ConvertMarkdownToHtml("**bold** and *italic*");
        Assert.StartsWith("<html><head></head><body>", html);
        Assert.Contains("font-weight:bold", html);
        Assert.Contains("font-style:italic", html);
    }

    [Theory]
    [InlineData("<strong>x</strong>", "font-weight:bold")]
    [InlineData("<em>x</em>", "font-style:italic")]
    [InlineData("<del>x</del>", "text-decoration:line-through")]
    [InlineData("<ul><li><p>x</p></li></ul>", "• x")]
    [InlineData("<ul><li>x</li></ul>", "• x")]
    [InlineData("<h2>Heading</h2>", "font-weight:bold")]
    public void SanitizeHtml_ConvertsConstructs(string input, string expectedFragment)
        => Assert.Contains(expectedFragment, PluginImportService.SanitizeHtmlForRichTextBox(input));

    [Fact]
    public void SanitizeHtml_StripsBlockquoteAndUnsupportedTags_WrapsBareText()
    {
        var html = PluginImportService.SanitizeHtmlForRichTextBox("<blockquote><p>hi <code>x</code> there</p></blockquote>");
        Assert.DoesNotContain("blockquote", html);
        Assert.DoesNotContain("<code>", html);
        Assert.Contains("<span>", html); // bare text wrapped
    }

    [Fact]
    public void SanitizeHtml_BrBecomesParagraphBoundary_AndEmptyPCollapses()
    {
        var html = PluginImportService.SanitizeHtmlForRichTextBox("<p>a<br/>b</p>");
        Assert.Contains("</p><p>", html);
    }

    [Fact]
    public void SanitizeHtml_KeepsAllSpanContentUnchanged()
    {
        var html = PluginImportService.SanitizeHtmlForRichTextBox("<p><span style=\"font-weight:bold\">x</span></p>");
        Assert.Contains("font-weight:bold", html);
    }

    [Fact]
    public void SanitizeHtml_WhitespaceOnlyParagraph_BecomesEmpty()
        => Assert.Equal("<p></p>", PluginImportService.SanitizeHtmlForRichTextBox("<p>   </p>"));

    [Theory]
    [InlineData("![[Images/pic.png]]", "Images/pic.png")]
    [InlineData("[[Alice]]", "Alice")]
    [InlineData("[[path|alias]]", "path")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void StripWikilink(string input, string expected)
        => Assert.Equal(expected, PluginImportService.StripWikilink(input));

    [Theory]
    [InlineData("first-draft", ChapterStatus.FirstDraft)]
    [InlineData("revised", ChapterStatus.Revised)]
    [InlineData("edited", ChapterStatus.Edited)]
    [InlineData("final", ChapterStatus.Final)]
    [InlineData("outline", ChapterStatus.Outline)]
    [InlineData(null, ChapterStatus.Outline)]
    [InlineData("weird", ChapterStatus.Outline)]
    public void MapChapterStatus(string? status, ChapterStatus expected)
        => Assert.Equal(expected, PluginImportService.MapChapterStatus(status));

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one two three", 3)]
    public void CountWords(string text, int expected)
        => Assert.Equal(expected, PluginImportService.CountWords(text));

    [Fact]
    public void SanitizeFileName_RemovesInvalidChars()
    {
        var invalid = Path.GetInvalidFileNameChars().First();
        Assert.Equal("ab", PluginImportService.SanitizeFileName($"a{invalid}b"));
    }

    [Fact]
    public void NonEmpty_NullIfEmpty()
    {
        Assert.Equal("fallback", PluginImportService.NonEmpty("", "fallback"));
        Assert.Equal("value", PluginImportService.NonEmpty("value", "fallback"));
        Assert.Null(PluginImportService.NullIfEmpty(""));
        Assert.Equal("x", PluginImportService.NullIfEmpty("x"));
    }
}

public class PluginSettingsDataTests
{
    private static PluginSettingsData Parse(string json)
        => new(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void GetStringOrDefault()
    {
        var s = Parse("""{ "a": "hello", "n": 5 }""");
        Assert.Equal("hello", s.GetStringOrDefault("a"));
        Assert.Equal("def", s.GetStringOrDefault("missing", "def")); // absent
        Assert.Equal("def", s.GetStringOrDefault("n", "def"));        // wrong kind
    }

    [Fact]
    public void GetObjectOrDefault()
    {
        var s = Parse("""{ "obj": { "x": 1 }, "notobj": 3 }""");
        Assert.NotNull(s.GetObjectOrDefault("obj"));
        Assert.Null(s.GetObjectOrDefault("notobj"));
        Assert.Null(s.GetObjectOrDefault("missing"));
    }

    [Fact]
    public void GetArrayOrDefault()
    {
        var s = Parse("""{ "arr": [1,2], "notarr": 3 }""");
        Assert.NotNull(s.GetArrayOrDefault("arr"));
        Assert.Null(s.GetArrayOrDefault("notarr"));
        Assert.Null(s.GetArrayOrDefault("missing"));
    }
}

public class PluginDetectionTests
{
    [Fact]
    public async Task Detect_FromDataJson()
    {
        using var dir = new TempDir();
        var pluginDir = Path.Combine(dir.Path, ".obsidian", "plugins", "novalist");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "data.json"),
            """{ "projects": [ { "name": "My Novel", "path": "Novel" } ] }""");

        var result = await PluginImportService.DetectPluginProjectAsync(dir.Path);
        Assert.True(result.HasPluginData);
        Assert.Single(result.Projects);
        Assert.Equal("My Novel", result.Projects[0].Name);
    }

    [Fact]
    public async Task Detect_RootHasChapters()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Chapters"));
        var result = await PluginImportService.DetectPluginProjectAsync(dir.Path);
        Assert.Single(result.Projects);
        Assert.Equal("", result.Projects[0].Path);
    }

    [Fact]
    public async Task Detect_SubdirHasChapters_SkipsDotDirs()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Novel", "Chapters"));
        Directory.CreateDirectory(Path.Combine(dir.Path, ".hidden", "Chapters"));
        var result = await PluginImportService.DetectPluginProjectAsync(dir.Path);
        Assert.Single(result.Projects);
        Assert.Equal("Novel", result.Projects[0].Path);
    }

    [Fact]
    public async Task Detect_NothingFound_Empty()
    {
        using var dir = new TempDir();
        var result = await PluginImportService.DetectPluginProjectAsync(dir.Path);
        Assert.Empty(result.Projects);
    }
}
