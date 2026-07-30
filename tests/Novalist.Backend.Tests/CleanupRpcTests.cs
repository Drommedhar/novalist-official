using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// A cleanup pass over prose the writer already has.
///
/// Auto-replacements fire while typing and skip pasted text on purpose, so a
/// chapter written elsewhere and pasted in kept its straight quotes for good.
/// </summary>
public sealed class CleanupRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CleanupRpc _rpc;
    private readonly string _chapterGuid;

    public CleanupRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CleanNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival").GetAwaiter().GetResult();
        _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>  \"He left--again...\"  </p><p></p><p>***</p><p>Then  she did.</p>",
            "He left again. Then she did.").GetAwaiter().GetResult();
        _rpc = new CleanupRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<string> SceneTextAsync()
    {
        var chapter = _workspace.Projects.GetChaptersOrdered()[0];
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid)[0];
        return await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
    }

    private static string[] EveryRule() =>
    [
        "SmartenQuotes", "Typography", "CollapseSpaces",
        "TrimParagraphs", "DropEmptyParagraphs", "NormaliseSceneBreaks"
    ];

    [Fact]
    public async Task ThePreviewChangesNothing()
    {
        var before = await SceneTextAsync();

        var report = await _rpc.PreviewAsync(EveryRule());

        // A pass that rewrites every scene in a book is not something a writer
        // should find out about afterwards.
        Assert.Equal(1, report.ScenesConsidered);
        Assert.Equal(1, report.ScenesChanged);
        Assert.Equal(["Arrival"], report.ChangedTitles);
        Assert.Equal(before, await SceneTextAsync());
    }

    [Fact]
    public async Task TheRunPutsTheProseRight()
    {
        var report = await _rpc.RunAsync(EveryRule());

        Assert.Equal(1, report.ScenesChanged);
        Assert.Equal("<p>“He left—again…”</p><p>* * *</p><p>Then she did.</p>",
            await SceneTextAsync());
    }

    [Fact]
    public async Task ARuleThisBuildDoesNotKnowIsIgnored()
    {
        // An older renderer naming a rule that has since gone should still
        // clean up the rest rather than failing the whole pass.
        var report = await _rpc.RunAsync(["SmartenQuotes", "TurnItIntoLatin"]);

        Assert.Equal(1, report.ScenesChanged);
        Assert.Contains("“He left", await SceneTextAsync());
    }

    [Fact]
    public async Task NoRulesIsNoPass()
        => Assert.Equal(0, (await _rpc.PreviewAsync([])).ScenesConsidered);

    [Fact]
    public async Task NamingAChapterNarrowsThePass()
    {
        var report = await _rpc.PreviewAsync(EveryRule(), [_chapterGuid]);

        Assert.Equal(1, report.ScenesConsidered);
    }

    [Fact]
    public async Task AChapterWithNothingInItChangesNothing()
    {
        var other = await _workspace.Projects.CreateChapterAsync("Two");

        var report = await _rpc.RunAsync(EveryRule(), [other.Guid]);

        Assert.Equal(0, report.ScenesChanged);
    }

    [Fact]
    public async Task EveryChangedSceneIsSnapshottedFirst()
    {
        await _rpc.RunAsync(EveryRule());

        // The pass rewrites the prose itself, the same as Replace All, and the
        // writer has to be able to get the old version back.
        var snapshots = Directory.EnumerateFiles(
            _workspace.Projects.ProjectRoot!, "*", SearchOption.AllDirectories)
            .Where(p => p.Contains("napshot", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(snapshots);
    }

    [Fact]
    public async Task RunningItTwiceChangesNothingTheSecondTime()
    {
        await _rpc.RunAsync(EveryRule());

        // A cleaned manuscript is already clean; a pass that keeps reporting
        // changes would make the count meaningless.
        Assert.Equal(0, (await _rpc.RunAsync(EveryRule())).ScenesChanged);
    }
}
