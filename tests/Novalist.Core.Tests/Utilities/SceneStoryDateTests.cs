using Novalist.Core.Models;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class SceneStoryDateTests
{
    [Fact]
    public void Resolve_PrefersSceneRange()
    {
        var chapter = new ChapterData { Date = "2020-01-01" };
        var scene = new SceneData
        {
            Date = "2021-01-01",
            DateRange = new StoryDateRange { Start = "2022-06-15" }
        };
        Assert.Equal("2022-06-15", SceneStoryDate.Resolve(chapter, scene));
    }

    [Fact]
    public void Resolve_FallsBackToSceneDate_WhenNoSceneRange()
    {
        var chapter = new ChapterData { Date = "2020-01-01", DateRange = new StoryDateRange { Start = "2019-01-01" } };
        var scene = new SceneData { Date = "  2021-03-09  " };
        Assert.Equal("2021-03-09", SceneStoryDate.Resolve(chapter, scene));
    }

    [Fact]
    public void Resolve_FallsBackToChapterRange_WhenSceneHasNoDate()
    {
        var chapter = new ChapterData { Date = "2020-01-01", DateRange = new StoryDateRange { Start = "2019-12-25" } };
        var scene = new SceneData();
        Assert.Equal("2019-12-25", SceneStoryDate.Resolve(chapter, scene));
    }

    [Fact]
    public void Resolve_FallsBackToChapterDate_WhenNothingElse()
    {
        var chapter = new ChapterData { Date = "  2018-07-04  " };
        var scene = new SceneData();
        Assert.Equal("2018-07-04", SceneStoryDate.Resolve(chapter, scene));
    }

    [Fact]
    public void Resolve_EmptyWhenNoDatesAnywhere()
    {
        Assert.Equal(string.Empty, SceneStoryDate.Resolve(new ChapterData(), new SceneData()));
    }

    [Fact]
    public void Resolve_IgnoresEmptySceneRange()
    {
        var chapter = new ChapterData();
        var scene = new SceneData { Date = "2021-05-05", DateRange = new StoryDateRange() };
        Assert.Equal("2021-05-05", SceneStoryDate.Resolve(chapter, scene));
    }
}
