using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Codex names sitting in prose as plain text. Novalist recognised a bare name
/// for the Wiki and the hover card, but only a real mention counts towards the
/// appearance figures - so an imported manuscript under-reported all of them.
/// </summary>
public sealed class UnlinkedMentionServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly EntityService _entities;
    private readonly UnlinkedMentionService _sut;
    private ChapterData _chapter = null!;
    private SceneData _scene = null!;
    private CharacterData _mira = null!;

    public UnlinkedMentionServiceTests()
    {
        _projects.CreateProjectAsync(_dir.Path, "P", "Book").GetAwaiter().GetResult();
        _entities = new EntityService(_projects);
        _sut = new UnlinkedMentionService(_projects, _entities);
    }

    public void Dispose() => _dir.Dispose();

    private async Task SeedAsync(string html)
    {
        _mira = new CharacterData { Name = "Mira", Surname = "Vance" };
        await _entities.SaveCharacterAsync(_mira);
        _chapter = await _projects.CreateChapterAsync("One");
        _scene = await _projects.CreateSceneAsync(_chapter.Guid, "Opening");
        await _projects.WriteSceneContentAsync(_chapter, _scene, html);
    }

    private async Task<string> SceneHtmlAsync()
        => await _projects.ReadSceneContentAsync(_chapter, _scene);

    [Fact]
    public async Task FindsAPlainNameAndCountsIt()
    {
        await SeedAsync("<p>Mira crossed the yard. Mira did not look back.</p>");

        var found = Assert.Single(await _sut.FindAsync());

        Assert.Equal("Mira Vance", found.EntityName);
        Assert.Equal(2, found.Count);
        Assert.Contains("crossed the yard", found.Context);
    }

    [Fact]
    public async Task ANameAlreadyLinkedIsNotUnlinked()
    {
        await SeedAsync(
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"x\">Mira</span> waited.</p>");

        // Offering to link what is already linked is the one thing this must
        // not do.
        Assert.Empty(await _sut.FindAsync());
    }

    [Fact]
    public async Task ANameInsideAWordIsNotAMention()
    {
        await SeedAsync("<p>The miracle was admired.</p>");

        Assert.Empty(await _sut.FindAsync());
    }

    [Fact]
    public async Task AProjectWithNoEntitiesFindsNothing()
    {
        _chapter = await _projects.CreateChapterAsync("One");
        _scene = await _projects.CreateSceneAsync(_chapter.Guid, "Opening");
        await _projects.WriteSceneContentAsync(_chapter, _scene, "<p>Nothing here.</p>");

        Assert.Empty(await _sut.FindAsync());
    }

    [Fact]
    public async Task AnEmptySceneIsSkipped()
    {
        await SeedAsync(string.Empty);

        Assert.Empty(await _sut.FindAsync());
    }

    [Fact]
    public async Task LinkingWrapsEveryOccurrenceAndLeavesTheProseIntact()
    {
        await SeedAsync("<p>Mira crossed the yard. Mira did not look back.</p>");

        var converted = await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id);

        Assert.Equal(2, converted);
        var html = await SceneHtmlAsync();
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "nv-entity-mention").Count);
        Assert.Contains("crossed the yard", html);
        Assert.Empty(await _sut.FindAsync());
    }

    [Fact]
    public async Task LinkingNeverTouchesMarkupOrAnExistingMention()
    {
        await SeedAsync(
            "<p title=\"Mira\">Mira waited beside "
            + "<span class=\"nv-entity-mention\" data-entity-id=\"other\">Mira</span>.</p>");

        await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id);

        var html = await SceneHtmlAsync();
        // The attribute is markup, not prose: rewriting it would produce a
        // document the editor cannot read.
        Assert.Contains("title=\"Mira\"", html);
        Assert.Contains("data-entity-id=\"other\"", html);
        // Exactly one new mention: the bare one.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "nv-entity-mention").Count);
    }

    [Fact]
    public async Task LinkingPrefersTheLongestNameSoAFullNameStaysOneMention()
    {
        await SeedAsync("<p>Mira Vance signed it.</p>");

        var converted = await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id);

        // One mention over the whole name, not "Mira" with "Vance" left loose.
        Assert.Equal(1, converted);
        Assert.Contains(">Mira Vance</span>", await SceneHtmlAsync());
    }

    [Fact]
    public async Task LinkingSomethingThatIsNotThereChangesNothing()
    {
        await SeedAsync("<p>Nobody is named here.</p>");

        Assert.Equal(0, await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id));
        Assert.Equal(0, await _sut.LinkAsync(_chapter.Guid, _scene.Id, "no-such-entity"));
        Assert.Equal(0, await _sut.LinkAsync("no-such-chapter", _scene.Id, _mira.Id));
        Assert.Equal(0, await _sut.LinkAsync(_chapter.Guid, "no-such-scene", _mira.Id));
    }

    [Fact]
    public async Task LinkingAnEmptySceneChangesNothing()
    {
        await SeedAsync(string.Empty);

        Assert.Equal(0, await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id));
    }

    [Fact]
    public async Task AnUnclosedTagIsLeftAloneRatherThanMangled()
    {
        await SeedAsync("<p>Mira waited.<span");

        await _sut.LinkAsync(_chapter.Guid, _scene.Id, _mira.Id);

        // Whatever the editor meant by it, guessing would be worse.
        Assert.EndsWith("<span", await SceneHtmlAsync());
    }
}
