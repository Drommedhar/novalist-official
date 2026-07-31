using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Earlier versions of the world, not just of the Codex.
///
/// Codex entries kept their history and nothing else did, so typing over a
/// plot thread's description or pasting over a research note had no answer
/// inside the app - and research is where a writer keeps the things they
/// cannot rewrite from memory.
/// </summary>
public sealed class WorldContentHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly IProjectService _projects;
    private readonly BookData _book;
    private readonly ProjectMetadata _project;

    public WorldContentHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-wch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _book = new BookData { SnapshotFolder = "Snapshots" };
        _project = new ProjectMetadata { Books = [_book] };
        _projects = Substitute.For<IProjectService>();
        _projects.ActiveBook.Returns(_book);
        _projects.CurrentProject.Returns(_project);
        _projects.ActiveDraftRoot.Returns(_root);
        _projects.SaveProjectAsync().Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ── Plot threads ──

    [Fact]
    public async Task APlotThreadKeepsWhatItSaidBefore()
    {
        var service = new PlotlineService(_projects);
        var thread = await service.CreateAsync("The crossing");
        thread.Description = "She has to get over the river.";
        await service.UpdateAsync(thread, PlotlineService.Serialize(thread));

        var edited = service.GetPlotlines().First(p => p.Id == thread.Id);
        // Taken before the edit, the way a caller does it: the list holds this
        // very object, so a moment later the old state is gone.
        var before = PlotlineService.Serialize(edited);
        edited.Description = "Replaced by mistake.";
        await service.UpdateAsync(edited, before);

        var history = service.History(thread.Id);
        Assert.NotEmpty(history);

        Assert.True(await service.RestoreAsync(thread.Id, history[0].Id));
        Assert.Equal(
            "She has to get over the river.",
            service.GetPlotlines().First(p => p.Id == thread.Id).Description);
    }

    [Fact]
    public async Task RestoringAThreadIsItselfUndoable()
    {
        var service = new PlotlineService(_projects);
        var thread = await service.CreateAsync("The crossing");
        var first = PlotlineService.Serialize(thread);
        thread.Description = "First.";
        await service.UpdateAsync(thread, first);
        var second = service.GetPlotlines().First(p => p.Id == thread.Id);
        var beforeSecond = PlotlineService.Serialize(second);
        second.Description = "Second.";
        await service.UpdateAsync(second, beforeSecond);

        var before = service.History(thread.Id).Count;
        await service.RestoreAsync(thread.Id, service.History(thread.Id)[0].Id);

        // The state the restore replaced became a version of its own.
        Assert.True(service.History(thread.Id).Count > before);
    }

    [Fact]
    public async Task ARestoredThreadKeepsTheIdItWasRestoredOnto()
    {
        // A revision put back under a different id would duplicate the thread
        // rather than restore it.
        var service = new PlotlineService(_projects);
        var thread = await service.CreateAsync("The crossing");
        var first = PlotlineService.Serialize(thread);
        thread.Description = "First.";
        await service.UpdateAsync(thread, first);
        var second = service.GetPlotlines().First(p => p.Id == thread.Id);
        var beforeSecond = PlotlineService.Serialize(second);
        second.Description = "Second.";
        await service.UpdateAsync(second, beforeSecond);

        await service.RestoreAsync(thread.Id, service.History(thread.Id)[0].Id);

        Assert.Single(service.GetPlotlines(), p => p.Id == thread.Id);
    }

    [Fact]
    public async Task AThreadRevisionThatIsNotThereIsRefused()
    {
        var service = new PlotlineService(_projects);
        var thread = await service.CreateAsync("The crossing");

        Assert.False(await service.RestoreAsync(thread.Id, "no-such-revision"));
        Assert.Empty(service.History("no-such-thread"));
    }

    [Fact]
    public async Task AThreadThatIsNotThereRecordsNothing()
    {
        var service = new PlotlineService(_projects);

        var ghost = new PlotlineData { Id = "ghost", Name = "Ghost" };
        await service.UpdateAsync(ghost, PlotlineService.Serialize(ghost));

        Assert.Empty(service.History("ghost"));
    }

    // ── Research items ──

    [Fact]
    public async Task AResearchItemKeepsWhatItSaidBefore()
    {
        var service = new ResearchService(_projects, new FileService());
        var item = new ResearchItem { Id = "r1", Title = "The bridge", Content = "Built 1846." };
        await service.SaveAsync(item);

        await service.SaveAsync(new ResearchItem
        {
            Id = "r1",
            Title = "The bridge",
            Content = "Pasted over by mistake."
        });

        var history = service.History("r1");
        Assert.NotEmpty(history);

        Assert.True(await service.RestoreAsync("r1", history[0].Id));
        Assert.Equal("Built 1846.", service.GetAll().First(r => r.Id == "r1").Content);
    }

    [Fact]
    public async Task ANewResearchItemHasNoHistoryYet()
    {
        // Nothing was replaced, so there is nothing to keep. A first save that
        // recorded a version would fill the list with empty befores.
        var service = new ResearchService(_projects, new FileService());
        await service.SaveAsync(new ResearchItem { Id = "r1", Title = "New", Content = "x" });

        Assert.Empty(service.History("r1"));
    }

    [Fact]
    public async Task AResearchRevisionThatIsNotThereIsRefused()
    {
        var service = new ResearchService(_projects, new FileService());
        await service.SaveAsync(new ResearchItem { Id = "r1", Title = "New", Content = "x" });

        Assert.False(await service.RestoreAsync("r1", "no-such-revision"));
    }

    [Fact]
    public async Task ASaveThatChangesNothingIsNotAVersion()
    {
        // Otherwise opening an item and leaving it alone would fill its
        // history with copies of itself and push the real ones out.
        var service = new ResearchService(_projects, new FileService());
        var item = new ResearchItem { Id = "r1", Title = "The bridge", Content = "Built 1846." };
        await service.SaveAsync(item);
        await service.SaveAsync(new ResearchItem
        {
            Id = "r1",
            Title = "The bridge",
            Content = "Built 1846.",
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        });

        Assert.Empty(service.History("r1"));
    }
}
