using NSubstitute;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Prose the writer cut and kept.
///
/// Deleted text was recoverable only by opening a snapshot of the whole scene
/// and reading it for the paragraph that used to be there. A paragraph cut
/// because it does not belong in this chapter is not a mistake to undo - it is
/// writing looking for a different home, and there was nowhere to put it.
/// </summary>
public class DarlingsServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly DarlingsService _sut;

    public DarlingsServiceTests()
    {
        _project.ProjectRoot.Returns(_dir.Path);
        _sut = new DarlingsService(_project, new FileService());
    }

    public void Dispose() => _dir.Dispose();

    private string File_ => Path.Combine(_dir.Path, ".novalist", "darlings.json");

    [Fact]
    public async Task AProjectThatHasCutNothingHasAnEmptyBin()
    {
        Assert.Empty(await _sut.ListAsync());
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public async Task ACutIsKeptWholeWithWhereItCameFrom()
    {
        await _sut.KeepAsync("  She had never once looked back.  ", "Chapter One - Arrival");

        var kept = Assert.Single(await _sut.ListAsync());
        Assert.Equal("She had never once looked back.", kept.Text);
        Assert.Equal("Chapter One - Arrival", kept.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task NothingWorthKeepingIsNotKept(string? text)
    {
        await _sut.KeepAsync(text);

        // Cutting a space and being offered to save it is how a writer learns
        // to ignore the feature.
        Assert.Empty(await _sut.ListAsync());
    }

    [Fact]
    public async Task TheNewestComesFirst()
    {
        await _sut.KeepAsync("first");
        await _sut.KeepAsync("second");

        Assert.Equal("second", (await _sut.ListAsync())[0].Text);
    }

    [Fact]
    public async Task ACutCanCarryWhyItWasKept()
    {
        var kept = await _sut.KeepAsync("She had never looked back.");

        var updated = await _sut.SetNoteAsync(kept[0].Id, "  use in chapter nine  ");

        Assert.Equal("use in chapter nine", Assert.Single(updated).Note);
    }

    [Fact]
    public async Task NotingSomethingThatIsGoneIsQuiet()
    {
        await _sut.KeepAsync("kept");

        Assert.Single(await _sut.SetNoteAsync("no-such-cut", "x"));
    }

    [Fact]
    public async Task ACutCanBeThrownAwayForGood()
    {
        var kept = await _sut.KeepAsync("kept");

        Assert.Empty(await _sut.RemoveAsync(kept[0].Id));
    }

    [Fact]
    public async Task RemovingSomethingThatIsGoneIsQuiet()
    {
        await _sut.KeepAsync("kept");

        Assert.Single(await _sut.RemoveAsync("no-such-cut"));
    }

    [Fact]
    public async Task TheBinDoesNotGrowWithoutEnd()
    {
        for (var i = 0; i < DarlingsService.MaxKept + 20; i++)
            await _sut.KeepAsync($"cut number {i}");

        var all = await _sut.ListAsync();

        // A writer who cuts all day should not end up with a file bigger than
        // the manuscript. The oldest goes first: a recent cut is the one about
        // to be used.
        Assert.Equal(DarlingsService.MaxKept, all.Count);
        Assert.DoesNotContain(all, d => d.Text == "cut number 0");
        Assert.Contains(all, d => d.Text == $"cut number {DarlingsService.MaxKept + 19}");
    }

    [Fact]
    public async Task CutsSurviveReadingTheFileBack()
    {
        await _sut.KeepAsync("She had never looked back.", "Chapter One");

        var reread = await new DarlingsService(_project, new FileService()).ListAsync();

        // Cut prose belongs to the project rather than the machine, so it
        // travels with the book.
        Assert.Equal("She had never looked back.", Assert.Single(reread).Text);
    }

    [Fact]
    public async Task ACorruptBinLosesTheCutsAndNothingElse()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        await File.WriteAllTextAsync(File_, "{ not json");

        Assert.Empty(await _sut.ListAsync());
    }

    [Fact]
    public async Task WithNoProjectOpenNothingIsWritten()
    {
        _project.ProjectRoot.Returns((string?)null);
        var orphan = new DarlingsService(_project, new FileService());

        var kept = await orphan.KeepAsync("kept");

        Assert.Single(kept);
        Assert.Empty(await orphan.ListAsync());
    }
}
