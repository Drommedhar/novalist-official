using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Where the writer put each card.
///
/// The corkboard could only lay cards out in reading order grouped by chapter,
/// which is the one arrangement the binder already shows.
/// </summary>
public sealed class CorkboardServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly CorkboardService _sut;

    public CorkboardServiceTests()
    {
        _projects.CreateProjectAsync(_dir.Path, "Board", "Book").GetAwaiter().GetResult();
        _sut = new CorkboardService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<string[]> ScenesAsync(int count)
    {
        var chapter = await _projects.CreateChapterAsync("One");
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
            ids.Add((await _projects.CreateSceneAsync(chapter.Guid, $"Scene {i + 1}")).Id);
        return [.. ids];
    }

    [Fact]
    public async Task AnUnplacedBoardIsTheBookInReadingOrder()
    {
        // Turning freeform on has to show the book as it stands, not every card
        // stacked in the corner waiting to be sorted out.
        var ids = await ScenesAsync(5);

        var placements = _sut.Placements();

        Assert.Equal(ids, placements.Select(p => p.SceneId));
        Assert.Equal(0, placements[0].X);
        Assert.Equal(0, placements[0].Y);
        // Fifth card wraps to the second row of a four-column grid.
        Assert.Equal(0, placements[4].X);
        Assert.True(placements[4].Y > 0);
    }

    [Fact]
    public async Task APlacedCardStaysWhereItWasPut()
    {
        var ids = await ScenesAsync(2);

        Assert.True(await _sut.SetPositionAsync(ids[1], 640, 300));

        var placed = _sut.Placements().Single(p => p.SceneId == ids[1]);
        Assert.Equal(640, placed.X);
        Assert.Equal(300, placed.Y);
    }

    [Fact]
    public async Task PlacingOneCardLeavesTheRestInReadingOrder()
    {
        var ids = await ScenesAsync(3);
        await _sut.SetPositionAsync(ids[0], 900, 900);

        var placements = _sut.Placements();

        Assert.Equal(900, placements[0].X);
        Assert.Equal(CorkboardService.DefaultX(1), placements[1].X);
        Assert.Equal(CorkboardService.DefaultY(1), placements[1].Y);
    }

    [Fact]
    public async Task ADragThatEndsOffTheBoardIsClampedRatherThanLost()
    {
        var ids = await ScenesAsync(1);

        await _sut.SetPositionAsync(ids[0], -500, 999_999);

        var placed = _sut.Placements()[0];
        Assert.Equal(CorkboardService.MinCoordinate, placed.X);
        Assert.Equal(CorkboardService.MaxCoordinate, placed.Y);
    }

    [Fact]
    public async Task AnArrangementSurvivesReopeningTheProject()
    {
        var ids = await ScenesAsync(2);
        await _sut.SetPositionAsync(ids[0], 400, 250);

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(_projects.ProjectRoot!);

        var placed = new CorkboardService(reopened).Placements().Single(p => p.SceneId == ids[0]);
        Assert.Equal(400, placed.X);
        Assert.Equal(250, placed.Y);
    }

    [Fact]
    public async Task ResetForgetsTheArrangementAndCountsWhatItCleared()
    {
        var ids = await ScenesAsync(3);
        await _sut.SetPositionAsync(ids[0], 100, 100);
        await _sut.SetPositionAsync(ids[2], 200, 200);

        Assert.Equal(2, await _sut.ResetAsync());

        var placements = _sut.Placements();
        Assert.Equal(CorkboardService.DefaultX(0), placements[0].X);
        Assert.Equal(CorkboardService.DefaultX(2), placements[2].X);
    }

    [Fact]
    public async Task ResettingAnUnarrangedBoardWritesNothing()
    {
        await ScenesAsync(2);
        Assert.Equal(0, await _sut.ResetAsync());
    }

    [Fact]
    public async Task AnUnknownSceneIsRefusedRatherThanCreated()
    {
        await ScenesAsync(1);
        Assert.False(await _sut.SetPositionAsync("not-a-scene", 10, 10));
    }

    [Fact]
    public void NoProjectOpen_IsAnEmptyBoard()
    {
        var bare = new CorkboardService(new ProjectService(new FileService()));
        Assert.Empty(bare.Placements());
    }

    [Fact]
    public async Task NoProjectOpen_ResetClearsNothing()
    {
        var bare = new CorkboardService(new ProjectService(new FileService()));
        Assert.Equal(0, await bare.ResetAsync());
        Assert.False(await bare.SetPositionAsync("anything", 0, 0));
    }

    [Fact]
    public async Task CardsFromEveryChapterShareOneBoard()
    {
        // A freeform board is the book, not one chapter at a time - the whole
        // point is putting scenes from different chapters beside each other.
        var first = await _projects.CreateChapterAsync("One");
        await _projects.CreateSceneAsync(first.Guid, "A");
        var second = await _projects.CreateChapterAsync("Two");
        await _projects.CreateSceneAsync(second.Guid, "B");

        Assert.Equal(["A", "B"], _sut.Placements()
            .Select(p => _projects.GetScenesForChapter(first.Guid)
                .Concat(_projects.GetScenesForChapter(second.Guid))
                .First(s => s.Id == p.SceneId).Title));
    }
}
