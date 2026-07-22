using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class AppearanceIndexServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _project = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private static string Mention(string id, string text)
        => $"<p>Then <span class=\"nv-entity-mention\" data-entity-id=\"{id}\">{text}</span> spoke.</p>";

    [Fact]
    public async Task Build_MapsEntityToMentioningScenes_WithEnrichedFields()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "Opening");
        sc.Synopsis = "Aldric arrives at the gate.";
        sc.Date = "2024-10-22";
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.True(index.ContainsKey("hero"));
        var refs = index["hero"];
        Assert.Single(refs);
        Assert.Equal(ch.Guid, refs[0].ChapterGuid);
        Assert.Equal(sc.Id, refs[0].SceneId);
        Assert.Equal("One", refs[0].ChapterTitle);
        Assert.Equal("Opening", refs[0].SceneTitle);
        Assert.Equal("Aldric arrives at the gate.", refs[0].Synopsis);
        Assert.Equal("2024-10-22", refs[0].StoryDate);
        Assert.Equal("2024-10-22", refs[0].IsoDate);
    }

    [Fact]
    public async Task Build_NullSynopsis_WhenSceneHasNone()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "Opening");
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Null(index["hero"][0].Synopsis);
        Assert.Equal(string.Empty, index["hero"][0].StoryDate);
        Assert.Null(index["hero"][0].IsoDate);
    }

    [Fact]
    public async Task Build_DedupesMultipleSpansOfSameEntityInOneScene()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric") + Mention("hero", "he"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Single(index["hero"]);
    }

    [Fact]
    public async Task Build_ExcludesScenesWithoutMentionSpans()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        await _project.WriteSceneContentAsync(ch, sc, "<p>Just plain prose, no markers.</p>");

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Empty(index);
    }

    [Fact]
    public async Task Build_PreservesManuscriptOrderAcrossChaptersAndScenes()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var c1 = await _project.CreateChapterAsync("C1");
        var c2 = await _project.CreateChapterAsync("C2");
        var s1 = await _project.CreateSceneAsync(c1.Guid, "S1");
        var s2 = await _project.CreateSceneAsync(c2.Guid, "S2");
        await _project.WriteSceneContentAsync(c2, s2, Mention("hero", "Aldric"));
        await _project.WriteSceneContentAsync(c1, s1, Mention("hero", "Aldric"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);
        var refs = index["hero"];

        Assert.Equal(2, refs.Count);
        // Scanned in reading order: chapter 1 scene before chapter 2 scene.
        Assert.True(refs[0].ChapterOrder < refs[1].ChapterOrder);
        Assert.Equal(s1.Id, refs[0].SceneId);
        Assert.Equal(s2.Id, refs[1].SceneId);
    }

    [Fact]
    public async Task Build_MultipleEntitiesInOneScene_ShareCoMentionSet()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric") + Mention("villain", "Mordre"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Single(index["hero"]);
        Assert.Single(index["villain"]);
        // Each appearance carries the full set of entities co-mentioned in the scene.
        Assert.Equal(new[] { "hero", "villain" }, index["hero"][0].EntityIds);
    }

    [Fact]
    public async Task Build_CapturesPlotlineIds()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        sc.PlotlineIds = ["p1", "p2"];
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Equal(new[] { "p1", "p2" }, index["hero"][0].PlotlineIds);
    }

    [Fact]
    public async Task Build_UsesManualPovOverride()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        sc.AnalysisOverrides = new SceneAnalysisOverrides { Pov = "  Aldric Vane  " };
        await _project.WriteSceneContentAsync(ch, sc, Mention("hero", "Aldric"));

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Equal("Aldric Vane", index["hero"][0].Pov);
    }

    [Fact]
    public async Task Build_DetectsPovFromText_WhenNoOverride()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        var html = "<p>Aldric drew his sword. Aldric ran. Aldric shouted at " +
                   "<span class=\"nv-entity-mention\" data-entity-id=\"hero\">Aldric</span>.</p>";
        await _project.WriteSceneContentAsync(ch, sc, html);

        var characters = new List<CharacterData> { new() { Id = "hero", Name = "Aldric" } };
        var index = await new AppearanceIndexService(_project).BuildAsync(characters);

        Assert.Equal("Aldric", index["hero"][0].Pov);
    }

    [Fact]
    public async Task Build_SkipsScene_WhenMarkerPresentButNoValidIds()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _project.CreateChapterAsync("One");
        var sc = await _project.CreateSceneAsync(ch.Guid, "S");
        // Marker class present, but the id attribute is empty -> no valid entity ids.
        await _project.WriteSceneContentAsync(ch, sc,
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"\">x</span></p>");

        var index = await new AppearanceIndexService(_project).BuildAsync([]);

        Assert.Empty(index);
    }
}
