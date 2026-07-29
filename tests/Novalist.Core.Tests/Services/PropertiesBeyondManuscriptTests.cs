using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The writer's own fields on plotlines, timeline events and research items.
///
/// Typed properties were Codex-only, then scene-and-chapter; a plot thread, a
/// dated event and a source had fixed field sets, so anything else about them
/// had to be smuggled through a description.
/// </summary>
public sealed class PropertiesBeyondManuscriptTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    private readonly ResearchService _research;
    private readonly ManuscriptPropertyService _sut;

    public PropertiesBeyondManuscriptTests()
    {
        _projects = new ProjectService(_files);
        _projects.CreateProjectAsync(_dir.Path, "Props", "Book").GetAwaiter().GetResult();
        _research = new ResearchService(_projects, _files);
        _sut = new ManuscriptPropertyService(_projects, _research);
    }

    public void Dispose() => _dir.Dispose();

    private static ManuscriptPropertyDefinition Def(
        string key, ManuscriptPropertyScope scope,
        CustomPropertyType type = CustomPropertyType.String)
        => new() { Key = key, Label = key, Type = type, Scope = scope };

    private PlotlineData AddPlotline(string name = "The betrayal")
    {
        var plotline = new PlotlineData { Name = name };
        _projects.ActiveBook!.Plotlines.Add(plotline);
        return plotline;
    }

    private TimelineManualEvent AddEvent(string title = "The fire")
    {
        var story = new TimelineManualEvent { Id = System.Guid.NewGuid().ToString(), Title = title };
        _projects.ProjectSettings.Timeline.ManualEvents.Add(story);
        return story;
    }

    private async Task<ResearchItem> AddResearchAsync(string title = "Ship logs")
    {
        var item = new ResearchItem { Title = title };
        await _research.SaveAsync(item);
        return item;
    }

    [Fact]
    public async Task Plotline_HoldsATypedValue()
    {
        var plotline = AddPlotline();
        await _sut.SetDefinitionsAsync([Def("resolvesIn", ManuscriptPropertyScope.Plotline)]);

        await _sut.SetPlotlineValueAsync(plotline.Id, "resolvesIn", "Act III");

        Assert.Equal("Act III", _sut.PlotlineValues(plotline.Id)["resolvesIn"]);
    }

    [Fact]
    public async Task Event_HoldsATypedValue()
    {
        var story = AddEvent();
        await _sut.SetDefinitionsAsync(
            [Def("onThePage", ManuscriptPropertyScope.Event, CustomPropertyType.Bool)]);

        await _sut.SetEventValueAsync(story.Id, "onThePage", "true");

        Assert.Equal("true", _sut.EventValues(story.Id)["onThePage"]);
    }

    [Fact]
    public async Task Research_HoldsATypedValueAndSurvivesAReload()
    {
        var item = await AddResearchAsync();
        await _sut.SetDefinitionsAsync(
            [Def("checked", ManuscriptPropertyScope.Research, CustomPropertyType.Date)]);

        await _sut.SetResearchValueAsync(item.Id, "checked", "2026-03-14");

        var reopened = new ProjectService(_files);
        await reopened.LoadProjectAsync(_projects.ProjectRoot!);
        var reread = new ManuscriptPropertyService(reopened, new ResearchService(reopened, _files));
        Assert.Equal("2026-03-14", reread.ResearchValues(item.Id)["checked"]);
    }

    [Fact]
    public async Task AValueTheTypeCannotHoldIsRefused()
    {
        var plotline = AddPlotline();
        await _sut.SetDefinitionsAsync(
            [Def("beats", ManuscriptPropertyScope.Plotline, CustomPropertyType.Int)]);

        await _sut.SetPlotlineValueAsync(plotline.Id, "beats", "quite a lot");

        Assert.Empty(_sut.PlotlineValues(plotline.Id));
    }

    [Fact]
    public async Task TheSameKeyMeansDifferentThingsInDifferentScopes()
    {
        // A plotline's "status" and a research item's "status" are not one
        // question, and neither should overwrite the other.
        var plotline = AddPlotline();
        var item = await AddResearchAsync();
        await _sut.SetDefinitionsAsync([
            Def("status", ManuscriptPropertyScope.Plotline),
            Def("status", ManuscriptPropertyScope.Research)
        ]);

        await _sut.SetPlotlineValueAsync(plotline.Id, "status", "Open");
        await _sut.SetResearchValueAsync(item.Id, "status", "Verified");

        Assert.Equal("Open", _sut.PlotlineValues(plotline.Id)["status"]);
        Assert.Equal("Verified", _sut.ResearchValues(item.Id)["status"]);
    }

    [Fact]
    public async Task DeletingAFieldDropsItsValuesEverywhereItReached()
    {
        var plotline = AddPlotline();
        var story = AddEvent();
        var item = await AddResearchAsync();
        await _sut.SetDefinitionsAsync([
            Def("note", ManuscriptPropertyScope.Plotline),
            Def("note", ManuscriptPropertyScope.Event),
            Def("note", ManuscriptPropertyScope.Research)
        ]);
        await _sut.SetPlotlineValueAsync(plotline.Id, "note", "a");
        await _sut.SetEventValueAsync(story.Id, "note", "b");
        await _sut.SetResearchValueAsync(item.Id, "note", "c");

        await _sut.SetDefinitionsAsync([]);

        Assert.Empty(_sut.PlotlineValues(plotline.Id));
        Assert.Empty(_sut.EventValues(story.Id));
        Assert.Empty(_sut.ResearchValues(item.Id));
        Assert.Null(plotline.Properties);
        Assert.Null(story.Properties);
    }

    [Fact]
    public async Task DeletingOneFieldLeavesTheOthersAlone()
    {
        var plotline = AddPlotline();
        await _sut.SetDefinitionsAsync([
            Def("kept", ManuscriptPropertyScope.Plotline),
            Def("dropped", ManuscriptPropertyScope.Plotline)
        ]);
        await _sut.SetPlotlineValueAsync(plotline.Id, "kept", "yes");
        await _sut.SetPlotlineValueAsync(plotline.Id, "dropped", "no");

        await _sut.SetDefinitionsAsync([Def("kept", ManuscriptPropertyScope.Plotline)]);

        Assert.Equal("yes", Assert.Single(_sut.PlotlineValues(plotline.Id)).Value);
    }

    [Fact]
    public async Task ABlankValueClearsRatherThanStoringNothing()
    {
        var story = AddEvent();
        await _sut.SetDefinitionsAsync([Def("note", ManuscriptPropertyScope.Event)]);
        await _sut.SetEventValueAsync(story.Id, "note", "something");

        await _sut.SetEventValueAsync(story.Id, "note", "   ");

        Assert.Empty(_sut.EventValues(story.Id));
        Assert.Null(story.Properties);
    }

    [Fact]
    public async Task ValuesForSomethingThatDoesNotExistAreEmptyNotAnError()
    {
        await _sut.SetDefinitionsAsync([Def("note", ManuscriptPropertyScope.Plotline)]);

        Assert.Empty(_sut.PlotlineValues("no-such-plotline"));
        Assert.Empty(_sut.EventValues("no-such-event"));
        Assert.Empty(_sut.ResearchValues("no-such-item"));
    }

    [Fact]
    public async Task SettingAValueOnSomethingThatDoesNotExistThrows()
    {
        await _sut.SetDefinitionsAsync([
            Def("note", ManuscriptPropertyScope.Plotline),
            Def("note", ManuscriptPropertyScope.Event),
            Def("note", ManuscriptPropertyScope.Research)
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetPlotlineValueAsync("nope", "note", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetEventValueAsync("nope", "note", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetResearchValueAsync("nope", "note", "x"));
    }

    [Fact]
    public async Task WithNoResearchServiceTheOtherScopesStillWork()
    {
        // The service is constructed without one wherever research is not in
        // reach; that must not take plotline and event fields down with it.
        var bare = new ManuscriptPropertyService(_projects);
        var plotline = AddPlotline();
        await bare.SetDefinitionsAsync([Def("note", ManuscriptPropertyScope.Plotline)]);

        await bare.SetPlotlineValueAsync(plotline.Id, "note", "still here");

        Assert.Equal("still here", bare.PlotlineValues(plotline.Id)["note"]);
        Assert.Empty(bare.ResearchValues("anything"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bare.SetResearchValueAsync("anything", "note", "x"));
    }

    [Fact]
    public async Task AnUnknownFieldIsRefusedRatherThanInvented()
    {
        var plotline = AddPlotline();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetPlotlineValueAsync(plotline.Id, "never-defined", "x"));
    }
}
