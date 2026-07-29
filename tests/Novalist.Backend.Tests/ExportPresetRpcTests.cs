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

    // ── The print page ──
    //
    // A manuscript is one page size with one margin all round because it is
    // read on a screen. A file that gets a bound book's page wrong is rejected
    // by the printer or comes back with text in the gutter.

    private static PrintSpecDto Page(
        double trimWidth = 6, double bleed = 0, double inside = 0.9, int minLines = 2) =>
        new(trimWidth, 9, inside, 0.5, 0.75, 0.75, true, 0.1, false, bleed, true, minLines);

    [Fact]
    public async Task APrintPageRoundTrips()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Print")).Last();

        var all = await _rpc.SaveAsync(copy with { Print = Page(bleed: 0.125) });

        var saved = all.Single(p => p.Id == copy.Id).Print;
        Assert.NotNull(saved);
        Assert.Equal(6, saved!.TrimWidthInches, 2);
        Assert.Equal(9, saved.TrimHeightInches, 2);
        Assert.Equal(0.125, saved.BleedInches, 3);
        Assert.Equal(0.9, saved.MarginInsideInches, 2);
        Assert.True(saved.MirrorMargins);
        Assert.False(saved.GutterFromPageCount);
    }

    [Fact]
    public async Task NoPrintPageStaysNoPrintPage()
    {
        // Null is the manuscript page, which is right for a submission. It has
        // to survive a save rather than being filled in with a default.
        var copy = (await _rpc.DuplicateAsync("default", "Plain")).Last();

        var all = await _rpc.SaveAsync(copy with { Print = null });

        Assert.Null(all.Single(p => p.Id == copy.Id).Print);
    }

    [Fact]
    public async Task ATrimNobodyPrintsIsRefused()
    {
        // A typo in a page size is a file the printer rejects, so it falls back
        // rather than travelling into the PDF.
        var copy = (await _rpc.DuplicateAsync("default", "Silly")).Last();

        var all = await _rpc.SaveAsync(copy with { Print = Page(trimWidth: 0) });

        Assert.Equal(8.5, all.Single(p => p.Id == copy.Id).Print!.TrimWidthInches, 2);
    }

    [Fact]
    public async Task AnAbsurdBleedIsClamped()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Bleedy")).Last();

        var all = await _rpc.SaveAsync(copy with { Print = Page(bleed: 12) });

        Assert.Equal(0, all.Single(p => p.Id == copy.Id).Print!.BleedInches, 3);
    }

    [Fact]
    public async Task AWidowRuleIsClampedToSomethingATypesetterWouldUse()
    {
        var copy = (await _rpc.DuplicateAsync("default", "Widows")).Last();

        var all = await _rpc.SaveAsync(copy with { Print = Page(minLines: 99) });

        Assert.Equal(5, all.Single(p => p.Id == copy.Id).Print!.MinLinesTogether);
    }

    [Fact]
    public void TheTrimsWeKnowByNameAreOffered()
    {
        var trims = _rpc.Trims();

        Assert.Contains(trims, t => t.Name == "us-trade" && t.WidthInches == 6);
        Assert.All(trims, t => Assert.True(t.WidthInches > 0 && t.HeightInches > 0));
    }
}
