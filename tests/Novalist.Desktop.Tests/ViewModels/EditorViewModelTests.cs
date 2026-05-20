using Avalonia.Media;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class EditorViewModelTests
{
    static EditorViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public IProjectService Proj = null!;
        public ISettingsService Settings = null!;
        public IEntityService Entity = null!;
        public AppSettings App = null!;
        public ProjectSettings ProjSettings = null!;
        public EditorViewModel Vm = null!;
        public Dictionary<string, string> Disk = new(StringComparer.OrdinalIgnoreCase);
    }

    private static H Build(bool loaded = false)
    {
        var h = new H();
        h.App = new AppSettings { EditorFontFamily = "Georgia", EditorFontSize = 14 };
        h.Settings = Substitute.For<ISettingsService>();
        h.Settings.Settings.Returns(h.App);
        h.Settings.Effective.Returns(h.App);
        h.Settings.SaveAsync().Returns(Task.CompletedTask);

        h.ProjSettings = new ProjectSettings { Overrides = new SettingsOverrides() };
        h.Proj = Substitute.For<IProjectService>();
        h.Proj.IsProjectLoaded.Returns(loaded);
        h.Proj.ProjectSettings.Returns(h.ProjSettings);
        h.Proj.SaveProjectSettingsAsync().Returns(Task.CompletedTask);
        h.Proj.SaveScenesAsync().Returns(Task.CompletedTask);
        h.Proj.WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/scene.html");
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>())
            .Returns(ci => Task.FromResult(h.Disk.TryGetValue(((SceneData)ci[1]).Id, out var c) ? c : "disk content"));
        h.Proj.ReadArchivedSceneContentAsync(Arg.Any<SceneData>()).Returns(Task.FromResult("archived content"));
        h.Proj.ActiveBook.Returns(new BookData { Id = "book1" });

        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());

        h.Vm = new EditorViewModel(h.Proj, h.Settings, h.Entity);
        h.Vm.AutoSaveDelayMs = 0; // disable debounce by default; opt back in per test
        return h;
    }

    private static (ChapterData, SceneData) Scene(string id = "s1", string title = "Scene", string? content = null, H? h = null)
    {
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter" };
        var sc = new SceneData { Id = id, Title = title, Order = 0 };
        if (h != null && content != null) h.Disk[id] = content;
        return (ch, sc);
    }

    // ── Construction & settings-derived props ───────────────────────
    [AvaloniaFact]
    public void Constructs_ExposesSettingsProps()
    {
        var h = Build();
        Assert.Equal("Georgia", h.Vm.EditorFontFamily);
        Assert.Equal(14, h.Vm.EditorFontSize);
        Assert.False(h.Vm.IsDocumentOpen);
        Assert.False(h.Vm.HasOpenScenes);
        Assert.Equal("middle", h.Vm.TypewriterScrollAnchor);
        _ = h.Vm.BookEditorWidth;
        _ = h.Vm.BookParagraphSpacingEnabled;
        _ = h.Vm.BookWidthEnabled;
        _ = h.Vm.PageViewEnabled;
        _ = h.Vm.TypewriterScrollEnabled;
    }

    [AvaloniaFact]
    public void ApplySettings_NotifiesAndUpdatesStats()
    {
        var h = Build();
        h.App.DialogueCorrectionEnabled = true;
        h.App.GrammarCheckEnabled = true;
        h.Vm.ApplySettings();
        Assert.True(h.Vm.DialogueCorrection.Enabled);
        Assert.True(h.Vm.GrammarCheck.Enabled);
    }

    [AvaloniaFact]
    public void FormattingCommands_InvokeHooks_AndState()
    {
        var h = Build();
        var hits = new List<string>();
        h.Vm.ToggleBoldAction = () => hits.Add("b");
        h.Vm.ToggleItalicAction = () => hits.Add("i");
        h.Vm.ToggleUnderlineAction = () => hits.Add("u");
        h.Vm.AlignLeftAction = () => hits.Add("l");
        h.Vm.AlignCenterAction = () => hits.Add("c");
        h.Vm.AlignRightAction = () => hits.Add("r");
        h.Vm.AlignJustifyAction = () => hits.Add("j");
        h.Vm.ToggleBoldCommand.Execute(null);
        h.Vm.ToggleItalicCommand.Execute(null);
        h.Vm.ToggleUnderlineCommand.Execute(null);
        h.Vm.AlignLeftCommand.Execute(null);
        h.Vm.AlignCenterCommand.Execute(null);
        h.Vm.AlignRightCommand.Execute(null);
        h.Vm.AlignJustifyCommand.Execute(null);
        Assert.Equal(new[] { "b", "i", "u", "l", "c", "r", "j" }, hits);

        h.Vm.UpdateFormattingState(true, true, false, TextAlignment.Center);
        Assert.True(h.Vm.IsBoldActive);
        Assert.True(h.Vm.IsItalicActive);
        Assert.False(h.Vm.IsUnderlineActive);
        Assert.True(h.Vm.IsAlignCenter);
    }

    [AvaloniaFact]
    public void SetFontSize_Global_WhenNoOverride()
    {
        var h = Build(loaded: false);
        h.Vm.SetFontSize(99); // clamped to 36, written to global
        Assert.Equal(36, h.App.EditorFontSize);
        h.Settings.Received().SaveAsync();
    }

    [AvaloniaFact]
    public void SetFontSize_Override_WhenProjectScoped()
    {
        var h = Build(loaded: true);
        h.ProjSettings.Overrides!.EditorFontSize = 12; // editor section project-scoped
        h.Vm.SetFontSize(5); // clamped to 8, written to override
        Assert.Equal(8, h.ProjSettings.Overrides.EditorFontSize);
        h.Proj.Received().SaveProjectSettingsAsync();
    }

    // ── Tabs / open / activate ──────────────────────────────────────
    [AvaloniaFact]
    public async Task OpenScene_NewTab_LoadsContent()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "<p>Hello world here.</p>", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        Assert.True(h.Vm.IsDocumentOpen);
        Assert.True(h.Vm.HasOpenScenes);
        Assert.Equal(sc, h.Vm.CurrentScene);
        Assert.Equal(ch, h.Vm.CurrentChapter);
        Assert.True(h.Vm.WordCount > 0); // stats computed off stripped html
        Assert.Contains("Chapter", h.Vm.DocumentTitle);
    }

    [AvaloniaFact]
    public async Task OpenScene_ExistingTab_Switches()
    {
        var h = Build();
        var (ch, s1) = Scene("s1", content: "one", h: h);
        var s2 = new SceneData { Id = "s2", Title = "Two", Order = 1 };
        h.Disk["s2"] = "two";
        await h.Vm.OpenSceneAsync(ch, s1);
        await h.Vm.OpenSceneAsync(ch, s2);
        Assert.True(h.Vm.HasMultipleTabs);
        await h.Vm.OpenSceneAsync(ch, s1); // existing -> switch back
        Assert.Equal(s1, h.Vm.CurrentScene);
        Assert.Equal(2, h.Vm.OpenScenes.Count);
    }

    [AvaloniaFact]
    public async Task OpenScene_Archived_ReadsArchivedContent()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "C" };
        var sc = new SceneData { Id = "a1", Title = "Arch", ArchivedAt = DateTime.UtcNow };
        await h.Vm.OpenSceneAsync(ch, sc);
        Assert.True(h.Vm.IsCurrentSceneArchived);
        Assert.Equal("archived content", h.Vm.Content);
    }

    [AvaloniaFact]
    public async Task ActivateTab_FromUi_Command()
    {
        var h = Build();
        var (ch, s1) = Scene("s1", content: "one", h: h);
        var s2 = new SceneData { Id = "s2", Order = 1 };
        h.Disk["s2"] = "two";
        await h.Vm.OpenSceneAsync(ch, s1);
        await h.Vm.OpenSceneAsync(ch, s2);
        await h.Vm.ActivateTabFromUiCommand.ExecuteAsync(h.Vm.OpenScenes[0]);
        Assert.Equal(s1, h.Vm.CurrentScene);
    }

    // ── Text change / dirty / stats / save ──────────────────────────
    [AvaloniaFact]
    public async Task OnTextChanged_MarksDirty_AndSave()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "orig", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        SceneSavedSetup(h, out var savedFired);

        h.Vm.OnTextChanged("<p>new text added here</p>", "new text added here");
        Assert.True(h.Vm.IsDirty);
        await h.Vm.SaveAsync();
        Assert.False(h.Vm.IsDirty);
        await h.Proj.Received().WriteSceneContentAsync(ch, sc, Arg.Any<string>());
        await h.Proj.Received().SaveScenesAsync();
        Assert.True(savedFired());
    }

    private static void SceneSavedSetup(H h, out Func<bool> fired)
    {
        var f = false;
        h.Vm.SceneSaved += (_, _) => f = true;
        fired = () => f;
    }

    [AvaloniaFact]
    public async Task Save_NotDirty_NoOp()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "x", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        await h.Vm.SaveAsync(); // not dirty
        await h.Proj.DidNotReceive().WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task Save_Archived_DropsDirtyWithoutWrite()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "C" };
        var sc = new SceneData { Id = "a1", ArchivedAt = DateTime.UtcNow };
        await h.Vm.OpenSceneAsync(ch, sc);
        h.Vm.OnTextChanged("<p>edited</p>", "edited");
        await h.Vm.SaveAsync();
        Assert.False(h.Vm.IsDirty);
        await h.Proj.DidNotReceive().WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public void OnCaretPositionChanged_Updates()
    {
        var h = Build();
        h.Vm.OnCaretPositionChanged(5, 12);
        Assert.Equal(5, h.Vm.CaretLine);
        Assert.Equal(12, h.Vm.CaretColumn);
    }

    [AvaloniaFact]
    public async Task ReloadCurrentScene_ReplacesContent()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "first", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        h.Disk["s1"] = "reloaded text";
        await h.Vm.ReloadCurrentSceneAsync();
        Assert.Equal("reloaded text", h.Vm.Content);
        Assert.False(h.Vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task ReloadCurrentScene_NoScene_NoOp()
    {
        var h = Build();
        await h.Vm.ReloadCurrentSceneAsync(); // no scene -> returns
        Assert.False(h.Vm.IsDocumentOpen);
    }

    // ── Close tabs ──────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task CloseTab_Active_ActivatesNext()
    {
        var h = Build();
        var (ch, s1) = Scene("s1", content: "one", h: h);
        var s2 = new SceneData { Id = "s2", Order = 1 };
        h.Disk["s2"] = "two";
        await h.Vm.OpenSceneAsync(ch, s1);
        await h.Vm.OpenSceneAsync(ch, s2); // s2 active
        await h.Vm.CloseTabFromUiCommand.ExecuteAsync(h.Vm.ActiveOpenScene);
        Assert.Single(h.Vm.OpenScenes);
        Assert.Equal(s1, h.Vm.CurrentScene);
    }

    [AvaloniaFact]
    public async Task CloseTab_Last_ClearsEditor()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "one", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        await h.Vm.CloseTabAsync(h.Vm.OpenScenes[0]);
        Assert.False(h.Vm.IsDocumentOpen);
        Assert.Empty(h.Vm.OpenScenes);
        Assert.Equal(0, h.Vm.WordCount);
    }

    [AvaloniaFact]
    public async Task CloseTab_ActiveDirty_SavesFirst()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "one", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        h.Vm.OnTextChanged("<p>dirty</p>", "dirty");
        await h.Vm.CloseTabAsync(h.Vm.OpenScenes[0]);
        await h.Proj.Received().WriteSceneContentAsync(ch, sc, Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task CloseTab_NonActiveDirty_Persists()
    {
        var h = Build();
        var (ch, s1) = Scene("s1", content: "one", h: h);
        var s2 = new SceneData { Id = "s2", Order = 1 };
        h.Disk["s2"] = "two";
        await h.Vm.OpenSceneAsync(ch, s1);
        await h.Vm.OpenSceneAsync(ch, s2); // s2 active
        // make s1 (non-active) dirty via its cached tab
        var s1Tab = h.Vm.OpenScenes.First(t => t.Scene.Id == "s1");
        s1Tab.IsDirty = true;
        s1Tab.CachedContent = "<p>edited s1</p>";
        await h.Vm.CloseTabAsync(s1Tab);
        await h.Proj.Received().WriteSceneContentAsync(ch, s1Tab.Scene, "<p>edited s1</p>");
    }

    [AvaloniaFact]
    public async Task DetachAndAttach_Tab()
    {
        var h = Build();
        var (ch, s1) = Scene("s1", content: "one", h: h);
        var s2 = new SceneData { Id = "s2", Order = 1 };
        h.Disk["s2"] = "two";
        await h.Vm.OpenSceneAsync(ch, s1);
        await h.Vm.OpenSceneAsync(ch, s2);
        var detached = await h.Vm.DetachTabAsync(h.Vm.ActiveOpenScene!);
        Assert.NotNull(detached);
        Assert.Single(h.Vm.OpenScenes);

        var h2 = Build();
        await h2.Vm.AttachTabAsync(detached!);
        Assert.True(h2.Vm.IsDocumentOpen);
        Assert.Single(h2.Vm.OpenScenes);
    }

    [AvaloniaFact]
    public async Task DetachTab_Last_ClearsEditor()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "one", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        await h.Vm.DetachTabAsync(h.Vm.OpenScenes[0]);
        Assert.False(h.Vm.IsDocumentOpen);
    }

    [AvaloniaFact]
    public async Task DetachTab_NotPresent_ReturnsNull()
    {
        var h = Build();
        var orphan = new EditorOpenScene(new ChapterData(), new SceneData());
        Assert.Null(await h.Vm.DetachTabAsync(orphan));
    }

    [AvaloniaFact]
    public async Task Close_SavesDirtyAndClears()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "one", h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        h.Vm.OnTextChanged("<p>dirty</p>", "dirty");
        await h.Vm.CloseAsync();
        Assert.False(h.Vm.IsDocumentOpen);
        await h.Proj.Received().WriteSceneContentAsync(ch, sc, Arg.Any<string>());
    }

    // ── Comment / footnote hooks ────────────────────────────────────
    [AvaloniaFact]
    public async Task CommentTextEdited_UpdatesSceneAndSaves()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "x", h: h);
        sc.Comments = [new SceneComment { Id = "c1", Text = "old" }];
        await h.Vm.OpenSceneAsync(ch, sc);
        h.Vm.RaiseCommentTextEdited("c1", "new text");
        Assert.Equal("new text", sc.Comments[0].Text);
        await h.Proj.Received().SaveScenesAsync();
    }

    [AvaloniaFact]
    public async Task CommentDeleteRequested_RemovesAndSyncs()
    {
        var h = Build();
        var (ch, sc) = Scene(content: "x", h: h);
        sc.Comments = [new SceneComment { Id = "c1", Text = "old" }];
        await h.Vm.OpenSceneAsync(ch, sc);
        string? removed = null;
        var synced = false;
        h.Vm.RemoveCommentAction = id => removed = id;
        h.Vm.SyncCommentsAction = () => synced = true;
        h.Vm.RaiseCommentDeleteRequested("c1");
        Assert.Equal("c1", removed);
        Assert.Empty(sc.Comments);
        Assert.True(synced);
    }

    [AvaloniaFact]
    public void Raises_Footnote_And_Comment_Events()
    {
        var h = Build();
        (string id, int n)? inserted = null;
        string? fnClicked = null;
        (string id, string anchor)? anchored = null;
        string? cClicked = null;
        var addComment = false;
        var addFootnote = false;
        h.Vm.FootnoteInserted += (id, n) => inserted = (id, n);
        h.Vm.FootnoteClicked += id => fnClicked = id;
        h.Vm.CommentAnchored += (id, a) => anchored = (id, a);
        h.Vm.CommentClicked += id => cClicked = id;
        h.Vm.AddCommentRequested += () => addComment = true;
        h.Vm.AddFootnoteRequested += () => addFootnote = true;

        h.Vm.RaiseFootnoteInserted("f1", 1);
        h.Vm.RaiseFootnoteClicked("f1");
        h.Vm.RaiseCommentAnchored("c1", "anchor");
        h.Vm.RaiseCommentClicked("c1");
        h.Vm.RaiseAddCommentRequested();
        h.Vm.RaiseAddFootnoteRequested();

        Assert.Equal(("f1", 1), inserted);
        Assert.Equal("f1", fnClicked);
        Assert.Equal(("c1", "anchor"), anchored);
        Assert.Equal("c1", cClicked);
        Assert.True(addComment);
        Assert.True(addFootnote);
    }

    [AvaloniaFact]
    public async Task RestoreCurrentArchivedScene_FiresWhenArchived()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "C" };
        var sc = new SceneData { Id = "a1", ArchivedAt = DateTime.UtcNow };
        await h.Vm.OpenSceneAsync(ch, sc);
        SceneData? restored = null;
        h.Vm.RestoreArchivedSceneRequested += s => restored = s;
        h.Vm.RestoreCurrentArchivedSceneCommand.Execute(null);
        Assert.Equal(sc, restored);
    }

    [AvaloniaFact]
    public void RestoreCurrentArchivedScene_NoOp_WhenNotArchived()
    {
        var h = Build();
        var fired = false;
        h.Vm.RestoreArchivedSceneRequested += _ => fired = true;
        h.Vm.RestoreCurrentArchivedSceneCommand.Execute(null); // no scene
        Assert.False(fired);
    }

    [AvaloniaFact]
    public void SetGrammarCheckContributors_NoThrow()
    {
        var h = Build();
        h.Vm.SetGrammarCheckContributors([]);
    }

    [AvaloniaFact]
    public async Task RefreshFocusPeek_NoThrow()
    {
        var h = Build();
        await h.Vm.RefreshFocusPeekAsync();
    }

    [AvaloniaFact]
    public async Task AutoSave_FiresAfterDelay()
    {
        // Scratch thread: Task.Delay yield contained off the Avalonia collection runner thread.
        Task.Run(async () =>
        {
            var h = Build();
            h.Vm.AutoSaveDelayMs = 40;
            var (ch, sc) = Scene(content: "orig", h: h);
            await h.Vm.OpenSceneAsync(ch, sc);
            h.Vm.OnTextChanged("<p>auto</p>", "auto"); // schedules autosave
            await Task.Delay(150);
            await h.Proj.Received().WriteSceneContentAsync(ch, sc, Arg.Any<string>());
        }).GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public async Task ReadabilityProps_AndFocusPeekExtension_Exposed()
    {
        var h = Build();
        var content = "The quick brown fox jumped over the lazy dog. " +
                      "She walked slowly toward the ancient house on the hill. " +
                      "Rain fell gently across the quiet town that evening.";
        var (ch, sc) = Scene(content: content, h: h);
        await h.Vm.OpenSceneAsync(ch, sc);
        Assert.True(h.Vm.HasReadability);
        Assert.False(string.IsNullOrEmpty(h.Vm.ReadingTimeDisplay));
        Assert.False(string.IsNullOrEmpty(h.Vm.ReadabilityDisplay));
        Assert.Equal(content, h.Vm.PlainTextContent);
        Assert.NotNull(h.Vm.FocusPeekExtension);
    }

    [AvaloniaFact]
    public void AutoSave_Reschedule_CancelsPrevious()
    {
        // Scratch thread keeps the cancelled Task.Delay off the collection runner thread.
        Task.Run(async () =>
        {
            var h = Build();
            h.Vm.AutoSaveDelayMs = 5000;
            var (ch, sc) = Scene(content: "orig", h: h);
            await h.Vm.OpenSceneAsync(ch, sc);
            h.Vm.OnTextChanged("<p>one</p>", "one");   // schedules autosave #1
            await Task.Delay(40);                       // ensure #1 is parked in Task.Delay
            h.Vm.OnTextChanged("<p>two</p>", "two");   // cancels #1 (OperationCanceledException), schedules #2
            await Task.Delay(120);                      // let the cancelled task run its catch
        }).GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void EditorOpenScene_Props()
    {
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter" };
        var sc = new SceneData { Id = "s1", Title = "" };
        var tab = new EditorOpenScene(ch, sc);
        Assert.Equal("Chapter", tab.DisplayTitle); // empty scene title -> chapter title
        tab.IsDirty = true;
        tab.IsActive = true;
        tab.CachedContent = "c";
        Assert.True(tab.IsDirty);
        Assert.True(tab.IsActive);
        Assert.Equal("c", tab.CachedContent);
    }
}
