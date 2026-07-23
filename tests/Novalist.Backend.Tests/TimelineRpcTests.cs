using Novalist.Backend;
using Novalist.Backend.Rpc;
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
}

