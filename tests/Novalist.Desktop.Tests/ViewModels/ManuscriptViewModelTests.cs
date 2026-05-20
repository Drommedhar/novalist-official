using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class ManuscriptViewModelTests
{
    private static ChapterData Chap(string guid, string title, ChapterStatus status = ChapterStatus.FirstDraft, string act = "")
        => new() { Guid = guid, Title = title, Status = status, Act = act, Order = 1 };

    private static SceneData Scene(string id, string title, int words = 100)
        => new() { Id = id, Title = title, WordCount = words };

    private static (ManuscriptViewModel Vm, IProjectService Proj, IEntityService Ent, TempDir Dir) Build(
        bool loaded = true, bool withRoot = true,
        (ChapterData Chapter, SceneData[] Scenes)[]? chapters = null,
        Dictionary<string, string>? sceneHtml = null)
    {
        var dir = new TempDir();
        var proj = Substitute.For<IProjectService>();
        var ent = Substitute.For<IEntityService>();
        proj.IsProjectLoaded.Returns(loaded);
        proj.ProjectRoot.Returns(withRoot ? dir.Path : (string?)null);
        proj.SaveScenesAsync().Returns(Task.CompletedTask);
        proj.WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        ent.LoadCharactersAsync().Returns(new List<CharacterData>());
        proj.GetChaptersOrdered().Returns((chapters ?? []).Select(c => c.Chapter).ToList());
        foreach (var (ch, scenes) in chapters ?? [])
        {
            proj.GetScenesForChapter(ch.Guid).Returns(scenes.ToList());
            foreach (var sc in scenes)
            {
                var path = Path.Combine(dir.Path, $"{ch.Guid}_{sc.Id}.html");
                proj.GetSceneFilePath(ch, sc).Returns(path);
                if (sceneHtml != null && sceneHtml.TryGetValue(sc.Id, out var html))
                    File.WriteAllText(path, html);
            }
        }
        return (new ManuscriptViewModel(proj, ent), proj, ent, dir);
    }

    [Fact]
    public void ViewMode_Toggles()
    {
        var (vm, _, _, dir) = Build(); using var _d = dir;
        Assert.True(vm.IsManuscriptMode);
        vm.SetViewModeCommand.Execute("Corkboard");
        Assert.True(vm.IsCorkboardMode);
        vm.SetViewModeCommand.Execute("Outliner");
        Assert.True(vm.IsOutlinerMode);
        vm.SetViewModeCommand.Execute(""); // empty -> unchanged
        Assert.True(vm.IsOutlinerMode);
    }

    [Fact]
    public void StatusOptions_AndDisplays()
    {
        var (vm, _, _, dir) = Build(); using var _d = dir;
        Assert.Equal(Enum.GetValues<ChapterStatus>().Length, vm.StatusOptions.Count);
        Assert.NotNull(vm.TotalWordsDisplay);
        Assert.NotNull(vm.ReadingTimeDisplay);
    }

    [Fact]
    public async Task Refresh_NotLoaded_Empty()
    {
        var (vm, _, _, dir) = Build(loaded: false); using var _d = dir;
        await vm.RefreshAsync();
        Assert.Empty(vm.Sections);
        Assert.False(vm.HasContent);
    }

    [Fact]
    public async Task Refresh_BuildsSections_Totals_ReadsContent()
    {
        var ch = Chap("c1", "One");
        var s1 = Scene("s1", "Scene1", 120);
        var s2 = Scene("s2", "Scene2", 80);
        var (vm, _, _, dir) = Build(
            chapters: [(ch, new[] { s1, s2 })],
            sceneHtml: new() { ["s1"] = "<p>hello world</p>" }); // s1 has a file, s2 doesn't
        using var _d = dir;

        await vm.RefreshAsync();

        Assert.Single(vm.Sections);
        Assert.Equal(2, vm.Sections[0].Scenes.Count);
        Assert.Equal(2, vm.TotalScenes);
        Assert.Equal(200, vm.TotalWords);
        Assert.True(vm.HasContent);
        Assert.Equal(2, vm.AllScenes.Count);
        Assert.Contains("hello", vm.Sections[0].Scenes.First(s => s.Scene.Id == "s1").HtmlContent);
        Assert.Equal(string.Empty, vm.Sections[0].Scenes.First(s => s.Scene.Id == "s2").HtmlContent);
    }

    [Fact]
    public async Task Refresh_ProjectRootNull_EmptyContent()
    {
        var ch = Chap("c1", "One");
        var (vm, _, _, dir) = Build(withRoot: false, chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        Assert.Equal(string.Empty, vm.Sections[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task Refresh_FilterStatus_SkipsNonMatching()
    {
        var draft = Chap("c1", "Draft", ChapterStatus.FirstDraft);
        var final = Chap("c2", "Final", ChapterStatus.Final);
        var (vm, proj, _, dir) = Build(chapters:
        [
            (draft, new[] { Scene("s1", "A") }),
            (final, new[] { Scene("s2", "B") }),
        ]);
        using var _d = dir;
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Sections.Count);

        vm.FilterStatus = "Final"; // OnFilterStatusChanged -> Refresh
        await Task.Delay(30);
        Assert.Single(vm.Sections);
        Assert.Equal("Final", vm.Sections[0].Status);
    }

    [Fact]
    public async Task RequestOpenScene_And_OnSceneFocused()
    {
        var ch = Chap("c1", "One");
        var (vm, _, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })],
            sceneHtml: new() { ["s1"] = "<b>Bold</b> text" });
        using var _d = dir;
        await vm.RefreshAsync();

        (ChapterData, SceneData)? opened = null;
        vm.SceneOpenRequested += (c, s) => opened = (c, s);
        vm.RequestOpenScene("c1", "s1");
        Assert.NotNull(opened);
        vm.RequestOpenScene("c1", "ghost"); // not found -> no-op

        (ChapterData C, SceneData S, string Text)? focused = null;
        vm.SceneFocusChanged += (c, s, t) => focused = (c, s, t);
        vm.OnSceneFocused("c1", "s1");
        Assert.Contains("Bold", focused!.Value.Text); // tags stripped
        vm.OnSceneFocused("c1", "ghost"); // no-op
    }

    [Fact]
    public async Task CycleStatus_AdvancesAndSaves()
    {
        var ch = Chap("c1", "One", ChapterStatus.Outline);
        var (vm, proj, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        await vm.CycleStatusByGuidAsync("c1");
        Assert.Equal(ChapterStatus.FirstDraft, ch.Status); // Outline -> next
        await proj.Received().SaveScenesAsync();
        await vm.CycleStatusByGuidAsync("ghost"); // not found -> no-op
    }

    [Fact]
    public async Task OnWebViewSceneChanged_UpdatesAndRecalculates()
    {
        var ch = Chap("c1", "One");
        var (vm, _, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S", 50) })]);
        using var _d = dir;
        await vm.RefreshAsync();
        Assert.Equal(50, vm.TotalWords);

        vm.OnWebViewSceneChanged("s1", "c1", "<p>new</p>", 300);
        Assert.Equal(300, vm.TotalWords);
        vm.OnWebViewSceneChanged("ghost", "c1", "x", 5); // not found -> no-op
    }

    [Fact]
    public async Task SaveAllDirty_WritesDirtyScenes()
    {
        var ch = Chap("c1", "One");
        var (vm, proj, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        bool saved = false;
        vm.SceneSaved += () => saved = true;
        vm.OnWebViewSceneChanged("s1", "c1", "<p>dirty</p>", 10); // marks dirty (also schedules autosave)
        await vm.SaveAllDirtyAsync();
        await proj.Received().WriteSceneContentAsync(ch, Arg.Any<SceneData>(), "<p>dirty</p>");
        Assert.True(saved);
    }

    [Fact]
    public async Task GetManuscriptJson_Serializes()
    {
        var ch = Chap("c1", "One", act: "Act1");
        var (vm, _, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        var json = vm.GetManuscriptJson();
        Assert.Contains("c1", json);
        Assert.Contains("Act1", json);
    }

    [Fact]
    public void NotifyContentRefresh_FiresEvent()
    {
        var (vm, _, _, dir) = Build(); using var _d = dir;
        bool fired = false;
        vm.ContentRefreshRequested += () => fired = true;
        vm.NotifyContentRefresh();
        Assert.True(fired);
    }

    [Fact]
    public void SetFilter_SetsStatus()
    {
        var (vm, _, _, dir) = Build(); using var _d = dir;
        vm.SetFilterCommand.Execute("Revised");
        Assert.Equal("Revised", vm.FilterStatus);
    }

    [Fact]
    public async Task AutoSave_AfterWebViewChange_PersistsAfterDelay()
    {
        var ch = Chap("c1", "One");
        var (vm, proj, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        vm.OnWebViewSceneChanged("s1", "c1", "<p>auto</p>", 5); // schedules 800ms autosave
        await Task.Delay(60); // let the first timer enter its await
        vm.OnWebViewSceneChanged("s1", "c1", "<p>auto2</p>", 6); // cancels first mid-await -> OperationCanceledException
        await Task.Delay(950);
        await proj.Received().WriteSceneContentAsync(ch, Arg.Any<SceneData>(), "<p>auto2</p>");
    }

    [Fact]
    public async Task SceneItem_Edit_SchedulesSave()
    {
        var ch = Chap("c1", "One");
        var (vm, proj, _, dir) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        using var _d = dir;
        await vm.RefreshAsync();
        var item = vm.AllScenes[0];
        item.Synopsis = "first edit"; // OnSceneItemEdited -> 500ms timer
        await Task.Delay(60);          // let it enter its await
        item.Synopsis = "second edit"; // cancels first mid-await -> OperationCanceledException
        await Task.Delay(650);
        await proj.Received().SaveScenesAsync();
    }

    // ── ManuscriptSceneItem ─────────────────────────────────────────
    [Fact]
    public void SceneItem_Properties_FireEditedAndPersist()
    {
        var ch = Chap("c1", "Chapter");
        var scene = Scene("s1", "S", 42);
        int edits = 0;
        var item = new ManuscriptSceneItem(ch, scene, "S", "<p>h</p>", 42, _ => edits++) { AutoPov = "Bob" };

        Assert.Equal("Chapter", item.ChapterTitle);
        Assert.Equal(42, item.WordCount);

        item.Synopsis = "Syn";
        Assert.Equal("Syn", scene.Synopsis);
        Assert.Equal("Syn", item.Synopsis); // getter
        item.Synopsis = "Syn"; // unchanged -> no extra edit
        Assert.Equal(1, edits);

        // Pov == AutoPov -> not persisted as override
        item.Pov = "Bob";
        Assert.Null(scene.AnalysisOverrides?.Pov);
        // Pov differs -> persisted
        item.Pov = "Alice";
        Assert.Equal("Alice", scene.AnalysisOverrides!.Pov);
        Assert.Equal("Alice", item.Pov);

        item.ChapterStatusValue = ChapterStatus.Final;
        Assert.Equal(ChapterStatus.Final, ch.Status);
        Assert.Equal(ChapterStatus.Final, item.ChapterStatusValue); // getter
        item.ChapterStatusValue = ChapterStatus.Final; // unchanged

        item.LabelColor = "#fff";
        Assert.Equal("#fff", scene.LabelColor);
        Assert.Equal("#fff", item.LabelColor); // getter
        item.LabelColor = "#fff"; // unchanged

        Assert.True(edits >= 4);
    }

    [Fact]
    public void StatusOption_ToString()
    {
        var opt = new ChapterStatusOption(ChapterStatus.Revised);
        Assert.Equal("Revised", opt.ToString());
        Assert.Equal(ChapterStatus.Revised, opt.Value);
    }
}
