using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Free-form Wiki articles: the ones about the world rather than about one
/// entry in it.
///
/// Every article was generated from a Codex entity, so an essay on how the
/// economy works had to hang off whichever entity it least badly belonged to,
/// or live in Research outside the Wiki. Only Locations nested, so filing one
/// article under another was not possible either.
/// </summary>
public class WikiPageServiceTests
{
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly ProjectMetadata _metadata = new();
    private readonly WikiPageService _sut;

    public WikiPageServiceTests()
    {
        _project.CurrentProject.Returns(_metadata);
        _sut = new WikiPageService(_project);
    }

    private Task<WikiPage> AddAsync(string title, string? parentId = null)
        => _sut.SaveAsync(null, title, string.Empty, parentId);

    [Fact]
    public async Task AnArticleIsSavedWithItsProse()
    {
        var page = await _sut.SaveAsync(null, "  The economy  ", "Salt is the currency.", null);

        Assert.Equal("The economy", page.Title);
        Assert.Equal("Salt is the currency.", page.Body);
        Assert.Equal(string.Empty, page.ParentId);
        await _project.Received().SaveProjectAsync();
    }

    [Fact]
    public async Task AnArticleWithNoTitleIsStillSaved()
    {
        var page = await AddAsync("   ");

        // A writer who starts an essay and names it afterwards should not lose
        // the essay.
        Assert.Equal(string.Empty, page.Title);
        Assert.Single(_sut.GetAll());
    }

    [Fact]
    public async Task SavingAgainUpdatesRatherThanDuplicates()
    {
        var page = await AddAsync("The economy");

        await _sut.SaveAsync(page.Id, "How the economy works", "Salt.", null);

        var single = Assert.Single(_sut.GetAll());
        Assert.Equal("How the economy works", single.Title);
        Assert.Equal("Salt.", single.Body);
    }

    [Fact]
    public async Task AnArticleCanSitUnderAnother()
    {
        var parent = await AddAsync("The world");

        var child = await AddAsync("The economy", parent.Id);

        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task NestingGoesAsDeepAsTheWorldDoes()
    {
        var world = await AddAsync("The world");
        var region = await AddAsync("The north", world.Id);
        var town = await AddAsync("Hillsford", region.Id);

        Assert.Equal(region.Id, town.ParentId);
        Assert.Equal(3, _sut.GetAll().Count);
    }

    // ─── Rings ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnArticleCannotSitUnderItself()
    {
        var page = await AddAsync("The economy");

        var moved = await _sut.MoveAsync(page.Id, page.Id);

        Assert.Equal(string.Empty, moved!.ParentId);
    }

    [Fact]
    public async Task AnArticleCannotSitUnderItsOwnChild()
    {
        var parent = await AddAsync("The world");
        var child = await AddAsync("The economy", parent.Id);

        var moved = await _sut.MoveAsync(parent.Id, child.Id);

        // A ring makes the tree unreachable from the top: the articles inside
        // it are still in the file and can never be opened again.
        Assert.Equal(string.Empty, moved!.ParentId);
        Assert.Equal(parent.Id, _sut.Get(child.Id)!.ParentId);
    }

    [Fact]
    public async Task AnArticleCannotSitUnderItsOwnGrandchild()
    {
        var world = await AddAsync("The world");
        var region = await AddAsync("The north", world.Id);
        var town = await AddAsync("Hillsford", region.Id);

        var moved = await _sut.MoveAsync(world.Id, town.Id);

        Assert.Equal(string.Empty, moved!.ParentId);
    }

    [Fact]
    public async Task AParentThatIsGoneIsNoParent()
    {
        var page = await AddAsync("The economy", "no-such-page");

        Assert.Equal(string.Empty, page.ParentId);
    }

    [Fact]
    public async Task MovingToTheTopLevelIsAllowed()
    {
        var parent = await AddAsync("The world");
        var child = await AddAsync("The economy", parent.Id);

        var moved = await _sut.MoveAsync(child.Id, "  ");

        Assert.Equal(string.Empty, moved!.ParentId);
    }

    [Fact]
    public async Task MovingSomethingThatIsGoneIsQuiet()
        => Assert.Null(await _sut.MoveAsync("no-such-page", null));

    // ─── Deleting ────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingAnArticleLiftsItsChildrenIntoItsPlace()
    {
        var world = await AddAsync("The world");
        var region = await AddAsync("The north", world.Id);
        var town = await AddAsync("Hillsford", region.Id);

        Assert.True(await _sut.DeleteAsync(region.Id));

        // A page is a container as much as an article, and deleting the
        // container should not take the writing inside it.
        Assert.Equal(2, _sut.GetAll().Count);
        Assert.Equal(world.Id, _sut.Get(town.Id)!.ParentId);
    }

    [Fact]
    public async Task DeletingATopLevelArticleLeavesItsChildrenAtTheTop()
    {
        var world = await AddAsync("The world");
        var region = await AddAsync("The north", world.Id);

        await _sut.DeleteAsync(world.Id);

        Assert.Equal(string.Empty, _sut.Get(region.Id)!.ParentId);
    }

    [Fact]
    public async Task DeletingSomethingThatIsGoneIsQuiet()
        => Assert.False(await _sut.DeleteAsync("no-such-page"));

    // ─── With no project ─────────────────────────────────────────────

    [Fact]
    public async Task WithNoProjectOpenNothingCanBeWritten()
    {
        _project.CurrentProject.Returns((ProjectMetadata?)null);
        var orphan = new WikiPageService(_project);

        Assert.Empty(orphan.GetAll());
        Assert.Null(orphan.Get("anything"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orphan.SaveAsync(null, "x", null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => orphan.MoveAsync("x", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => orphan.DeleteAsync("x"));
    }
}
