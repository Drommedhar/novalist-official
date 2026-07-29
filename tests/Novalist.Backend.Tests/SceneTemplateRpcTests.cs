using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Scene templates over the RPC surface, against a real project.</summary>
public sealed class SceneTemplateRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SceneTemplateRpc _rpc;
    private readonly ProjectRpc _project;

    public SceneTemplateRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-scenetpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "TplNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SceneTemplateRpc(_workspace);
        _project = new ProjectRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<(string ChapterGuid, string SceneId)> SourceSceneAsync()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Interrogation");
        scene.Synopsis = "Someone asks; someone lies";
        scene.Stage = "draft";
        scene.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides
        {
            Pov = "Mira",
            Tags = ["night"]
        };
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Who is asking?</p>");
        await _workspace.Projects.SaveScenesAsync();
        return (chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task ATemplateMadeFromASceneReportsWhatItCarries()
    {
        var (chapterGuid, sceneId) = await SourceSceneAsync();

        var templates = await _rpc.SaveFromSceneAsync(chapterGuid, sceneId, "Interrogation");

        var template = Assert.Single(templates);
        Assert.Equal("Interrogation", template.Name);
        Assert.Equal("Someone asks; someone lies", template.Synopsis);
        Assert.Equal("Mira", template.Pov);
        Assert.Equal("draft", template.Stage);
        Assert.Equal(["night"], template.Tags);
        Assert.True(template.ContentLength > 0);
    }

    [Fact]
    public async Task ASceneCreatedFromATemplateStartsWithItsFields()
    {
        var (chapterGuid, sceneId) = await SourceSceneAsync();
        var templateId = (await _rpc.SaveFromSceneAsync(chapterGuid, sceneId, "Interrogation"))[0].Id;

        var state = await _project.CreateSceneAsync(chapterGuid, "A second one", templateId);

        var created = state.Chapters
            .Single(c => c.Guid == chapterGuid).Scenes
            .Single(s => s.Title == "A second one");
        Assert.Equal("Someone asks; someone lies", created.Synopsis);
        Assert.Equal("draft", created.Stage);
    }

    [Fact]
    public async Task ASceneCreatedWithNoTemplateIsBlank()
    {
        var (chapterGuid, sceneId) = await SourceSceneAsync();
        await _rpc.SaveFromSceneAsync(chapterGuid, sceneId, "Interrogation");

        var state = await _project.CreateSceneAsync(chapterGuid, "Blank");

        var created = state.Chapters
            .Single(c => c.Guid == chapterGuid).Scenes
            .Single(s => s.Title == "Blank");
        Assert.Null(created.Synopsis);
    }

    [Fact]
    public async Task AnUnknownTemplateIdIsJustABlankScene()
    {
        var (chapterGuid, _) = await SourceSceneAsync();

        var state = await _project.CreateSceneAsync(chapterGuid, "Blank", "nope");

        Assert.Null(state.Chapters
            .Single(c => c.Guid == chapterGuid).Scenes
            .Single(s => s.Title == "Blank").Synopsis);
    }

    [Fact]
    public async Task DeletingATemplateLeavesTheScenesMadeFromItAlone()
    {
        var (chapterGuid, sceneId) = await SourceSceneAsync();
        var templateId = (await _rpc.SaveFromSceneAsync(chapterGuid, sceneId, "Interrogation"))[0].Id;
        await _project.CreateSceneAsync(chapterGuid, "From the template", templateId);

        Assert.Empty(await _rpc.DeleteAsync(templateId));
        // The source scene and the one made from the template both remain.
        Assert.Equal(2, _workspace.Projects.GetScenesForChapter(chapterGuid).Count);
    }
}
