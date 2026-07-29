using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Export layouts from the RPC the Export view calls.</summary>
public sealed class ExportPresetRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ExportPresetRpc _rpc;

    public ExportPresetRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PresetNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ExportPresetRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void TheBuiltInsAreListedAndMarkedAsSuch()
    {
        Assert.NotEmpty(_rpc.List());
        Assert.All(_rpc.List(), p => Assert.False(p.IsCustom));
    }

    [Fact]
    public async Task DuplicatingAddsAnEditableCopy()
    {
        var all = await _rpc.DuplicateAsync("default", "Mine");

        Assert.Equal("Mine", all.Last().DisplayName);
        Assert.True(all.Last().IsCustom);
    }

    [Fact]
    public async Task SavingAUserPresetRoundTrips()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Mine")).Last();

        var all = await _rpc.SaveAsync(copy with { SceneSeparator = "~~~", ShowSceneTitles = true });

        var saved = all.Single(p => p.Id == copy.Id);
        Assert.Equal("~~~", saved.SceneSeparator);
        Assert.True(saved.ShowSceneTitles);
    }

    [Fact]
    public async Task ANonsenseFontSizeFallsBackRatherThanProducingAFileNobodyCanOpen()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Mine")).Last();

        var all = await _rpc.SaveAsync(copy with { BodyFontSizePt = 0, MarginInches = 99 });

        var saved = all.Single(p => p.Id == copy.Id);
        Assert.Equal(12, saved.BodyFontSizePt);
        Assert.Equal(1, saved.MarginInches);
    }

    [Fact]
    public async Task ABlankChapterHeadingFallsBackToTheTitleAlone()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Mine")).Last();

        var all = await _rpc.SaveAsync(copy with { ChapterTitleFormat = "  " });

        Assert.Equal("{title}", all.Single(p => p.Id == copy.Id).ChapterTitleFormat);
    }

    [Fact]
    public async Task DeletingRemovesAUserPresetOnly()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Mine")).Last();

        var afterUser = await _rpc.DeleteAsync(copy.Id);
        Assert.DoesNotContain(afterUser, p => p.Id == copy.Id);

        var afterBuiltIn = await _rpc.DeleteAsync("default");
        Assert.Contains(afterBuiltIn, p => p.Id == "default");
    }
}
