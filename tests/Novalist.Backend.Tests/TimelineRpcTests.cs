using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class TimelineRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly TimelineRpc _rpc;

    public TimelineRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-tl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "TlNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new TimelineRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Get_BuildsActsChaptersScenesAndManualEvents()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Kapitel", "1043-03-01");
        chapter.Act = "Act I";
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Dated", "1043-03-02");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Inherits");
        await _rpc.SaveEventAsync(null, "The Comet", "1043-03-01", "Omen", "plot", chapter.Guid);

        var dto = await _rpc.Get();

        Assert.Equal("vertical", dto.ViewMode);
        Assert.Equal("month", dto.ZoomLevel);
        var all = dto.Groups.SelectMany(g => g.Events).ToList();
        Assert.Contains(all, e => e.Source == "act" && e.Title == "Act I");
        Assert.Contains(all, e => e.Source == "chapter" && e.Id == $"ch-{chapter.Guid}");
        Assert.Contains(all, e => e.Source == "scene" && e.Title.EndsWith("Dated"));
        Assert.Contains(all, e => e.Source == "scene" && e.Title.EndsWith("Inherits") && e.DateStr == "1043-03-01");
        Assert.Contains(all, e => e.IsManual && e.Title == "The Comet");
        Assert.Equal("Mar 1043", dto.Groups.First(g => g.Key == "1043-03").Label);
    }

    [Fact]
    public async Task SaveEvent_UpdatesExisting_AndDeleteRemoves()
    {
        var created = await _rpc.SaveEventAsync(null, "Old", "1043-01-01", "", "plot", null);
        var manual = created.Groups.SelectMany(g => g.Events).Single(e => e.IsManual);

        var updated = await _rpc.SaveEventAsync(
            manual.Id.Replace("manual-", ""), "New", "1043-02-01", "changed", "war", null);
        var edited = updated.Groups.SelectMany(g => g.Events).Single(e => e.IsManual);
        Assert.Equal("New", edited.Title);
        Assert.Equal("war", edited.CategoryId);

        var deleted = await _rpc.DeleteEventAsync(manual.Id.Replace("manual-", ""));
        Assert.DoesNotContain(deleted.Groups.SelectMany(g => g.Events), e => e.IsManual);
    }

    [Fact]
    public async Task Get_ResolvesManualEventEntityChipsToArticles()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Id = "hero", Name = "Aldric", Surname = "Vane"
        });
        await entities.SaveLocationAsync(new Novalist.Core.Models.LocationData { Id = "port", Name = "Harbour" });
        // Two characters share the first name, so the bare name stays ambiguous.
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Id = "t1", Name = "Robin" });
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Id = "t2", Name = "Robin" });

        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ManualEvents.Add(new Novalist.Core.Models.TimelineManualEvent
        {
            Id = "e1", Title = "Landfall", Date = "1043-03-01",
            Characters = { "Aldric Vane", "Robin", "Ghost" },
            Locations = { "Harbour" }
        });
        await _workspace.Projects.SaveProjectSettingsAsync();

        var dto = await _rpc.Get();

        Assert.Contains(dto.EntityLinks, l => l.Name == "Aldric Vane" && l.EntityId == "hero" && l.TypeKey == "character");
        Assert.Contains(dto.EntityLinks, l => l.Name == "Harbour" && l.EntityId == "port" && l.TypeKey == "location");
        Assert.DoesNotContain(dto.EntityLinks, l => l.Name == "Robin");  // ambiguous
        Assert.DoesNotContain(dto.EntityLinks, l => l.Name == "Ghost");  // unknown
    }

    [Fact]
    public async Task Get_NoManualEventNames_YieldsNoEntityLinks()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Kapitel", "1043-03-01");
        await _rpc.SaveEventAsync(null, "The Comet", "1043-03-01", "Omen", "plot", chapter.Guid);

        var dto = await _rpc.Get();
        Assert.Empty(dto.EntityLinks);
    }

    [Fact]
    public async Task SetView_PersistsModeAndZoom()
    {
        await _rpc.SetViewAsync("horizontal", "year");
        var dto = await _rpc.Get();
        Assert.Equal("horizontal", dto.ViewMode);
        Assert.Equal("year", dto.ZoomLevel);
        Assert.Empty(dto.Groups);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1043-03-01", "1043-03-01")]
    [InlineData("1043-03", "1043-03-01")]
    [InlineData("1043", "1043-01-01")]
    [InlineData("5.3.1043", "1043-03-05")]
    [InlineData("March 5, 1043", "1043-03-05")]
    [InlineData("not a date", null)]
    public void ParseDate_HandlesAllFormats(string? input, string? expectedIso)
    {
        var parsed = TimelineRpc.ParseDate(input);
        Assert.Equal(expectedIso, parsed?.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData(null, "month", "no-date", "???")]
    [InlineData("1043-03-05", "year", "1043", "1043")]
    [InlineData("1043-03-05", "month", "1043-03", "Mar 1043")]
    [InlineData("1043-03-05", "day", "1043-03-05", "Mar 5, 1043")]
    public void GroupKeyAndLabel_MatchAvaloniaBehavior(
        string? date, string zoom, string expectedKey, string expectedLabel)
    {
        var parsed = TimelineRpc.ParseDate(date);
        var key = TimelineRpc.GroupKey(parsed, zoom);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedLabel, TimelineRpc.GroupLabel(key, zoom));
    }

    [Fact]
    public void StructureTemplates_ListsBundledStructures()
    {
        var templates = _rpc.GetStructureTemplates();
        Assert.Equal(4, templates.Length);
        Assert.Contains(templates, t => t.Id == "save-the-cat" && t.DisplayName == "Save the Cat");
    }

    [Fact]
    public async Task ApplyStructureTemplate_AppendsBeatsAsManualEvents()
    {
        var result = await _rpc.ApplyStructureTemplateAsync("seven-point");
        var manual = result.Groups.SelectMany(g => g.Events).Where(e => e.IsManual).ToArray();
        Assert.Equal(7, manual.Length);
        Assert.Contains(manual, e => e.Title == "Hook");
        Assert.Equal(7, _workspace.Projects.ProjectSettings.Timeline.ManualEvents.Count);

        var unchanged = await _rpc.ApplyStructureTemplateAsync("no-such-structure");
        Assert.Equal(7, unchanged.Groups.SelectMany(g => g.Events).Count(e => e.IsManual));
    }

    // ── Lanes ──
    //
    // Filtering the timeline to one character hides the threads being
    // compared, which is the opposite of what "does this POV disappear for
    // eighty pages" needs. Lanes need the events to say who and where.

    [Fact]
    public async Task SceneEvents_CarryTheirCastPovAndPlotlines()
    {
        var entities = new EntityService(_workspace.Projects);
        var mira = new Novalist.Core.Models.CharacterData { Name = "Mira" };
        var vault = new Novalist.Core.Models.LocationData { Name = "The vault" };
        await entities.SaveCharacterAsync(mira);
        await entities.SaveLocationAsync(vault);

        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        scene.Date = "2024-01-02";
        scene.Cast = [mira.Id, vault.Id, "long-gone"];
        scene.PlotlineIds = ["p1"];
        scene.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides { Pov = "Mira" };
        await _workspace.Projects.SaveScenesAsync();

        var timeline = await _rpc.Get();
        var sceneEvent = timeline.Groups
            .SelectMany(g => g.Events)
            .Single(e => e.Source == "scene");

        Assert.Equal(["Mira"], sceneEvent.Characters);
        Assert.Equal(["The vault"], sceneEvent.Locations);
        Assert.Equal("Mira", sceneEvent.Pov);
        Assert.Equal(["p1"], sceneEvent.PlotlineIds);
        // An id whose entity is gone contributes no lane rather than one
        // headed by a GUID.
        Assert.DoesNotContain("long-gone", sceneEvent.Characters);
    }

    [Fact]
    public async Task SceneEvents_WithNoCast_CarryNothing()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        scene.Date = "2024-01-02";
        await _workspace.Projects.SaveScenesAsync();

        var sceneEvent = (await _rpc.Get()).Groups
            .SelectMany(g => g.Events)
            .Single(e => e.Source == "scene");

        Assert.Empty(sceneEvent.Characters);
        Assert.Empty(sceneEvent.PlotlineIds);
        Assert.Equal(string.Empty, sceneEvent.Pov);
    }

    [Fact]
    public async Task SceneEvents_CarryTheirModeAndReadingPosition()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var first = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Now");
        var second = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Then");
        first.Date = "2024-05-01";
        second.Date = "1999-01-01";
        second.NarrativeMode = "flashback";
        await _workspace.Projects.SaveScenesAsync();

        var scenes = (await _rpc.Get()).Groups
            .SelectMany(g => g.Events)
            .Where(e => e.Source == "scene")
            .OrderBy(e => e.ReadingIndex)
            .ToList();

        // Reading order is what the reader meets, whatever the dates say - a
        // flashback dated 1999 is still the second scene of the book.
        Assert.Equal(["Now", "Then"], scenes.Select(e => e.Title.Split(": ")[1]));
        Assert.Equal([1, 2], scenes.Select(e => e.ReadingIndex));
        Assert.Equal("flashback", scenes[1].NarrativeMode);
        Assert.Equal(string.Empty, scenes[0].NarrativeMode);
    }

    [Fact]
    public async Task ABookOnItsOwnCalendarGroupsByInWorldYear()
    {
        _workspace.Projects.ActiveBook!.Calendar = new Novalist.Core.Models.InWorldCalendar
        {
            Type = Novalist.Core.Models.InWorldCalendarType.Custom,
            MonthNames = ["First", "Second"],
            DaysPerMonth = [30, 30],
            Eras = [new Novalist.Core.Models.CalendarEra { Name = "Fourth Age", StartYear = 300 }]
        };
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var early = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Early");
        var late = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Late");
        early.Date = "342.1.5";
        late.Date = "343.2.1";
        await _workspace.Projects.SaveScenesAsync();

        var timeline = await _rpc.Get();

        // Gregorian parsing cannot read these at all, so every scene used to
        // land in the undated bucket.
        var groups = timeline.Groups.Where(g => g.Events.Any(e => e.Source == "scene")).ToList();
        Assert.Equal(["43 Fourth Age", "44 Fourth Age"], groups.Select(g => g.Label));
    }

    [Fact]
    public async Task ABookOnItsOwnCalendarKeepsUnreadableDatesInTheirOwnGroup()
    {
        _workspace.Projects.ActiveBook!.Calendar = new Novalist.Core.Models.InWorldCalendar
        {
            Type = Novalist.Core.Models.InWorldCalendarType.Custom,
            MonthNames = ["First"],
            DaysPerMonth = [30]
        };
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Vague");
        scene.Date = "sometime later";
        await _workspace.Projects.SaveScenesAsync();

        var timeline = await _rpc.Get();

        // Shown rather than dropped: a scene with a date nobody can parse is
        // still a scene.
        Assert.Contains(timeline.Groups, g => g.Events.Any(e => e.Title.EndsWith("Vague")));
    }

    // ── Who was there, where, and how long it lasted ──

    [Fact]
    public async Task Event_ParticipantsAndSpanAreAuthorable()
    {
        // All three have been on the model for a long time and only scene
        // analysis ever wrote them, so backstory that never appears in a scene
        // could not be attached to the people it defines.
        await _rpc.SaveEventAsync(
            null, "The siege", "1043-03-01", "It lasted a while", "plot", null,
            characters: ["  Mira  ", "Tobin", "mira"],
            locations: ["Ashport"],
            endDate: "  1043-05-20  ");

        var stored = _workspace.Projects.ProjectSettings.Timeline.ManualEvents.Single();
        // Trimmed, and the same name twice is one participant.
        Assert.Equal(["Mira", "Tobin"], stored.Characters);
        Assert.Equal(["Ashport"], stored.Locations);
        Assert.Equal("1043-05-20", stored.EndDate);

        var dto = (await _rpc.Get()).Groups
            .SelectMany(g => g.Events)
            .Single(e => e.IsManual);
        Assert.Equal("1043-05-20", dto.EndDateStr);
        Assert.Equal("1043-05-20", dto.SortEndDate);
    }

    [Fact]
    public async Task Event_ACallerThatDoesNotKnowAboutThemLeavesThemAlone()
    {
        await _rpc.SaveEventAsync(
            null, "The siege", "1043-03-01", "", "plot", null,
            characters: ["Mira"], locations: ["Ashport"], endDate: "1043-05-20");
        var id = _workspace.Projects.ProjectSettings.Timeline.ManualEvents.Single().Id;

        // The six-argument form is what shipped first; it must not silently
        // erase participants somebody set in the editor.
        await _rpc.SaveEventAsync(id, "The siege of Ashport", "1043-03-01", "", "plot", null);

        var stored = _workspace.Projects.ProjectSettings.Timeline.ManualEvents.Single();
        Assert.Equal("The siege of Ashport", stored.Title);
        Assert.Equal(["Mira"], stored.Characters);
        Assert.Equal("1043-05-20", stored.EndDate);
    }

    [Fact]
    public async Task Scene_SpanReachesTheTimeline()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The crossing");
        scene.Date = "1043-03-01";
        scene.DateRange = new Novalist.Core.Models.StoryDateRange
        {
            Start = "1043-03-01",
            End = "1043-03-09"
        };
        await _workspace.Projects.SaveScenesAsync();

        var dto = (await _rpc.Get()).Groups
            .SelectMany(g => g.Events)
            .Single(e => e.Source == "scene");

        // A scene that spans days has always known it; the timeline just never
        // passed the far end on, so nothing could draw the span.
        Assert.Equal("1043-03-09", dto.EndDateStr);
        Assert.Equal("1043-03-09", dto.SortEndDate);
    }

    [Fact]
    public async Task Event_WithNoEndIsStillInstantaneous()
    {
        await _rpc.SaveEventAsync(null, "A shot", "1043-03-01", "", "plot", null);

        var dto = (await _rpc.Get()).Groups.SelectMany(g => g.Events).Single(e => e.IsManual);

        Assert.Equal(string.Empty, dto.EndDateStr);
        Assert.Null(dto.SortEndDate);
    }

}
