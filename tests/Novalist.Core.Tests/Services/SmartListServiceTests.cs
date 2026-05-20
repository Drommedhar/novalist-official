using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class SmartListServiceTests
{
    private static (SmartListService Sut, IProjectService Project, IEntityService Entity) Build()
    {
        var project = Substitute.For<IProjectService>();
        var entity = Substitute.For<IEntityService>();
        entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        return (new SmartListService(project, entity), project, entity);
    }

    [Fact]
    public void GetAll_NoProject_ReturnsEmpty()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        Assert.Empty(sut.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsProjectSmartLists()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "l1" } } };
        project.CurrentProject.Returns(meta);
        Assert.Single(sut.GetAll());
    }

    [Fact]
    public async Task SaveAsync_NoProject_DoesNothing()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.SaveAsync(new SmartList());
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_AddsNewList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata();
        project.CurrentProject.Returns(meta);

        await sut.SaveAsync(new SmartList { Id = "new" });

        Assert.Single(meta.SmartLists);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "x", Name = "old" } } };
        project.CurrentProject.Returns(meta);

        await sut.SaveAsync(new SmartList { Id = "x", Name = "updated" });

        Assert.Single(meta.SmartLists);
        Assert.Equal("updated", meta.SmartLists[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_NoProject_DoesNothing()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.DeleteAsync("x");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteAsync_RemovesMatchingList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "x" } } };
        project.CurrentProject.Returns(meta);

        await sut.DeleteAsync("x");

        Assert.Empty(meta.SmartLists);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task EvaluateAsync_NoFilters_ReturnsAllScenes()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList());
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_ChapterStatusFilter_SkipsNonMatching()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1", Status = ChapterStatus.Outline };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1" } });

        var result = await sut.EvaluateAsync(new SmartList { ChapterStatus = "Done" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task EvaluateAsync_TagFilter_MatchesSceneTag()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var matching = new SceneData { Id = "s1", AnalysisOverrides = new() { Tags = new() { "action" } } };
        var other = new SceneData { Id = "s2", AnalysisOverrides = new() { Tags = new() { "calm" } } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { matching, other });

        var result = await sut.EvaluateAsync(new SmartList { Tag = "action" });
        Assert.Single(result);
        Assert.Equal("s1", result[0].Scene.Id);
    }

    [Fact]
    public async Task EvaluateAsync_TagFilter_NoTags_Skips()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1" } });

        var result = await sut.EvaluateAsync(new SmartList { Tag = "action" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_UsesOverridePov()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1", AnalysisOverrides = new() { Pov = "Alice" } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "ali" });
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_FallsBackToAutoDetect()
    {
        var (sut, project, entity) = Build();
        entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Bob" } });
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("<p>Bob ran. Bob fell. Bob wept. Bob slept.</p>");

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "Bob" });
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_NoMatch_Skips()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1", AnalysisOverrides = new() { Pov = "Alice" } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "Zelda" });
        Assert.Empty(result);
    }
}
