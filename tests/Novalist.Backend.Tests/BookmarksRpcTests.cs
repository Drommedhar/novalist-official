using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Places worth coming back to.
///
/// The favourite flag and saved lists answer "which scenes match this query".
/// A bookmark answers a different one - the paragraph where she finds out, the
/// entry I keep re-reading - and had nowhere to be recorded.
/// </summary>
public sealed class BookmarksRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly BookmarksRpc _rpc;

    public BookmarksRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-bm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BookmarkNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new BookmarksRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static BookmarkDto Draft(
        string kind = "Scene", string label = "Where she finds out",
        string? group = null, string? anchor = null)
        => new("", kind, label, group, "chapter-1", "scene-1", null, anchor, null, 0);

    [Fact]
    public async Task ABookmarkRoundTripsAndKeepsItsAnchor()
    {
        var saved = await _rpc.SaveAsync(Draft(anchor: "  She read the letter twice.  "));

        var bookmark = Assert.Single(saved);
        Assert.Equal("Where she finds out", bookmark.Label);
        Assert.Equal("Scene", bookmark.Kind);
        // Stored as text rather than an offset: prose is edited above a mark
        // constantly and an offset drifts into an unrelated sentence.
        Assert.Equal("She read the letter twice.", bookmark.AnchorText);
        Assert.NotEmpty(bookmark.Id);
    }

    [Fact]
    public async Task SavingByIdUpdatesRatherThanDuplicating()
    {
        var first = (await _rpc.SaveAsync(Draft())).Single();

        var updated = await _rpc.SaveAsync(first with { Label = "Renamed" });

        Assert.Equal("Renamed", Assert.Single(updated).Label);
    }

    [Fact]
    public async Task ABlankLabelFallsBackToTheKind()
    {
        // A bookmark made in one keystroke still has to be findable in the list.
        var saved = await _rpc.SaveAsync(Draft(label: "   "));

        Assert.Equal("Scene", Assert.Single(saved).Label);
    }

    [Fact]
    public async Task AnUnknownKindReadsAsAScene()
        => Assert.Equal("Scene", Assert.Single(await _rpc.SaveAsync(Draft(kind: "rhubarb"))).Kind);

    [Fact]
    public async Task GroupsAreListedAndSortedWithLooseOnesLast()
    {
        await _rpc.SaveAsync(Draft(label: "A", group: "  Act two  "));
        await _rpc.SaveAsync(Draft(label: "B", group: "Act one"));
        await _rpc.SaveAsync(Draft(label: "C"));

        Assert.Equal(["Act one", "Act two"], await Task.FromResult(_rpc.Groups()));

        // Grouped first, then the loose one - a named set is a deliberate act.
        Assert.Equal(["B", "A", "C"], _rpc.List().Select(b => b.Label));
    }

    [Fact]
    public async Task DeletingOneLeavesTheRest()
    {
        var kept = (await _rpc.SaveAsync(Draft(label: "Kept"))).Single();
        var doomed = (await _rpc.SaveAsync(Draft(label: "Doomed")))
            .Single(b => b.Label == "Doomed");

        var left = await _rpc.DeleteAsync(doomed.Id);

        Assert.Equal(kept.Id, Assert.Single(left).Id);
        // Deleting something that is not there is not an error.
        Assert.Single(await _rpc.DeleteAsync("no-such-id"));
    }

    [Fact]
    public async Task BookmarksNeedAProject()
    {
        var bare = new BookmarksRpc(new Workspace(Path.Combine(_root, "no-project")));

        Assert.Empty(bare.List());
        Assert.Empty(bare.Groups());
        Assert.Empty(await bare.DeleteAsync("anything"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync(Draft()));
    }

    // ── The inline preview ──

    [Fact]
    public async Task ASceneBookmarkShowsThePassageItMarks()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The Arrival");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>The bell rang once. She had known since Tuesday and said nothing.</p>",
            "The bell rang once. She had known since Tuesday and said nothing.");

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Scene",
            Label = "the bit where she finds out",
            ChapterGuid = chapter.Guid,
            TargetId = scene.Id,
            AnchorText = "known since Tuesday"
        });

        var preview = await _rpc.PreviewAsync(saved.Last().Id);

        // A bookmark that only navigates makes you go and look to remember why
        // you kept it, and for a list of thirty that is thirty trips.
        Assert.Contains("known since Tuesday", preview);
    }

    [Fact]
    public async Task AnAnchorTheProseNoLongerHasStillShowsTheScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The Arrival");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>The bell rang once.</p>", "The bell rang once.");

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Scene",
            ChapterGuid = chapter.Guid,
            TargetId = scene.Id,
            AnchorText = "a line that was rewritten away"
        });

        // An empty preview reads as a broken bookmark; the scene is still worth
        // recognising even when the sentence it named has been rewritten.
        Assert.Contains("The bell rang once", await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task ABookmarkThatIsGonePreviewsNothing()
        => Assert.Equal(string.Empty, await _rpc.PreviewAsync("no-such-bookmark"));

    [Fact]
    public async Task ASceneThatHasBeenDeletedPreviewsNothing()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Gone");
        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Scene", ChapterGuid = chapter.Guid, TargetId = scene.Id
        });
        await _workspace.Projects.DeleteSceneAsync(chapter.Guid, scene.Id);

        Assert.Equal(string.Empty, await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task AChapterBookmarkShowsItsFirstScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>The harbour was empty by six.</p>", "The harbour was empty by six.");

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Chapter", ChapterGuid = chapter.Guid
        });

        Assert.Contains("harbour was empty", await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task AnEntityBookmarkShowsWhatTheEntryIs()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        var place = new Novalist.Core.Models.LocationData
        {
            Name = "The Rookery", Description = "A tower of black brick above the harbour."
        };
        await entities.SaveLocationAsync(place);

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Entity", TargetId = place.Id, TargetType = "location"
        });

        Assert.Contains("black brick", await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Theory]
    [InlineData("character", "the harbourmaster")]
    [InlineData("item", "a brass key")]
    [InlineData("lore", "sworn at the turning")]
    public async Task EveryBuiltInTypePreviewsWhateverItCallsItsDescription(
        string type, string expected)
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        string id;
        switch (type)
        {
            case "character":
                // A character's one-line "what they are" is the role, not a
                // description field - it does not have one.
                var mira = new Novalist.Core.Models.CharacterData
                {
                    Name = "Mira", Role = "the harbourmaster"
                };
                await entities.SaveCharacterAsync(mira);
                id = mira.Id;
                break;
            case "item":
                var key = new Novalist.Core.Models.ItemData
                {
                    Name = "The Key", Description = "a brass key, worn smooth"
                };
                await entities.SaveItemAsync(key);
                id = key.Id;
                break;
            default:
                var oath = new Novalist.Core.Models.LoreData
                {
                    Name = "The Oath", Description = "sworn at the turning of the tide"
                };
                await entities.SaveLoreAsync(oath);
                id = oath.Id;
                break;
        }

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Entity", TargetId = id, TargetType = type
        });

        Assert.Contains(expected, await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task AnEntityThatHasBeenDeletedPreviewsNothing()
    {
        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Entity", TargetId = "no-such-entity", TargetType = "character"
        });

        // The bookmark outlives the entry, and an empty preview beats throwing
        // in a panel the writer only opened to glance at.
        Assert.Equal(string.Empty, await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task ABookmarkOnAWritersOwnTypeShowsItsFirstField()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCustomEntityTypeAsync(
            new Novalist.Core.Models.CustomEntityTypeDefinition
            {
                TypeKey = "ship", DisplayName = "Ship"
            });
        var ship = new Novalist.Core.Models.CustomEntityData
        {
            EntityTypeKey = "ship",
            Name = "The Corvid",
            Fields = { ["rigging"] = "A black-sailed cutter, fast in a following wind." }
        };
        await entities.SaveCustomEntityAsync(ship);

        var saved = await _rpc.SaveAsync(Draft() with
        {
            Kind = "Entity", TargetId = ship.Id, TargetType = "ship"
        });

        // A faction, a ship, a ledger - a bible lives in the types the writer
        // invented as much as in the four that ship.
        Assert.Contains("black-sailed cutter", await _rpc.PreviewAsync(saved.Last().Id));
    }

    [Fact]
    public async Task AKindWithNothingToShowPreviewsNothing()
    {
        // A story date and a map pin are places rather than prose; there is
        // nothing to extract and pretending otherwise would print a label twice.
        var saved = await _rpc.SaveAsync(Draft() with { Kind = "StoryDate", StoryDate = "1043-03-01" });

        Assert.Equal(string.Empty, await _rpc.PreviewAsync(saved.Last().Id));
    }
}
