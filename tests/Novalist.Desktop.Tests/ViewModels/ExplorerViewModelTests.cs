using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class ExplorerViewModelTests
{
    private sealed class Harness
    {
        public IProjectService Proj = null!;
        public List<ChapterData> Chapters = [];
        public Dictionary<string, List<SceneData>> Scenes = new();
        public List<SceneData> Archived = [];
        public ExplorerViewModel Vm = null!;
    }

    private static ChapterData Chap(string guid, string title, int order, string act = "", ChapterStatus status = ChapterStatus.FirstDraft, bool fav = false)
        => new() { Guid = guid, Title = title, Order = order, Act = act, Status = status, IsFavorite = fav };

    private static SceneData Scene(string id, string title, string chapter, bool fav = false)
        => new() { Id = id, Title = title, ChapterGuid = chapter, IsFavorite = fav };

    private static Harness Build(Action<Harness>? setup = null)
    {
        var h = new Harness();
        var proj = Substitute.For<IProjectService>();
        h.Proj = proj;
        setup?.Invoke(h);
        proj.GetChaptersOrdered().Returns(_ => h.Chapters.ToList());
        proj.GetScenesForChapter(Arg.Any<string>()).Returns(ci => h.Scenes.TryGetValue(ci.Arg<string>(), out var s) ? s.ToList() : []);
        proj.GetArchivedScenes().Returns(_ => h.Archived.ToList());
        // mutating async ops
        proj.CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ci =>
        {
            var c = Chap("new-ch", ci.ArgAt<string>(0), h.Chapters.Count + 1);
            h.Chapters.Add(c); h.Scenes[c.Guid] = [];
            return c;
        });
        proj.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci =>
        {
            var sc = Scene("new-sc", ci.ArgAt<string>(1), ci.ArgAt<string>(0));
            if (!h.Scenes.TryGetValue(sc.ChapterGuid, out var l)) { l = []; h.Scenes[sc.ChapterGuid] = l; }
            l.Add(sc);
            return sc;
        });
        foreach (var m in new[] { "SaveProjectAsync", "SaveScenesAsync" }) { }
        proj.SaveProjectAsync().Returns(Task.CompletedTask);
        proj.SaveScenesAsync().Returns(Task.CompletedTask);
        h.Vm = new ExplorerViewModel(proj);
        h.Vm.Refresh();
        return h;
    }

    private static ChapterTreeItemViewModel ChapVm(ExplorerViewModel vm, string guid)
        => vm.ExplorerItems.OfType<ChapterTreeItemViewModel>().First(c => c.Chapter.Guid == guid);

    // ── Refresh / grouping / filters ────────────────────────────────
    [Fact]
    public void Refresh_GroupsByAct_AddsHeaders()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1, act: "Act I"), Chap("c2", "Two", 2, act: "Act I"), Chap("c3", "Three", 3)];
            x.Scenes["c1"] = [Scene("s1", "Sc1", "c1")];
        });
        var headers = h.Vm.ExplorerItems.OfType<ActHeaderViewModel>().ToList();
        Assert.Single(headers); // one "Act I" header
        Assert.Equal(3, h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>().Count());
    }

    [Fact]
    public void Refresh_NonContiguousActs_CollapseUnderSingleHeader()
    {
        // Chapters in order: Act I, Act II, Act I, none. The Act-I header must
        // appear once with both its chapters grouped beneath it, not twice.
        var h = Build(x =>
        {
            x.Chapters =
            [
                Chap("c1", "A", 1, act: "Act I"),
                Chap("c2", "B", 2, act: "Act II"),
                Chap("c3", "C", 3, act: "Act I"),
                Chap("c4", "D", 4),
            ];
        });
        var headers = h.Vm.ExplorerItems.OfType<ActHeaderViewModel>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal(new[] { "Act I", "Act II" }, headers.Select(a => a.ActName));

        // Layout order: no-act first, then Act I (in first-appearance order),
        // then Act II. Validates that the un-acted chapter is at the top and
        // each act group is contiguous.
        var items = h.Vm.ExplorerItems.ToList();
        Assert.IsType<ChapterTreeItemViewModel>(items[0]); // c4 (no act)
        Assert.Equal("c4", ((ChapterTreeItemViewModel)items[0]).Chapter.Guid);
        Assert.IsType<ActHeaderViewModel>(items[1]);
        Assert.Equal("Act I", ((ActHeaderViewModel)items[1]).ActName);
        Assert.Equal("c1", ((ChapterTreeItemViewModel)items[2]).Chapter.Guid);
        Assert.Equal("c3", ((ChapterTreeItemViewModel)items[3]).Chapter.Guid);
        Assert.IsType<ActHeaderViewModel>(items[4]);
        Assert.Equal("Act II", ((ActHeaderViewModel)items[4]).ActName);
        Assert.Equal("c2", ((ChapterTreeItemViewModel)items[5]).Chapter.Guid);
    }

    [Fact]
    public void Refresh_ActWithTrailingWhitespace_GroupsWithTrimmedTwin()
    {
        // Two chapters share the same act except one stored with trailing
        // whitespace. Trim + case-insensitive grouping collapses them.
        var h = Build(x =>
        {
            x.Chapters =
            [
                Chap("c1", "A", 1, act: "Act I "),
                Chap("c2", "B", 2, act: "act i"),
            ];
        });
        Assert.Single(h.Vm.ExplorerItems.OfType<ActHeaderViewModel>());
    }

    [Fact]
    public void Refresh_FavoritesAndSearchFilter()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "Alpha", 1), Chap("c2", "Beta", 2, fav: true)];
            x.Scenes["c1"] = [Scene("s1", "needle", "c1")];
            x.Scenes["c2"] = [Scene("s2", "plain", "c2")];
        });

        h.Vm.SearchQuery = "needle"; // scene title match -> only c1 with that scene
        Assert.Contains(h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>(), c => c.Chapter.Guid == "c1");
        Assert.DoesNotContain(h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>(), c => c.Chapter.Guid == "c2");

        h.Vm.SearchQuery = "alph"; // chapter title match
        Assert.Contains(h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>(), c => c.Chapter.Guid == "c1");

        h.Vm.SearchQuery = string.Empty;
        h.Vm.ToggleFavoritesOnlyFilterCommand.Execute(null);
        Assert.True(h.Vm.ShowFavoritesOnly);
        // Only favorite chapter c2 remains.
        Assert.DoesNotContain(h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>(), c => c.Chapter.Guid == "c1");
    }

    [Fact]
    public void RefreshArchived_BuildsWithOriginAndOrder()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1)];
            x.Archived =
            [
                new SceneData { Id = "a1", Title = "Old", OriginChapterGuid = "c1", ArchivedAt = DateTime.UtcNow.AddDays(-2) },
                new SceneData { Id = "a2", Title = "New", OriginChapterGuid = "gone", ArchivedAt = DateTime.UtcNow },
            ];
        });
        Assert.True(h.Vm.HasArchivedScenes);
        Assert.Equal("New", h.Vm.ArchivedScenes[0].DisplayName); // newest first
        Assert.Equal("One", h.Vm.ArchivedScenes.First(a => a.Scene.Id == "a1").OriginLabel);
        Assert.Equal(string.Empty, h.Vm.ArchivedScenes.First(a => a.Scene.Id == "a2").OriginLabel); // origin gone
    }

    // ── Smart lists ─────────────────────────────────────────────────
    [Fact]
    public async Task SmartLists_CrudViaService()
    {
        var h = Build(x => x.Chapters = [Chap("c1", "One", 1)]);
        var svc = Substitute.For<ISmartListService>();
        var lists = new List<SmartList> { new() { Id = "l1", Name = "Drafts" } };
        svc.GetAll().Returns(_ => lists.ToList());
        svc.EvaluateAsync(Arg.Any<SmartList>())
           .Returns(new List<(ChapterData Chapter, SceneData Scene)> { (h.Chapters[0], Scene("sm", "Hit", "c1")) });
        h.Vm.AttachSmartListService(svc);
        Assert.Single(h.Vm.SmartLists);

        // Smart-list scene open routes through to SceneOpenRequested.
        ChapterData? opened = null;
        h.Vm.SceneOpenRequested += (c, _) => opened = c;
        await h.Vm.SmartLists[0].RefreshCommand.ExecuteAsync(null);
        h.Vm.SmartLists[0].Matches[0].OpenCommand.Execute(null);
        Assert.NotNull(opened);

        await h.Vm.CreateSmartListCommand.ExecuteAsync(null); // editor null -> no-op
        h.Vm.ShowSmartListEditor = _ => Task.FromResult<SmartList?>(new SmartList { Id = "l2", Name = "New" });
        await h.Vm.CreateSmartListCommand.ExecuteAsync(null);
        await svc.Received().SaveAsync(Arg.Is<SmartList>(s => s.Id == "l2"));

        await h.Vm.EditSmartListCommand.ExecuteAsync(h.Vm.SmartLists[0]);

        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.DeleteSmartListCommand.ExecuteAsync(h.Vm.SmartLists[0]);
        await svc.Received().DeleteAsync("l1");
    }

    // ── Archive commands ────────────────────────────────────────────
    [Fact]
    public async Task Archive_Restore_Delete_Toggle_Open()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1)];
            x.Scenes["c1"] = [Scene("s1", "Sc", "c1")];
        });
        var sceneVm = ChapVm(h.Vm, "c1").Scenes[0];
        h.Vm.HandleSceneSelection(sceneVm, false, false, false);
        await h.Vm.ArchiveSelectedSceneCommand.ExecuteAsync(null);
        await h.Proj.Received().ArchiveSceneAsync("c1", "s1");

        var archived = new SceneData { Id = "a1", Title = "Arc", OriginChapterGuid = "c1" };
        h.Archived = [archived];
        h.Vm.Refresh();
        var arcVm = h.Vm.ArchivedScenes[0];

        SceneData? opened = null;
        h.Vm.ArchivedSceneOpenRequested += s => opened = s;
        h.Vm.OpenArchivedSceneCommand.Execute(arcVm);
        Assert.Same(archived, opened);

        h.Vm.ToggleArchiveCommand.Execute(null);
        Assert.True(h.Vm.IsArchiveExpanded);

        await h.Vm.RestoreArchivedSceneCommand.ExecuteAsync(arcVm);
        await h.Proj.Received().RestoreArchivedSceneAsync("a1", "c1", null);

        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.DeleteArchivedSceneCommand.ExecuteAsync(arcVm);
        await h.Proj.Received().DeleteArchivedSceneAsync("a1");

        await h.Vm.RestoreArchivedSceneCommand.ExecuteAsync(null); // null no-op
        await h.Vm.DeleteArchivedSceneCommand.ExecuteAsync(null);
        h.Vm.OpenArchivedSceneCommand.Execute(null);
    }

    [Fact]
    public async Task Restore_OriginGone_FallsBackToFirstChapter()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1)];
            x.Archived = [new SceneData { Id = "a1", Title = "Arc", OriginChapterGuid = "deleted" }];
        });
        await h.Vm.RestoreArchivedSceneCommand.ExecuteAsync(h.Vm.ArchivedScenes[0]);
        await h.Proj.Received().RestoreArchivedSceneAsync("a1", "c1", null); // fell back to c1
    }

    // ── Create / rename / delete ────────────────────────────────────
    [Fact]
    public async Task CreateChapter_DialogGuards_AndSuccess()
    {
        var h = Build(x => x.Chapters = [Chap("c1", "One", 1)]);
        await h.Vm.CreateChapterCommand.ExecuteAsync(null); // dialog null -> no-op
        await h.Proj.DidNotReceive().CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>());

        h.Vm.ShowChapterDialog = () => Task.FromResult<ChapterDialogResult?>(new ChapterDialogResult("", ""));
        await h.Vm.CreateChapterCommand.ExecuteAsync(null); // empty title -> no-op
        await h.Proj.DidNotReceive().CreateChapterAsync(Arg.Any<string>(), Arg.Any<string>());

        h.Vm.ShowChapterDialog = () => Task.FromResult<ChapterDialogResult?>(new ChapterDialogResult("Fresh", "2026-01-01"));
        await h.Vm.CreateChapterCommand.ExecuteAsync(null);
        await h.Proj.Received().CreateChapterAsync("Fresh", "2026-01-01");
        Assert.Contains(h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>(), c => c.Chapter.Title == "Fresh");
    }

    [Fact]
    public async Task CreateScene_GuardsAndSuccess()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; x.Scenes["c1"] = []; });
        await h.Vm.CreateSceneCommand.ExecuteAsync(null); // no dialog -> no-op (chapters exist)
        h.Vm.ShowSceneDialog = _ => Task.FromResult<SceneDialogResult?>(new SceneDialogResult("Scene A", "c1", ""));
        await h.Vm.CreateSceneCommand.ExecuteAsync(null);
        await h.Proj.Received().CreateSceneAsync("c1", "Scene A", "");
    }

    [Fact]
    public async Task CreateScene_NoChapters_NoOp()
    {
        var h = Build();
        h.Vm.ShowSceneDialog = _ => Task.FromResult<SceneDialogResult?>(new SceneDialogResult("X", "c1", ""));
        await h.Vm.CreateSceneCommand.ExecuteAsync(null);
        await h.Proj.DidNotReceive().CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Rename_Chapter_Scene_GuardsAndSuccess()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; x.Scenes["c1"] = [Scene("s1", "Sc", "c1")]; });
        var chVm = ChapVm(h.Vm, "c1");
        var scVm = chVm.Scenes[0];

        await h.Vm.RenameChapterCommand.ExecuteAsync(chVm); // no dialog -> null -> no-op
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("One"); // unchanged
        await h.Vm.RenameChapterCommand.ExecuteAsync(chVm);
        await h.Proj.DidNotReceive().RenameChapterAsync(Arg.Any<string>(), Arg.Any<string>());

        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Renamed");
        await h.Vm.RenameChapterCommand.ExecuteAsync(chVm);
        await h.Proj.Received().RenameChapterAsync("c1", "Renamed");

        await h.Vm.RenameSceneCommand.ExecuteAsync(scVm);
        await h.Proj.Received().RenameSceneAsync("c1", "s1", "Renamed");
    }

    [Fact]
    public async Task Delete_Chapter_Scene_UseSelectionOrFallback()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; x.Scenes["c1"] = [Scene("s1", "Sc", "c1")]; });
        var chVm = ChapVm(h.Vm, "c1");
        h.Vm.HandleChapterSelection(chVm, false, false);
        await h.Vm.DeleteChapterCommand.ExecuteAsync(null);
        await h.Proj.Received().DeleteChapterAsync("c1");

        var h2 = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; x.Scenes["c1"] = [Scene("s1", "Sc", "c1")]; });
        var scVm = ChapVm(h2.Vm, "c1").Scenes[0];
        h2.Vm.HandleSceneSelection(scVm, false, false, false);
        await h2.Vm.DeleteSceneCommand.ExecuteAsync(null);
        await h2.Proj.Received().DeleteSceneAsync("c1", "s1");
    }

    // ── Dates / status / favorites / label ──────────────────────────
    [Fact]
    public async Task SetChapterDate_And_SceneDate_RangeAndCancel()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; x.Scenes["c1"] = [Scene("s1", "Sc", "c1")]; });
        var chVm = ChapVm(h.Vm, "c1");
        var scVm = chVm.Scenes[0];

        h.Vm.ShowDateRangeDialog = (_, _) => Task.FromResult(new ExplorerViewModel.DateRangeDialogResult(true, null));
        await h.Vm.SetChapterDateCommand.ExecuteAsync(chVm); // cancelled
        await h.Proj.DidNotReceive().SetChapterDateRangeAsync(Arg.Any<string>(), Arg.Any<StoryDateRange?>());

        h.Vm.ShowDateRangeDialog = (_, _) => Task.FromResult(new ExplorerViewModel.DateRangeDialogResult(false, new StoryDateRange { Start = "2026-05-01" }));
        await h.Vm.SetChapterDateCommand.ExecuteAsync(chVm);
        await h.Proj.Received().SetChapterDateRangeAsync("c1", Arg.Any<StoryDateRange?>());
        Assert.Equal("2026-05-01", chVm.Chapter.Date);

        await h.Vm.SetSceneDateCommand.ExecuteAsync(scVm);
        await h.Proj.Received().SetSceneDateRangeAsync("c1", "s1", Arg.Any<StoryDateRange?>());
    }

    [Fact]
    public async Task CycleStatus_Favorites_LabelColor()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1, status: ChapterStatus.Outline)]; x.Scenes["c1"] = [Scene("s1", "Sc", "c1")]; });
        var chVm = ChapVm(h.Vm, "c1");
        var scVm = chVm.Scenes[0];

        await h.Vm.CycleChapterStatusCommand.ExecuteAsync(chVm);
        Assert.Equal(ChapterStatus.FirstDraft, chVm.Chapter.Status);

        await h.Vm.ToggleChapterFavoriteCommand.ExecuteAsync(chVm);
        await h.Proj.Received().SetChapterFavoriteAsync("c1", true);
        await h.Vm.ToggleSceneFavoriteCommand.ExecuteAsync(scVm);
        await h.Proj.Received().SetSceneFavoriteAsync("c1", "s1", true);

        await h.Vm.SetSceneLabelColorAsync(scVm, "#abc");
        await h.Proj.Received().SetSceneLabelColorAsync("c1", "s1", "#abc");
        Assert.Equal("#abc", scVm.LabelColor);
        await h.Vm.SetSceneLabelColorAsync(null, "#x"); // null no-op
    }

    // ── Acts ────────────────────────────────────────────────────────
    [Fact]
    public async Task Acts_Create_Rename_Delete_SetRemove()
    {
        var h = Build(x => x.Chapters = [Chap("c1", "One", 1, act: "Act I"), Chap("c2", "Two", 2)]);
        var c2 = ChapVm(h.Vm, "c2");

        // CreateAct assigns to selected chapter
        h.Vm.HandleChapterSelection(c2, false, false);
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Act II");
        await h.Vm.CreateActCommand.ExecuteAsync(null);
        Assert.Equal("Act II", c2.Chapter.Act);

        // duplicate act name -> ignored
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Act I");
        await h.Vm.CreateActCommand.ExecuteAsync(null);

        var actVm = h.Vm.ExplorerItems.OfType<ActHeaderViewModel>().First(a => a.ActName == "Act I");
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Prologue");
        await h.Vm.RenameActCommand.ExecuteAsync(actVm);
        Assert.Equal("Prologue", ChapVm(h.Vm, "c1").Chapter.Act);

        var actVm2 = h.Vm.ExplorerItems.OfType<ActHeaderViewModel>().First();
        await h.Vm.DeleteActCommand.ExecuteAsync(actVm2);

        var anyCh = ChapVm(h.Vm, "c1");
        h.Vm.ShowAutoCompleteInputDialog = (_, _, _) => Task.FromResult<string?>("Act Z");
        await h.Vm.SetChapterActCommand.ExecuteAsync(anyCh);
        Assert.Equal("Act Z", anyCh.Chapter.Act);

        await h.Vm.RemoveChapterFromActCommand.ExecuteAsync(anyCh);
        Assert.Equal(string.Empty, anyCh.Chapter.Act);

        await h.Vm.RenameActCommand.ExecuteAsync(null); // null no-op
        await h.Vm.DeleteActCommand.ExecuteAsync(null);
    }

    // ── Selection (ctrl/shift) ──────────────────────────────────────
    [Fact]
    public void ChapterSelection_Single_Ctrl_Shift()
    {
        var h = Build(x => x.Chapters = [Chap("c1", "A", 1), Chap("c2", "B", 2), Chap("c3", "C", 3)]);
        var items = h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>().ToList();

        h.Vm.HandleChapterSelection(items[0], false, false);
        Assert.True(items[0].IsSelected);

        h.Vm.HandleChapterSelection(items[1], ctrl: true, shift: false);
        Assert.True(items[1].IsSelected);
        h.Vm.HandleChapterSelection(items[1], ctrl: true, shift: false); // toggle off
        Assert.False(items[1].IsSelected);

        // Re-anchor on c1 cleanly (select a different chapter first so this isn't a
        // re-click of the sole selection, which would only toggle expand).
        h.Vm.HandleChapterSelection(items[1], false, false);
        h.Vm.HandleChapterSelection(items[0], false, false); // anchor = c1
        h.Vm.HandleChapterSelection(items[2], ctrl: false, shift: true); // range 0..2
        Assert.All(items, i => Assert.True(i.IsSelected));

        // re-click selected single -> toggles expand
        h.Vm.HandleChapterSelection(items[0], false, false);
        var exp = items[0].IsExpanded;
        h.Vm.HandleChapterSelection(items[0], false, false);
        Assert.NotEqual(exp, items[0].IsExpanded);
    }

    [Fact]
    public void SceneSelection_Single_Ctrl_Shift_AndOpen()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "A", 1)];
            x.Scenes["c1"] = [Scene("s1", "S1", "c1"), Scene("s2", "S2", "c1"), Scene("s3", "S3", "c1")];
        });
        var scenes = ChapVm(h.Vm, "c1").Scenes.ToList();
        ChapterData? openedCh = null;
        h.Vm.SceneOpenRequested += (c, _) => openedCh = c;

        h.Vm.HandleSceneSelection(scenes[0], false, false, openScene: true);
        Assert.True(scenes[0].IsSelected);
        Assert.NotNull(openedCh);

        h.Vm.HandleSceneSelection(scenes[1], ctrl: true, shift: false, openScene: false);
        Assert.True(scenes[1].IsSelected);
        h.Vm.HandleSceneSelection(scenes[1], ctrl: true, shift: false, openScene: false);
        Assert.False(scenes[1].IsSelected);

        h.Vm.HandleSceneSelection(scenes[0], false, false, false);
        h.Vm.HandleSceneSelection(scenes[2], ctrl: false, shift: true, openScene: false);
        Assert.All(scenes, s => Assert.True(s.IsSelected));

        // shift with an anchor scene not in the current visual list -> single fallback
        h.Vm.HandleSceneSelection(scenes[0], false, false, false); // anchor s1
        var stray = new SceneTreeItemViewModel(Scene("zzz", "Z", "c1"), h.Chapters[0]);
        h.Vm.HandleSceneSelection(stray, ctrl: false, shift: true, openScene: false);
        Assert.True(stray.IsSelected);
    }

    [Fact]
    public void SelectChapterCommand_And_SelectSceneCommand()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "A", 1)]; x.Scenes["c1"] = [Scene("s1", "S1", "c1")]; });
        var ch = ChapVm(h.Vm, "c1");
        h.Vm.SelectChapterCommand.Execute(ch);
        Assert.Same(ch, h.Vm.SelectedChapter);
        h.Vm.SelectChapterCommand.Execute(ch); // re-click toggles expand
        var sc = ch.Scenes[0];
        h.Vm.SelectSceneCommand.Execute(sc);
        Assert.Same(sc, h.Vm.SelectedScene);
        h.Vm.SetTabCommand.Execute("Scenes");
        Assert.Equal("Scenes", h.Vm.ActiveTab);
    }

    // ── Drag / move / navigate ──────────────────────────────────────
    [Fact]
    public async Task Drag_Move_Navigate()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "A", 1), Chap("c2", "B", 2)];
            x.Scenes["c1"] = [Scene("s1", "S1", "c1"), Scene("s2", "S2", "c1")];
            x.Scenes["c2"] = [Scene("s3", "S3", "c2")];
        });
        var c1 = ChapVm(h.Vm, "c1");
        var dragCh = h.Vm.PrepareChapterDrag(c1);
        Assert.Contains(c1, dragCh);

        var sc1 = c1.Scenes[0];
        var dragSc = h.Vm.PrepareSceneDrag(sc1);
        Assert.Contains(sc1, dragSc);

        await h.Vm.MoveChaptersBeforeAsync(["c1"], "c2");
        await h.Proj.Received().MoveChaptersAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>());
        await h.Vm.MoveChaptersBeforeAsync(["c1"], "ghost"); // target not found -> no extra move

        await h.Vm.MoveScenesBeforeAsync(["s1"], "s2", "c1");
        await h.Proj.Received().MoveScenesAsync(Arg.Any<IReadOnlyList<string>>(), "c1", Arg.Any<int>());

        await h.Vm.MoveScenesToChapterAsync(["s1"], "c2");
        await h.Proj.Received().MoveScenesAsync(Arg.Any<IReadOnlyList<string>>(), "c2", Arg.Any<int>());

        // Navigate
        var scenes = ChapVm(h.Vm, "c1").Scenes;
        h.Vm.HandleSceneSelection(scenes[0], false, false, false);
        h.Vm.NavigateScene(1); // next
        h.Vm.NavigateScene(-1); // prev
        h.Vm.NavigateScene(-1); // out of bounds -> no-op
    }

    [Fact]
    public void NavigateScene_NoScenes_NoOp()
    {
        var h = Build();
        h.Vm.NavigateScene(1); // no scenes
    }

    [Fact]
    public void ShiftSelection_AnchorMissing_FallsBackToSingle()
    {
        var h = Build(x => x.Chapters = [Chap("c1", "A", 1), Chap("c2", "B", 2)]);
        var items = h.Vm.ExplorerItems.OfType<ChapterTreeItemViewModel>().ToList();
        h.Vm.HandleChapterSelection(items[0], false, false); // anchor c1
        h.Vm.Refresh(); // rebuild -> item objects replaced, anchor guid still c1 (present)
        // shift to a brand-new chapter VM not in current list
        var stray = new ChapterTreeItemViewModel(Chap("zzz", "Z", 9));
        h.Vm.HandleChapterSelection(stray, ctrl: false, shift: true); // end == -1 -> single
        Assert.True(stray.IsSelected);
    }

    // ── Sub view models ─────────────────────────────────────────────
    [Theory]
    [InlineData(ChapterStatus.Outline)]
    [InlineData(ChapterStatus.FirstDraft)]
    [InlineData(ChapterStatus.Revised)]
    [InlineData(ChapterStatus.Edited)]
    [InlineData(ChapterStatus.Final)]
    [InlineData((ChapterStatus)99)] // out-of-range -> default arm
    public void ChapterTreeItem_StatusDisplay_AllStatuses(ChapterStatus status)
    {
        var vm = new ChapterTreeItemViewModel(Chap("c", "T", 1, status: status));
        Assert.False(string.IsNullOrEmpty(vm.StatusDisplay));
    }

    [Fact]
    public void ChapterTreeItem_DisplaysAndGitStatus()
    {
        var ch = Chap("c", "Title", 2, act: "Act", fav: true);
        ch.Date = "2026-01-01";
        var vm = new ChapterTreeItemViewModel(ch);
        Assert.True(vm.HasAct);
        Assert.True(vm.HasDate);
        Assert.True(vm.IsFavorite);
        Assert.False(string.IsNullOrEmpty(vm.DateDisplay));
        Assert.Equal("2. Title", vm.DisplayName);
        ch.Title = "Changed";
        vm.RefreshDisplay();
        Assert.Equal("2. Changed", vm.DisplayName);

        var scene = new SceneTreeItemViewModel(Scene("s", "S", "c"), ch) { HasGitChanges = true };
        vm.Scenes.Add(scene);
        vm.RefreshGitStatus();
        Assert.True(vm.HasGitChanges);
        Assert.False(string.IsNullOrEmpty(vm.GitStatusLabel));
    }

    [Fact]
    public void SceneTreeItem_Displays()
    {
        var sc = Scene("s", "Scene", "c", fav: true);
        sc.Date = "2026-02-02";
        sc.LabelColor = "#fff";
        var vm = new SceneTreeItemViewModel(sc, Chap("c", "Ch", 1));
        Assert.True(vm.HasDate);
        Assert.False(string.IsNullOrEmpty(vm.DateDisplay));
        Assert.True(vm.IsFavorite);
        Assert.True(vm.HasLabelColor);
        Assert.Equal("#fff", vm.LabelColor);
        sc.Title = "New";
        vm.RefreshDisplay();
        Assert.Equal("New", vm.DisplayName);
    }

    [Fact]
    public void ArchivedAndActHeader_SubVms()
    {
        var arc = new ArchivedSceneItemViewModel(new SceneData { Title = "Arc", ArchivedAt = new DateTime(2026, 1, 2) }, "Origin");
        Assert.Equal("Arc", arc.DisplayName);
        Assert.Equal("Origin", arc.OriginLabel);
        Assert.False(string.IsNullOrEmpty(arc.ArchivedAtDisplay));
        var arc2 = new ArchivedSceneItemViewModel(new SceneData { Title = "X" }, null);
        Assert.Equal(string.Empty, arc2.ArchivedAtDisplay);

        var act = new ActHeaderViewModel("act one");
        Assert.Equal("ACT ONE", act.DisplayName);
        act.RefreshDisplay();
        Assert.Equal("ACT ONE", act.DisplayName);
    }

    // ── Single-selection fallbacks (no multi-select) ────────────────
    [Fact]
    public async Task ArchiveDeleteScene_DeleteChapter_FallBackToSelected()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1)];
            x.Scenes["c1"] = [Scene("s1", "Sc1", "c1")];
        });
        h.Proj.ArchiveSceneAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.DeleteSceneAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.DeleteChapterAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        var ch = ChapVm(h.Vm, "c1");

        h.Vm.SelectedScene = ch.Scenes[0]; // no multi-select -> fallback to SelectedScene
        await h.Vm.ArchiveSelectedSceneCommand.ExecuteAsync(null);
        await h.Proj.Received().ArchiveSceneAsync("c1", "s1");

        ch = ChapVm(h.Vm, "c1"); // Refresh cleared selections; re-seed
        h.Vm.SelectedScene = ch.Scenes[0];
        await h.Vm.DeleteSceneCommand.ExecuteAsync(null);
        await h.Proj.Received().DeleteSceneAsync("c1", "s1");

        h.Vm.SelectedChapter = ChapVm(h.Vm, "c1");
        await h.Vm.DeleteChapterCommand.ExecuteAsync(null);
        await h.Proj.Received().DeleteChapterAsync("c1");
    }

    // ── Date range cleared (no Start) ───────────────────────────────
    [Fact]
    public async Task SetChapterAndSceneDate_NullRange_ClearsDate()
    {
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1)];
            x.Scenes["c1"] = [Scene("s1", "Sc1", "c1")];
        });
        h.Proj.SetChapterDateRangeAsync(Arg.Any<string>(), Arg.Any<StoryDateRange?>()).Returns(Task.CompletedTask);
        h.Proj.SetSceneDateRangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<StoryDateRange?>()).Returns(Task.CompletedTask);
        // non-cancelled result with no range -> Date cleared (the else branch)
        h.Vm.ShowDateRangeDialog = (_, _) => Task.FromResult(new ExplorerViewModel.DateRangeDialogResult(false, null));

        var ch = ChapVm(h.Vm, "c1");
        ch.Chapter.Date = "2020-01-01";
        await h.Vm.SetChapterDateCommand.ExecuteAsync(ch);
        Assert.Equal(string.Empty, ch.Chapter.Date);

        var sc = ch.Scenes[0];
        sc.Scene.Date = "2020-01-01";
        await h.Vm.SetSceneDateCommand.ExecuteAsync(sc);
        Assert.Equal(string.Empty, sc.Scene.Date);
    }

    // ── Set act via auto-complete dialog ────────────────────────────
    [Fact]
    public async Task SetChapterAct_ViaAutoCompleteDialog()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1)]; });
        h.Proj.SaveProjectAsync().Returns(Task.CompletedTask);
        h.Vm.ShowAutoCompleteInputDialog = (_, _, _) => Task.FromResult<string?>("Act II");
        var ch = ChapVm(h.Vm, "c1");
        await h.Vm.SetChapterActCommand.ExecuteAsync(ch);
        Assert.Equal("Act II", ch.Chapter.Act);
    }

    [Fact]
    public async Task SetChapterAct_NoDialogHook_NoOp()
    {
        var h = Build(x => { x.Chapters = [Chap("c1", "One", 1, act: "Act I")]; });
        var ch = ChapVm(h.Vm, "c1");
        await h.Vm.SetChapterActCommand.ExecuteAsync(ch); // RequestAutoCompleteInput -> null hook -> null
        Assert.Equal("Act I", ch.Chapter.Act); // unchanged
    }

    [Fact]
    public async Task SetChapterAct_PassesExistingActsAsSuggestions()
    {
        // Regression: the Set Act dialog must offer existing acts as
        // suggestions so the user can pick one instead of being forced to
        // retype an existing name (which previously was the only obvious
        // path because the autocomplete dropdown was hidden behind a typed
        // prefix). Existing acts come from both chapter assignments AND any
        // orphan acts tracked on the active book.
        var orphanActs = new List<ActData>
        {
            new() { Name = "Prologue" },
            new() { Name = "Epilogue" },
        };
        var book = new BookData { Acts = orphanActs };
        var h = Build(x =>
        {
            x.Chapters = [Chap("c1", "One", 1, act: "Act I"), Chap("c2", "Two", 2, act: "Act II"), Chap("c3", "Three", 3)];
        });
        h.Proj.ActiveBook.Returns(book);

        IReadOnlyList<string>? captured = null;
        h.Vm.ShowAutoCompleteInputDialog = (_, _, s) =>
        {
            captured = s;
            return Task.FromResult<string?>("Act I"); // pick an existing act
        };
        var ch = ChapVm(h.Vm, "c3");
        await h.Vm.SetChapterActCommand.ExecuteAsync(ch);

        Assert.NotNull(captured);
        Assert.Contains("Act I", captured);
        Assert.Contains("Act II", captured);
        Assert.Contains("Prologue", captured);
        Assert.Contains("Epilogue", captured);
        Assert.Equal("Act I", ch.Chapter.Act); // picking existing applied
    }
}
