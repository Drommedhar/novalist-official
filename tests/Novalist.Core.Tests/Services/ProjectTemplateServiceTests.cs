using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ProjectTemplateServiceTests
{
    private readonly ProjectTemplateService _sut = new();

    [Fact]
    public void GetTemplates_IncludesBuiltIns()
    {
        var templates = _sut.GetTemplates();
        Assert.Contains(templates, t => t.Id == "blank");
        Assert.Contains(templates, t => t.Id == "three-act");
        Assert.Contains(templates, t => t.Id == "save-the-cat");
        Assert.Contains(templates, t => t.Id == "hero-journey");
        Assert.Contains(templates, t => t.Id == "non-fiction");
    }

    [Fact]
    public void GetById_Match_CaseInsensitive()
        => Assert.Equal("three-act", _sut.GetById("THREE-ACT")!.Id);

    [Fact]
    public void GetById_NoMatch_ReturnsNull()
        => Assert.Null(_sut.GetById("nope"));

    [Fact]
    public async Task ApplyAsync_Blank_DoesNothing()
    {
        var project = Substitute.For<IProjectService>();
        await _sut.ApplyAsync(project, _sut.GetById("blank")!);
        await project.DidNotReceiveWithAnyArgs().CreateChapterAsync(default!);
        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task ApplyAsync_CreatesChaptersScenesAndSaves()
    {
        var project = Substitute.For<IProjectService>();
        project.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new ChapterData { Title = ci.ArgAt<string>(0) });
        project.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new SceneData { Title = ci.ArgAt<string>(1) });

        var template = _sut.GetById("three-act")!;
        await _sut.ApplyAsync(project, template);

        // three-act has 3 chapters, 7 scenes total.
        await project.Received(3).CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>());
        await project.Received(7).CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await project.Received(1).SaveScenesAsync();
    }

    [Fact]
    public async Task ApplyAsync_SetsActAndSynopsis_AndHandlesEmptySynopsis()
    {
        var project = Substitute.For<IProjectService>();
        var createdChapters = new List<ChapterData>();
        var createdScenes = new List<SceneData>();
        project.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => { var c = new ChapterData { Title = ci.ArgAt<string>(0) }; createdChapters.Add(c); return c; });
        project.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => { var s = new SceneData { Title = ci.ArgAt<string>(1) }; createdScenes.Add(s); return s; });

        // hero-journey has Act labels and some empty synopses.
        await _sut.ApplyAsync(project, _sut.GetById("hero-journey")!);

        Assert.Contains(createdChapters, c => c.Act == "I");
        // Scenes with empty synopsis stay null (synopsis only set when non-blank).
        Assert.Contains(createdScenes, s => s.Synopsis == null);
    }

    [Fact]
    public async Task ApplyAsync_NonFiction_ChaptersWithoutAct()
    {
        var project = Substitute.For<IProjectService>();
        var chapters = new List<ChapterData>();
        project.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => { var c = new ChapterData { Title = ci.ArgAt<string>(0) }; chapters.Add(c); return c; });
        project.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new SceneData { Title = ci.ArgAt<string>(1) });

        await _sut.ApplyAsync(project, _sut.GetById("non-fiction")!);
        // Non-fiction chapters have no Act -> Act stays empty.
        Assert.Contains(chapters, c => string.IsNullOrEmpty(c.Act));
    }
}
