using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Files kept with a Codex entry.
///
/// Entries could hold images and nothing else, so a recorded interview, a
/// scanned deed or a pronunciation clip had to be filed as a Research item and
/// linked back - stored and surfaced somewhere other than the entry it is about.
/// </summary>
public sealed class AttachmentsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly AttachmentsRpc _rpc;
    private readonly EntityService _entities;
    private readonly string _characterId;

    public AttachmentsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-att-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "AttNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _entities = new EntityService(_workspace.Projects);

        var mira = new CharacterData { Name = "Mira" };
        _entities.SaveCharacterAsync(mira).GetAwaiter().GetResult();
        _characterId = mira.Id;
        _rpc = new AttachmentsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A real file to attach, since the point is that it gets copied.</summary>
    private string WriteFile(string name, string contents = "data")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private Task<AttachmentDto[]> ListAsync() => _rpc.ListAsync("character", _characterId);

    [Fact]
    public async Task AnEntryStartsWithNothingAttached()
        => Assert.Empty(await ListAsync());

    [Fact]
    public async Task AnEntryThatIsNotThereHasNoAttachments()
        => Assert.Empty(await _rpc.ListAsync("character", "no-such-entry"));

    [Fact]
    public async Task AFileIsCopiedIntoTheProject()
    {
        var source = WriteFile("interview.mp3");

        var saved = await _rpc.AddAsync("character", _characterId, source);

        var attachment = Assert.Single(saved);
        Assert.Equal("interview.mp3", attachment.Name);
        // Copied rather than referenced: a path into somebody's Downloads
        // folder is a file that will be gone by the time anyone follows it.
        Assert.NotEqual(source, attachment.FullPath);
        Assert.True(File.Exists(attachment.FullPath));
        Assert.Contains(EntityService.AttachmentFolder, attachment.FullPath);
    }

    [Fact]
    public async Task TheKindIsReadFromTheFileName()
    {
        await _rpc.AddAsync("character", _characterId, WriteFile("interview.mp3"));
        await _rpc.AddAsync("character", _characterId, WriteFile("walkthrough.mp4"));
        await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf"));
        await _rpc.AddAsync("character", _characterId, WriteFile("mystery.qqq"));

        // The writer sees a recording as a recording without saying so, and an
        // unknown format still attaches - only the icon is less specific.
        Assert.Equal(["Audio", "Video", "Document", "File"],
            (await ListAsync()).Select(a => a.Kind));
    }

    [Fact]
    public async Task TheSameFileAttachedTwiceIsOneFileOnDisk()
    {
        var first = await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf", "same"));
        var second = await _rpc.AddAsync("character", _characterId, WriteFile("deed-copy.pdf", "same"));

        // Matched on contents rather than name, because a browser saves the
        // third copy as "deed (2).pdf".
        Assert.Equal(2, second.Length);
        Assert.Equal(first[0].FullPath, second[1].FullPath);
    }

    [Fact]
    public async Task TwoDifferentFilesWithOneNameBothSurvive()
    {
        await _rpc.AddAsync("character", _characterId, WriteFile("scan.pdf", "one"));
        var sub = Path.Combine(_root, "other");
        Directory.CreateDirectory(sub);
        var second = Path.Combine(sub, "scan.pdf");
        File.WriteAllText(second, "two");

        var saved = await _rpc.AddAsync("character", _characterId, second);

        // Suffixed rather than overwritten: two different files called scan.pdf
        // are two files, and losing one silently is the worst outcome here.
        Assert.NotEqual(saved[0].FullPath, saved[1].FullPath);
        Assert.Equal("one", await File.ReadAllTextAsync(saved[0].FullPath));
        Assert.Equal("two", await File.ReadAllTextAsync(saved[1].FullPath));
    }

    [Fact]
    public async Task ANameCanBeGivenInsteadOfTheFileName()
    {
        var saved = await _rpc.AddAsync(
            "character", _characterId, WriteFile("IMG_4821.m4a"), "How she says her own name");

        Assert.Equal("How she says her own name", Assert.Single(saved).Name);
    }

    [Fact]
    public async Task AFileThatIsNotThereIsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync("character", _characterId, Path.Combine(_root, "gone.pdf")));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync("character", _characterId, "  "));
    }

    [Fact]
    public async Task AttachingToAnEntryThatIsGoneThrows()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync("character", "no-such-entry", WriteFile("deed.pdf")));

    // ── Links ──

    [Fact]
    public async Task ALinkCopiesNothing()
    {
        var saved = await _rpc.AddLinkAsync(
            "character", _characterId, " https://example.test/deed ", "The deed online");

        var link = Assert.Single(saved);
        Assert.Equal("Link", link.Kind);
        Assert.Equal("https://example.test/deed", link.Url);
        // Pretending to have saved the page would be a promise this cannot keep.
        Assert.Equal(string.Empty, link.FullPath);
        Assert.Equal("The deed online", link.Name);
    }

    [Fact]
    public async Task ALinkWithNoNameIsCalledByItsAddress()
        => Assert.Equal(
            "https://example.test",
            Assert.Single(await _rpc.AddLinkAsync("character", _characterId, "https://example.test")).Name);

    [Fact]
    public async Task AnEmptyAddressIsRefused()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddLinkAsync("character", _characterId, "   "));

    // ── Editing and removing ──

    [Fact]
    public async Task RenamingAndNotingStick()
    {
        var saved = await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf"));

        var updated = await _rpc.UpdateAsync(
            "character", _characterId, saved[0].Id, " The deed ", " Settles who owns the house. ");

        Assert.Equal("The deed", updated[0].Name);
        Assert.Equal("Settles who owns the house.", updated[0].Note);
    }

    [Fact]
    public async Task AnEmptyRenameIsNoRename()
    {
        var saved = await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf"));

        // A blank name leaves a row nobody can tell from the next one.
        var updated = await _rpc.UpdateAsync("character", _characterId, saved[0].Id, "  ");

        Assert.Equal("deed.pdf", updated[0].Name);
    }

    [Fact]
    public async Task UpdatingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf"));

        Assert.Single(await _rpc.UpdateAsync("character", _characterId, "no-such-attachment", "X"));
    }

    [Fact]
    public async Task RemovingTakesItOffTheEntryAndLeavesTheFile()
    {
        var saved = await _rpc.AddAsync("character", _characterId, WriteFile("interview.mp3"));
        var stored = saved[0].FullPath;

        Assert.Empty(await _rpc.RemoveAsync("character", _characterId, saved[0].Id));

        // Another entry may point at the same file, and deleting somebody's
        // only copy of a recording because they tidied a Codex entry is not a
        // trade anybody would accept.
        Assert.True(File.Exists(stored));
    }

    [Fact]
    public async Task RemovingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.AddAsync("character", _characterId, WriteFile("deed.pdf"));

        Assert.Single(await _rpc.RemoveAsync("character", _characterId, "no-such-attachment"));
    }

    // ── Every type, not just characters ──

    [Fact]
    public async Task EveryEntityTypeCanCarryAFile()
    {
        var place = new LocationData { Name = "The Rookery" };
        await _entities.SaveLocationAsync(place);
        var relic = new ItemData { Name = "The Crest" };
        await _entities.SaveItemAsync(relic);
        var oath = new LoreData { Name = "The Oath" };
        await _entities.SaveLoreAsync(oath);
        await _entities.SaveCustomEntityTypeAsync(
            new CustomEntityTypeDefinition { TypeKey = "ship", DisplayName = "Ship" });
        var ship = new CustomEntityData { EntityTypeKey = "ship", Name = "The Corvid" };
        await _entities.SaveCustomEntityAsync(ship);

        Assert.Single(await _rpc.AddAsync("location", place.Id, WriteFile("plan.pdf")));
        Assert.Single(await _rpc.AddAsync("item", relic.Id, WriteFile("photo.png")));
        Assert.Single(await _rpc.AddAsync("lore", oath.Id, WriteFile("wording.txt")));
        // A bible lives in the types the writer invented as much as the four
        // that ship.
        Assert.Single(await _rpc.AddAsync("ship", ship.Id, WriteFile("rigging.pdf")));
    }

    [Fact]
    public async Task AnAttachmentSurvivesReopeningTheProject()
    {
        await _rpc.AddAsync("character", _characterId, WriteFile("interview.mp3"));

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(await ListAsync());
    }
}
