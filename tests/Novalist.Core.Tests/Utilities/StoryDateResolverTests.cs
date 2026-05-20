using Novalist.Core.Models;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class StoryDateResolverTests
{
    [Fact]
    public void Resolve_PrefersSceneDateRange()
    {
        var scene = new SceneData { DateRange = new StoryDateRange { Start = "scene-range" } };
        var result = StoryDateResolver.Resolve(scene, null, null);
        Assert.Equal("scene-range", result!.Start);
    }

    [Fact]
    public void Resolve_FallsBackToSceneDateString()
    {
        var scene = new SceneData { Date = "2024-01-01" };
        var result = StoryDateResolver.Resolve(scene, null, null);
        Assert.Equal("2024-01-01", result!.Start);
    }

    [Fact]
    public void Resolve_FallsBackToChapterDateRange()
    {
        var chapter = new ChapterData { DateRange = new StoryDateRange { Start = "chapter-range" } };
        var result = StoryDateResolver.Resolve(new SceneData(), chapter, null);
        Assert.Equal("chapter-range", result!.Start);
    }

    [Fact]
    public void Resolve_FallsBackToChapterDateString()
    {
        var chapter = new ChapterData { Date = "2024-02-02" };
        var result = StoryDateResolver.Resolve(new SceneData(), chapter, null);
        Assert.Equal("2024-02-02", result!.Start);
    }

    [Fact]
    public void Resolve_FallsBackToActDateRange_WhenChapterHasAct()
    {
        var chapter = new ChapterData { Act = "Act One" };
        var acts = new List<ActData>
        {
            new() { Name = "Act One", DateRange = new StoryDateRange { Start = "act-range" } }
        };
        var result = StoryDateResolver.Resolve(new SceneData(), chapter, acts);
        Assert.Equal("act-range", result!.Start);
    }

    [Fact]
    public void Resolve_ActMatchIsCaseInsensitive()
    {
        var chapter = new ChapterData { Act = "act one" };
        var acts = new List<ActData>
        {
            new() { Name = "Act One", DateRange = new StoryDateRange { Start = "act-range" } }
        };
        Assert.Equal("act-range", StoryDateResolver.Resolve(new SceneData(), chapter, acts)!.Start);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenActNotFound()
    {
        var chapter = new ChapterData { Act = "Missing" };
        var acts = new List<ActData> { new() { Name = "Other", DateRange = new StoryDateRange { Start = "x" } } };
        Assert.Null(StoryDateResolver.Resolve(new SceneData(), chapter, acts));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNothingSet()
        => Assert.Null(StoryDateResolver.Resolve(new SceneData(), new ChapterData(), new List<ActData>()));

    [Fact]
    public void Resolve_ReturnsNull_WhenAllNull()
        => Assert.Null(StoryDateResolver.Resolve(null, null, null));
}

public class StoryDateRangeTests
{
    [Theory]
    [InlineData("start", "", "", true)]
    [InlineData("", "end", "", true)]
    [InlineData("", "", "note", true)]
    [InlineData("", "", "", false)]
    public void HasValue(string start, string end, string note, bool expected)
    {
        var range = new StoryDateRange { Start = start, End = end, Note = note };
        Assert.Equal(expected, range.HasValue);
    }

    [Fact]
    public void Clone_CopiesAllFields()
    {
        var range = new StoryDateRange
        {
            Start = "s", End = "e", Note = "n", StartTime = "10:00", EndTime = "11:00"
        };
        var clone = range.Clone();
        Assert.NotSame(range, clone);
        Assert.Equal("s", clone.Start);
        Assert.Equal("e", clone.End);
        Assert.Equal("n", clone.Note);
        Assert.Equal("10:00", clone.StartTime);
        Assert.Equal("11:00", clone.EndTime);
    }
}
