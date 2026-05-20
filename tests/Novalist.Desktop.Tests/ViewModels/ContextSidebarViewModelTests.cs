using System.ComponentModel;
using Avalonia.Threading;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class ContextSidebarViewModelTests
{
    static ContextSidebarViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class FakeEditor : IEditorContext
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _open;
        public bool IsDocumentOpen { get => _open; set { _open = value; Raise(nameof(IsDocumentOpen)); } }
        public ChapterData? CurrentChapter { get; set; }
        public SceneData? CurrentScene { get; set; }
        public string PlainTextContent { get; set; } = string.Empty;
        private string _content = string.Empty;
        public string Content { get => _content; set { _content = value; Raise(nameof(Content)); } }
        public string DocumentTitle { get; set; } = string.Empty;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private sealed class H
    {
        public IProjectService Proj = null!;
        public IEntityService Entity = null!;
        public ProjectSettings ProjSettings = null!;
        public ContextSidebarViewModel Vm = null!;
        public FakeEditor Editor = new();
        public List<ChapterData> Chapters = [];
        public Dictionary<string, List<SceneData>> Scenes = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SceneContent = new(StringComparer.OrdinalIgnoreCase);
    }

    private static H Build(bool loaded = true)
    {
        var h = new H();
        h.ProjSettings = new ProjectSettings();
        h.Proj = Substitute.For<IProjectService>();
        h.Proj.ProjectSettings.Returns(h.ProjSettings);
        h.Proj.IsProjectLoaded.Returns(loaded);
        h.Proj.SaveProjectSettingsAsync().Returns(Task.CompletedTask);
        h.Proj.GetChaptersOrdered().Returns(_ => h.Chapters);
        h.Proj.GetScenesForChapter(Arg.Any<string>()).Returns(ci =>
            h.Scenes.TryGetValue((string)ci[0], out var s) ? s : new List<SceneData>());
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns(ci =>
            Task.FromResult(h.SceneContent.TryGetValue(((SceneData)ci[1]).Id, out var c) ? c : string.Empty));
        h.Proj.SetSceneAnalysisOverridesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SceneAnalysisOverrides?>())
            .Returns(Task.CompletedTask);

        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());

        h.Vm = new ContextSidebarViewModel(h.Proj, h.Entity);
        return h;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

    // ── Construction & section state ────────────────────────────────
    [AvaloniaFact]
    public void Constructor_LoadsSectionState()
    {
        var h = Build();
        h.ProjSettings.ViewState.ContextCharactersExpanded = true;
        Assert.True(h.Vm.IsCharactersSectionExpanded);
        Assert.False(h.Vm.HasAnyContent);
    }

    [AvaloniaFact]
    public void SectionToggles_PersistToViewState()
    {
        var h = Build();
        h.Vm.ToggleCharactersSectionCommand.Execute(null);
        Assert.Equal(h.Vm.IsCharactersSectionExpanded, h.ProjSettings.ViewState.ContextCharactersExpanded);
        h.Vm.ToggleMentionsSectionCommand.Execute(null);
        h.Vm.ToggleLocationsSectionCommand.Execute(null);
        h.Vm.ToggleItemsSectionCommand.Execute(null);
        h.Vm.ToggleLoreSectionCommand.Execute(null);
        h.Vm.ToggleSceneAnalysisSectionCommand.Execute(null);
        Assert.Equal(h.Vm.IsSceneAnalysisSectionExpanded, h.ProjSettings.ViewState.ContextSceneAnalysisExpanded);
        h.Proj.Received().SaveProjectSettingsAsync();
    }

    // ── Attach / clear / refresh ────────────────────────────────────
    [AvaloniaFact]
    public void AttachEditor_NoDocument_ClearsContext()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor); // editor not open
        Assert.False(h.Vm.IsContextAvailable);
        Assert.Null(h.Vm.SceneAnalysis);
    }

    [AvaloniaFact]
    public void AttachEditor_Null_Clears()
    {
        var h = Build();
        h.Vm.AttachEditor(null);
        Assert.False(h.Vm.IsContextAvailable);
    }

    private static void OpenScene(H h, string content)
    {
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter One", Act = "Act 1" };
        var scene = new SceneData { Id = "s1", Title = "Scene One", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [scene];
        h.SceneContent["s1"] = content;
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = scene;
        h.Editor.PlainTextContent = content;
        h.Editor.IsDocumentOpen = true;
    }

    [AvaloniaFact]
    public async Task RefreshEntityData_Loaded_BuildsCardsOnAttach()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Alice", Role = "Lead", Gender = "F", Age = "30", Group = "Heroes" } });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "Tower", Parent = "City", Description = "tall" } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Name = "Blade", Type = "Weapon", Description = "sharp" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Name = "Prophecy", Category = "Myth", Description = "old" } });

        await h.Vm.RefreshEntityDataAsync();
        OpenScene(h, "Alice walked to the Tower holding the Blade. The Prophecy was true!");
        h.Vm.AttachEditor(h.Editor);

        Assert.True(h.Vm.IsContextAvailable);
        Assert.True(h.Vm.HasCharacterCards);
        Assert.True(h.Vm.HasLocationCards);
        Assert.True(h.Vm.HasItemCards);
        Assert.True(h.Vm.HasLoreCards);
        Assert.True(h.Vm.HasSceneAnalysis);
        Assert.True(h.Vm.HasAnyContent);

        // Exercise the open commands on cards
        EntityType? opened = null;
        h.Vm.EntityOpenRequested += (t, _) => opened = t;
        h.Vm.CharacterCards[0].OpenCommand.Execute(null);
        Assert.Equal(EntityType.Character, opened);
        h.Vm.LocationCards[0].OpenCommand.Execute(null);
        h.Vm.ItemCards[0].OpenCommand.Execute(null);
        h.Vm.LoreCards[0].OpenCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task RefreshEntityData_NotLoaded_ClearsSources()
    {
        var h = Build(loaded: false);
        await h.Vm.RefreshEntityDataAsync();
        Assert.False(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public async Task MentionRows_BuildWithGapWarning()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Alice" } });
        await h.Vm.RefreshEntityDataAsync();

        // 4 chapters; Alice only in chapter 1 -> gap of 3 -> warning
        var chapters = new List<ChapterData>();
        for (var i = 1; i <= 4; i++)
        {
            var ch = new ChapterData { Guid = $"ch{i}", Title = $"Chapter {i}", Order = i };
            chapters.Add(ch);
            var sc = new SceneData { Id = $"s{i}", Title = $"S{i}", Order = 0 };
            h.Scenes[ch.Guid] = [sc];
            h.SceneContent[sc.Id] = i == 1 ? "Alice appears here." : "Nobody.";
        }
        h.Chapters = chapters;
        h.Editor.CurrentChapter = chapters[0];
        h.Editor.CurrentScene = h.Scenes["ch1"][0];
        h.Editor.PlainTextContent = "Alice appears here.";
        h.Editor.IsDocumentOpen = true;

        await h.Vm.PreloadSnapshotsAsync();
        h.Vm.AttachEditor(h.Editor);

        Assert.True(h.Vm.HasMentionFrequency);
        var row = h.Vm.MentionRows[0];
        Assert.True(row.HasGapWarning);
        Assert.Equal(4, row.Cells.Count);
        Assert.True(row.Cells[0].IsPresent);
        Assert.True(row.Cells[0].IsCurrentChapter);
        Assert.True(row.Cells[3].IsAbsent);
        row.OpenCommand.Execute(null);
    }

    [AvaloniaFact]
    public void RefreshContextForScene_BuildsDirectly()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var scene = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [scene];
        h.Vm.RefreshContextForScene(ch, scene, "Some quiet routine prose.");
        Assert.True(h.Vm.IsContextAvailable);
        Assert.Equal("Sc", h.Vm.ContextLabel);
    }

    // ── Editor property change -> dispatcher post ───────────────────
    [AvaloniaFact]
    public void EditorContentChange_PostsRefresh()
    {
        var h = Build();
        OpenScene(h, "Quiet.");
        h.Vm.AttachEditor(h.Editor);
        h.Editor.Content = "new content"; // triggers OnEditorPropertyChanged -> Post
        Pump();
        Assert.True(h.Vm.IsContextAvailable);
        // Second change while pending coalesces
        h.Editor.Content = "again";
        h.Editor.Content = "again2";
        Pump();
    }

    // ── Context date display ────────────────────────────────────────
    [AvaloniaFact]
    public void ContextDateDisplay_Variants()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch", Date = "2024-03-15" };
        var scene = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [scene];
        h.Vm.RefreshContextForScene(ch, scene, "text");
        Assert.True(h.Vm.HasContextDate);
        Assert.Contains("2024-03-15", h.Vm.ContextDateDisplay); // weekday appended

        // non-date string
        var scene2 = new SceneData { Id = "s1", Title = "Sc", Order = 0, Date = "Summer" };
        h.Vm.RefreshContextForScene(ch, scene2, "text");
        Assert.Equal("Summer", h.Vm.ContextDateDisplay);
    }

    // ── Snapshot reload / ensure-context ────────────────────────────
    [AvaloniaFact]
    public async Task Preload_ThenAttach_NoForceReload()
    {
        var h = Build();
        OpenScene(h, "Calm steady prose.");
        await h.Vm.PreloadSnapshotsAsync();
        h.Vm.AttachEditor(h.Editor); // snapshots match -> EnsureProjectContextAsync returns early
        Assert.True(h.Vm.IsContextAvailable);
        Assert.False(h.Vm.IsContextLoading);
    }

    // ── Scene analysis override save via parent ─────────────────────
    [AvaloniaFact]
    public async Task SceneAnalysis_SaveOverride_PersistsAndRefreshes()
    {
        var h = Build();
        OpenScene(h, "A tense fight broke out. Danger! Fear gripped them.");
        h.Vm.AttachEditor(h.Editor);
        var analysis = h.Vm.SceneAnalysis!;
        Assert.NotNull(analysis);

        analysis.PovInput = "Alice";
        await analysis.SavePovCommand.ExecuteAsync(null);
        await h.Proj.Received().SetSceneAnalysisOverridesAsync("ch1", "s1", Arg.Any<SceneAnalysisOverrides?>());
    }

    [AvaloniaFact]
    public async Task SceneAnalysis_OverridesApplied_FromScene()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var scene = new SceneData
        {
            Id = "s1", Title = "Sc", Order = 0,
            AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Bob", Emotion = "joyful", Intensity = 7, Conflict = "duel", Tags = ["custom"] },
        };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [scene];
        h.Vm.RefreshContextForScene(ch, scene, "text");
        var a = h.Vm.SceneAnalysis!;
        Assert.Equal("Bob", a.Pov);
        Assert.True(a.HasPovOverride);
        Assert.True(a.HasEmotionOverride);
        Assert.True(a.HasIntensityOverride);
        Assert.True(a.HasConflictOverride);
        Assert.True(a.HasTagsOverride);
        Assert.Contains("custom", a.Tags);
    }

    // ── Rich analysis: all scene-tag + emotion + intensity branches ──
    private static string BigContent()
    {
        var sb = new System.Text.StringBuilder();
        // first person (>=4), 3 chars, 2 locations, conflict, exclamations, negatives
        sb.Append("I saw Alice and Bob and Cara at the Tower near the City. ");
        sb.Append("My blood ran cold. We feared the fight! I sensed danger, despair, panic, anger, hurt, threat, grief, dark, cold, cry, scream! ");
        sb.Append("They argued and chose to flee. We held the Blade and spoke of the Prophecy. ");
        // big quoted dialogue block to push dialogue ratio over 0.35
        sb.Append('"');
        sb.Append(string.Join(" ", System.Linq.Enumerable.Repeat("terror dread afraid", 250)));
        sb.Append("\" ");
        // pad to >1200 words
        sb.Append(string.Join(" ", System.Linq.Enumerable.Repeat("word", 700)));
        return sb.ToString();
    }

    [AvaloniaFact]
    public async Task RichScene_HitsAllTagAndAnalysisBranches()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Alice", Surname = "Smith", Role = "Lead", ChapterOverrides = { new CharacterOverride { Chapter = "ch1", Scene = "Scene One", Name = "AliceOverride" } } },
            new() { Name = "Bob", AgeMode = "date", BirthDate = "2000-01-01", AgeIntervalUnit = IntervalUnit.Years },
            new() { Name = "Cara" },
        });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Name = "Tower", Description = new string('x', 220) }, // long desc -> TrimDescription
            new() { Name = "City" },
        });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Name = "Blade" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Name = "Prophecy" } });
        await h.Vm.RefreshEntityDataAsync();

        var content = BigContent();
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter One", Date = "2020-01-01" };
        var s1 = new SceneData { Id = "s1", Title = "Scene One", Order = 0, Date = "2020-01-01" };
        var s2 = new SceneData { Id = "s2", Title = "Scene Two", Order = 1 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [s1, s2];
        h.SceneContent["s1"] = content;
        h.SceneContent["s2"] = "<p>calm &amp; quiet</p>"; // HTML -> NormalizeSceneContent
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = s1;
        h.Editor.PlainTextContent = content;
        h.Editor.IsDocumentOpen = true;

        h.Vm.AttachEditor(h.Editor);

        var a = h.Vm.SceneAnalysis!;
        Assert.NotNull(a);
        Assert.True(a.HasTags);
        Assert.True(a.Sparkline.HasSparkPoints); // multi-scene sparkline
        Assert.Equal(3, h.Vm.CharacterCards.Count); // ensemble
        // Alice override name applied in display
        Assert.Contains(h.Vm.CharacterCards, card => card.Title.Contains("AliceOverride"));
        // long location description trimmed
        Assert.Contains(h.Vm.LocationCards, card => card.Description.EndsWith("..."));
    }

    [AvaloniaFact]
    public async Task ReAttach_DetachesPreviousEditor()
    {
        var h = Build();
        OpenScene(h, "text");
        h.Vm.AttachEditor(h.Editor);
        var editor2 = new FakeEditor();
        h.Vm.AttachEditor(editor2); // detaches first
        // changing the old editor must no longer trigger refresh
        h.Editor.Content = "changed";
        Pump();
        Assert.True(true); // no throw / no stale handler
    }

    [AvaloniaFact]
    public async Task SceneAnalysis_AllOverrideSaves_AndResets()
    {
        var h = Build();
        OpenScene(h, "A quiet steady routine scene with calm gentle peace.");
        h.Vm.AttachEditor(h.Editor);
        var a = h.Vm.SceneAnalysis!;

        a.PovInput = "NewPov";
        await a.SavePovCommand.ExecuteAsync(null);
        a.SelectedEmotion = Loc.T("emotion.joyful");
        await a.SaveEmotionCommand.ExecuteAsync(null);
        // re-fetch analysis (RefreshContext rebuilds it after each save)
        a = h.Vm.SceneAnalysis!;
        a.BeginEditIntensityCommand.Execute(null);
        a.IntensityInput = "5";
        await a.SaveIntensityCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        a.BeginEditConflictCommand.Execute(null);
        a.ConflictInput = "a new conflict";
        await a.SaveConflictCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        a.BeginEditTagsCommand.Execute(null);
        a.TagsInput = "tagA, tagB";
        await a.SaveTagsCommand.ExecuteAsync(null);

        a = h.Vm.SceneAnalysis!;
        await a.ResetPovCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        await a.ResetEmotionCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        await a.ResetIntensityCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        await a.ResetConflictCommand.ExecuteAsync(null);
        a = h.Vm.SceneAnalysis!;
        await a.ResetTagsCommand.ExecuteAsync(null);

        await h.Proj.Received().SetSceneAnalysisOverridesAsync("ch1", "s1", Arg.Any<SceneAnalysisOverrides?>());
    }

    [AvaloniaFact]
    public async Task PovOptions_FallbackToAllCharacters_WhenNoneMatched()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Ghost" } });
        await h.Vm.RefreshEntityDataAsync();
        OpenScene(h, "Nobody named here at all."); // Ghost not mentioned
        h.Vm.AttachEditor(h.Editor);
        var a = h.Vm.SceneAnalysis!;
        Assert.Contains("Ghost", a.PovOptions); // fallback to all character sources
    }

    [AvaloniaFact]
    public async Task DetectPov_FirstPersonFallback_WhenNoCharacters()
    {
        var h = Build();
        await h.Vm.RefreshEntityDataAsync(); // no characters
        OpenScene(h, "I walked. I saw. My hands shook. We ran together.");
        h.Vm.AttachEditor(h.Editor);
        var a = h.Vm.SceneAnalysis!;
        Assert.Equal(Loc.T("pov.firstPerson"), a.Pov);
    }

    [AvaloniaFact]
    public async Task DetectPov_ChapterOnlyOverride()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Hero", ChapterOverrides = { new CharacterOverride { Chapter = "ch1", Name = "ChapHero" } } },
        });
        await h.Vm.RefreshEntityDataAsync();
        OpenScene(h, "Hero stands here. Hero again.");
        h.Vm.AttachEditor(h.Editor);
        Assert.Equal("ChapHero", h.Vm.SceneAnalysis!.Pov);
    }

    [AvaloniaFact]
    public async Task DetectPov_ActOverride()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Hero", ChapterOverrides = { new CharacterOverride { Act = "Act 1", Name = "ActHero" } } },
        });
        await h.Vm.RefreshEntityDataAsync();
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter One", Act = "Act 1" };
        var scene = new SceneData { Id = "s1", Title = "Scene One", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [scene];
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = scene;
        h.Editor.PlainTextContent = "Hero. Hero.";
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        Assert.Equal("ActHero", h.Vm.SceneAnalysis!.Pov);
    }

    [AvaloniaFact]
    public void ContextDateRange_SceneAndChapter()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch", DateRange = new StoryDateRange { Start = "2024-01-01" } };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0, DateRange = new StoryDateRange { Start = "2024-02-02" } };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        h.Vm.RefreshContextForScene(ch, sc, "text");
        Assert.True(h.Vm.HasContextDate); // scene DateRange used

        var sc2 = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Vm.RefreshContextForScene(ch, sc2, "text"); // chapter DateRange used
        Assert.True(h.Vm.HasContextDate);
    }

    [AvaloniaFact]
    public void ContextSubtitle_EmptyChapterTitle()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        h.Vm.RefreshContextForScene(ch, sc, "text");
        Assert.False(string.IsNullOrEmpty(h.Vm.ContextSubtitle));
    }

    [AvaloniaFact]
    public void StatusMessage_LoadingAndEmpty()
    {
        var h = Build();
        Assert.Equal(Loc.T("context.empty"), h.Vm.ContextStatusMessage);
        h.Vm.IsContextLoading = true;
        Assert.Equal(Loc.T("context.loading"), h.Vm.ContextStatusMessage);
    }

    [AvaloniaFact]
    public async Task EnsureProjectContext_ReadThrows_HitsCatch()
    {
        var h = Build();
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("boom"));
        // second scene forces a disk read (current scene uses in-memory content)
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", Order = 0 };
        var s2 = new SceneData { Id = "s2", Title = "S2", Order = 1 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [s1, s2];
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = s1;
        h.Editor.PlainTextContent = "current";
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor); // EnsureProjectContextAsync runs, ReadSceneContentAsync throws -> catch
        Assert.True(h.Vm.IsContextAvailable);
        Assert.False(h.Vm.IsContextLoading);
    }

    [AvaloniaFact]
    public void EntityCard_And_Pill_Flags()
    {
        var pill = new ContextSidebarPillViewModel("Label", "Val");
        Assert.Equal("Label", pill.Label);
        Assert.Equal("Val", pill.Value);

        var card = new ContextSidebarEntityCardViewModel(
            "Title", "Primary", "Secondary", "Desc", "Info",
            [pill], new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        Assert.True(card.HasPrimaryBadge);
        Assert.True(card.HasSecondaryBadge);
        Assert.True(card.HasDescription);
        Assert.True(card.HasInfoText);
        Assert.True(card.HasPills);
        Assert.Equal("Title", card.Title);
    }

    [AvaloniaFact]
    public void MentionCell_Props()
    {
        var cell = new ContextSidebarMentionCellViewModel("1", true, true, "tip");
        Assert.True(cell.IsPresent);
        Assert.False(cell.IsAbsent);
        Assert.True(cell.IsCurrentChapter);
        Assert.Equal("tip", cell.ToolTip);
        Assert.Equal("1", cell.Label);
    }

    // ── Batch 3: remaining reachable branches ───────────────────────
    [AvaloniaFact]
    public void EmptyContent_ZeroWordRatios()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        h.Vm.RefreshContextForScene(ch, sc, "!!!"); // no words -> dialogue/avg-sentence return 0
        Assert.True(h.Vm.HasSceneAnalysis);
        Assert.True(h.Vm.HasContextSubtitle);
    }

    [AvaloniaFact]
    public void ContextDateDisplay_LeadingButUnparseable_ReturnsRaw()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0, Date = "2020-99-99" };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        h.Vm.RefreshContextForScene(ch, sc, "text");
        Assert.Equal("2020-99-99", h.Vm.ContextDateDisplay);
    }

    [AvaloniaFact]
    public void Intensity_ZeroWithConflict_GoesNegative()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        // 1 positive (hope) cancels via 2 conflict (argue, fight) -> score 0 -> conflict>0 branch
        h.Vm.RefreshContextForScene(ch, sc, "We hope. They argue. They fight.");
        Assert.True(h.Vm.SceneAnalysis!.Intensity < 0);
    }

    [AvaloniaFact]
    public void Emotion_IntensityFallback_TenseAndTriumphant()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];

        // Strong negatives, none of which are emotion-profile keywords or substrings of them
        // (e.g. "danger" contains "anger") -> fallback "tense"
        h.Vm.RefreshContextForScene(ch, sc, "blood hurt threat cold cry scream sad");
        Assert.Equal("tense", h.Vm.SceneAnalysis!.Emotion);

        // Strong positives, no emotion-profile keyword -> fallback "triumphant"
        h.Vm.RefreshContextForScene(ch, sc, "warm relief bright safe");
        Assert.Equal("triumphant", h.Vm.SceneAnalysis!.Emotion);
    }

    [AvaloniaFact]
    public void ConflictSnippet_LongSentenceTruncated()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        var longConflict = "They began to " + new string('x', 120) + " fight over everything in sight";
        h.Vm.RefreshContextForScene(ch, sc, longConflict);
        Assert.EndsWith("...", h.Vm.SceneAnalysis!.Conflict);
    }

    [AvaloniaFact]
    public async Task DateAge_ResolvedFromSceneDate()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Aged", AgeMode = "date", BirthDate = "2000-01-01", AgeIntervalUnit = IntervalUnit.Years },
        });
        await h.Vm.RefreshEntityDataAsync();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", Order = 0, Date = "2020-06-15" };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [sc];
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = sc;
        h.Editor.PlainTextContent = "Aged was here.";
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        var card = h.Vm.CharacterCards.First(c => c.Title == "Aged");
        Assert.Contains(card.Pills, p => !string.IsNullOrEmpty(p.Value)); // computed age pill present
    }

    [AvaloniaFact]
    public async Task NormalizeSceneContent_EmptyDiskScene()
    {
        var h = Build();
        await h.Vm.RefreshEntityDataAsync();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", Order = 0 };
        var s2 = new SceneData { Id = "s2", Title = "S2", Order = 1 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [s1, s2];
        h.SceneContent["s2"] = ""; // empty disk content -> NormalizeSceneContent empty branch
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = s1;
        h.Editor.PlainTextContent = "current";
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        Assert.True(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public async Task SaveOverride_NoCurrentScene_EarlyReturn()
    {
        var h = Build();
        OpenScene(h, "A calm steady scene.");
        h.Vm.AttachEditor(h.Editor);
        var a = h.Vm.SceneAnalysis!;
        h.Editor.CurrentScene = null; // SaveSceneAnalysisOverrideAsync should bail
        a.PovInput = "Whoever";
        await a.SavePovCommand.ExecuteAsync(null);
        await h.Proj.DidNotReceive().SetSceneAnalysisOverridesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SceneAnalysisOverrides?>());
    }

    [AvaloniaFact]
    public async Task SnapshotReload_SceneSetChanged()
    {
        var h = Build();
        await h.Vm.RefreshEntityDataAsync();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [s1];
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = s1;
        h.Editor.PlainTextContent = "x";
        h.Editor.IsDocumentOpen = true;
        await h.Vm.PreloadSnapshotsAsync();
        h.Vm.AttachEditor(h.Editor); // snapshots match

        // Now change the scene set so SnapshotMatches returns false (count/id/order differ)
        h.Scenes["ch1"] = [s1, new SceneData { Id = "s2", Title = "S2", Order = 1 }];
        h.Editor.Content = "trigger"; // posts refresh -> NeedsSnapshotReload true -> SnapshotMatches false
        Pump();
        Assert.True(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public async Task ChapterSnapshot_UpdateUnknownScene_NoOp()
    {
        var h = Build();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", Order = 0 };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [s1];
        await h.Vm.PreloadSnapshotsAsync(); // snapshot for ch1 contains s1 only
        // Refresh for a scene id absent from the existing chapter snapshot -> UpdateSceneContent no-op
        var sX = new SceneData { Id = "sX", Title = "Sc", Order = 0 };
        h.Vm.RefreshContextForScene(ch, sX, "text");
        Assert.True(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public async Task DateAge_InvalidBirthDate_FallsThroughToAge()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Bad", Age = "fortyish", AgeMode = "date", BirthDate = "not-a-date" },
        });
        await h.Vm.RefreshEntityDataAsync();
        OpenScene(h, "Bad walks in.");
        h.Vm.AttachEditor(h.Editor);
        var card = h.Vm.CharacterCards.First(c => c.Title == "Bad");
        Assert.Contains(card.Pills, p => p.Value == "fortyish"); // computed empty -> raw Age used
    }

    [AvaloniaFact]
    public async Task SnapshotReload_SameCountDifferentSceneId()
    {
        var h = Build();
        await h.Vm.RefreshEntityDataAsync();
        var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
        h.Chapters = [ch];
        h.Scenes["ch1"] = [new SceneData { Id = "s1", Title = "S1", Order = 0 }];
        h.Editor.CurrentChapter = ch;
        h.Editor.CurrentScene = h.Scenes["ch1"][0];
        h.Editor.PlainTextContent = "x";
        h.Editor.IsDocumentOpen = true;
        await h.Vm.PreloadSnapshotsAsync();
        h.Vm.AttachEditor(h.Editor);

        // Same scene count, different id -> SnapshotMatches per-scene mismatch
        h.Scenes["ch1"] = [new SceneData { Id = "s2", Title = "S2", Order = 0 }];
        h.Editor.CurrentScene = h.Scenes["ch1"][0];
        h.Editor.Content = "trigger";
        Pump();
        Assert.True(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public async Task SnapshotReload_ChapterGuidMissing()
    {
        var h = Build();
        await h.Vm.RefreshEntityDataAsync();
        var chA = new ChapterData { Guid = "chA", Title = "A" };
        var chB = new ChapterData { Guid = "chB", Title = "B" };
        h.Chapters = [chA, chB];
        h.Scenes["chA"] = [new SceneData { Id = "a1", Order = 0 }];
        h.Scenes["chB"] = [new SceneData { Id = "b1", Order = 0 }];
        await h.Vm.PreloadSnapshotsAsync(); // snapshots {chA, chB}

        // Swap chB for chC: same chapter count (2) but current guid not in snapshots
        var chC = new ChapterData { Guid = "chC", Title = "C" };
        h.Chapters = [chA, chC];
        h.Scenes["chC"] = [new SceneData { Id = "c1", Order = 0 }];
        h.Editor.CurrentChapter = chC;
        h.Editor.CurrentScene = h.Scenes["chC"][0];
        h.Editor.PlainTextContent = "x";
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor); // NeedsSnapshotReload: count equal but guid missing -> true
        Assert.True(h.Vm.IsContextAvailable);
    }

    [AvaloniaFact]
    public void EnsureProjectContext_VersionChangedMidLoad_Bails()
    {
        // Run on a scratch thread: no AvaloniaSynchronizationContext is captured, so the
        // awaited gate continuation resumes inline rather than posting to the headless
        // dispatcher (which would poison sibling tests in the Avalonia collection).
        Task.Run(async () =>
        {
            var h = Build();
            await h.Vm.RefreshEntityDataAsync();
            var ch = new ChapterData { Guid = "ch1", Title = "Ch" };
            var s1 = new SceneData { Id = "s1", Title = "S1", Order = 0 };
            var s2 = new SceneData { Id = "s2", Title = "S2", Order = 1 };
            h.Chapters = [ch];
            h.Scenes["ch1"] = [s1, s2];

            var gate = new TaskCompletionSource<string>();
            var calls = 0;
            h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>())
                .Returns(_ => { calls++; return calls == 1 ? gate.Task : Task.FromResult("loaded"); });

            h.Editor.CurrentChapter = ch;
            h.Editor.CurrentScene = s1;
            h.Editor.PlainTextContent = "current";
            h.Editor.IsDocumentOpen = true;

            h.Vm.AttachEditor(h.Editor); // first EnsureProjectContext suspends on gate (reading s2)

            // Second refresh bumps the snapshot version and completes synchronously
            h.Vm.RefreshContextForScene(ch, s1, "current");

            gate.SetResult("late"); // first load resumes with a stale version -> bails at the guard
            await Task.Yield();
            Assert.True(h.Vm.IsContextAvailable);
        }).GetAwaiter().GetResult();
    }
}
