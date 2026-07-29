using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Setups and their payoffs. Novalist had no edge between two scenes at all,
/// so the one question worth asking about a setup - does anything answer it -
/// could not be asked.
/// </summary>
public sealed class PromiseServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly PromiseService _sut;
    private readonly SceneData _first;
    private readonly SceneData _second;

    public PromiseServiceTests()
    {
        _projects.CreateProjectAsync(_dir.Path, "P", "Book").GetAwaiter().GetResult();
        var chapter = _projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _first = _projects.CreateSceneAsync(chapter.Guid, "The mantel").GetAwaiter().GetResult();
        _second = _projects.CreateSceneAsync(chapter.Guid, "The shot").GetAwaiter().GetResult();
        _sut = new PromiseService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task APromiseWithNoPayoffIsUnpaid()
    {
        await _sut.SaveAsync(_first.Id, null, "  the gun on the mantel  ", null);

        var report = Assert.Single(_sut.Report());

        Assert.Equal("the gun on the mantel", report.Label);
        Assert.Equal(PromiseState.Unpaid, report.State);
        Assert.Equal("The mantel", report.SceneTitle);
        Assert.Null(report.PayoffSceneTitle);
    }

    [Fact]
    public async Task APayoffLaterInTheBookKeepsIt()
    {
        await _sut.SaveAsync(_first.Id, null, "the gun", _second.Id);

        var report = Assert.Single(_sut.Report());

        Assert.Equal(PromiseState.Kept, report.State);
        Assert.Equal("The shot", report.PayoffSceneTitle);
    }

    [Fact]
    public async Task APayoffBeforeTheSetupIsOutOfOrder()
    {
        // Moving a scene is enough to cause this, which is why it is worth
        // reporting rather than being treated as kept.
        await _sut.SaveAsync(_second.Id, null, "the gun", _first.Id);

        Assert.Equal(PromiseState.OutOfOrder, Assert.Single(_sut.Report()).State);
    }

    [Fact]
    public async Task APayoffSceneThatIsGoneIsBroken()
    {
        await _sut.SaveAsync(_first.Id, null, "the gun", _second.Id);

        await _projects.DeleteSceneAsync(_second.ChapterGuid, _second.Id);

        Assert.Equal(PromiseState.Broken, Assert.Single(_sut.Report()).State);
    }

    [Fact]
    public async Task ASceneCannotPayOffItsOwnPromise()
    {
        await _sut.SaveAsync(_first.Id, null, "the gun", _first.Id);

        // Stored as unanswered rather than as kept: nothing has been answered.
        Assert.Equal(PromiseState.Unpaid, Assert.Single(_sut.Report()).State);
    }

    [Fact]
    public async Task SavingAgainEditsRatherThanDuplicating()
    {
        var id = await _sut.SaveAsync(_first.Id, null, "the gun", null);

        await _sut.SaveAsync(_first.Id, id, "the gun over the mantel", _second.Id);

        var report = Assert.Single(_sut.Report());
        Assert.Equal("the gun over the mantel", report.Label);
        Assert.Equal(PromiseState.Kept, report.State);
    }

    [Fact]
    public async Task ABlankLabelIsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SaveAsync(_first.Id, null, "   ", null));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SaveAsync("no-such-scene", null, "x", null));
    }

    [Fact]
    public async Task DeleteRemovesIt_AndSaysWhenThereWasNothingToRemove()
    {
        var id = await _sut.SaveAsync(_first.Id, null, "the gun", null);

        Assert.False(await _sut.DeleteAsync(_second.Id, id));
        Assert.False(await _sut.DeleteAsync(_first.Id, "no-such-promise"));
        Assert.True(await _sut.DeleteAsync(_first.Id, id));
        Assert.Empty(_sut.Report());
        // Removing the last one leaves no empty list behind on the scene.
        Assert.Null(_projects.GetScenesForChapter(_first.ChapterGuid)
            .First(s => s.Id == _first.Id).Promises);
    }

    [Fact]
    public void AProjectWithNoPromisesReportsNothing() => Assert.Empty(_sut.Report());
}
