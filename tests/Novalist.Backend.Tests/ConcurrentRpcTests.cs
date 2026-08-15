using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Two requests at once must not tear the workspace they share.
///
/// The backend serves JSON-RPC concurrently and hands every one of its seventy
/// facades the same <see cref="Workspace"/>, so any two overlapping requests
/// run against one set of collections. The word history was rebuilt in place on
/// every dashboard read while another read was part-way through counting it,
/// and the second died with "Collection was modified; enumeration operation may
/// not execute".
///
/// It reached a writer as a Dashboard that simply refused to load: open the
/// Dashboard on a book big enough for the read to take a moment, step into
/// Settings, come back, and the second read overlapped the first.
/// </summary>
public sealed class ConcurrentRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public ConcurrentRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-conc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "Concurrent", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task OverlappingDashboardReads_DoNotTearTheWordHistory()
    {
        // Enough of a book that a read takes long enough to be overlapped.
        for (var c = 0; c < 8; c++)
        {
            var chapter = await _workspace.Projects.CreateChapterAsync("Chapter " + c);
            for (var s = 0; s < 4; s++)
            {
                var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene " + s);
                var prose = string.Join(' ', Enumerable.Repeat("wort", 120));
                await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>" + prose + "</p>", prose);
            }
        }

        var dashboard = new DashboardRpc(_workspace);
        var settings = new SettingsRpc(_workspace);
        var project = new ProjectRpc(_workspace);

        // What the interface does on the way back to the Dashboard: its own
        // read starts while the one from before is still finishing, with the
        // settings the writer just left still being read.
        for (var round = 0; round < 8; round++)
        {
            await Task.WhenAll(
                Task.Run(async () => await dashboard.GetAsync(30)),
                Task.Run(async () => await settings.GetAsync()),
                Task.Run(() => project.GetState()),
                Task.Run(async () => await dashboard.GetAsync(90)));
        }

        // And the figures are still right afterwards, so the fix is a snapshot
        // rather than a swallowed exception.
        var dto = await dashboard.GetAsync(30);
        Assert.Equal(32, dto.SceneCount);
        Assert.True(dto.TotalWords > 0);
    }

    [Fact]
    public async Task ASaveDuringADashboardRead_DoesNotTearTheWordHistory()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter");
        var scenes = new List<string>();
        for (var s = 0; s < 12; s++)
        {
            var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene " + s);
            scenes.Add(scene.Id);
            var prose = string.Join(' ', Enumerable.Repeat("wort", 80));
            await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>" + prose + "</p>", prose);
        }

        var dashboard = new DashboardRpc(_workspace);

        // Typing does not stop because a screen is counting: a save appends to
        // the same history the read is walking.
        for (var round = 0; round < 8; round++)
        {
            var reads = Task.WhenAll(
                Task.Run(async () => await dashboard.GetAsync(30)),
                Task.Run(async () => await dashboard.GetAsync(365)));
            var writes = Task.WhenAll(scenes.Take(4).Select(id => Task.Run(async () =>
                await _workspace.WordHistory.RecordSaveAsync(
                    _workspace.Projects.ActiveBook!.Id, id, 100 + round))));
            await Task.WhenAll(reads, writes);
        }

        Assert.True((await dashboard.GetAsync(30)).TotalWords > 0);
    }
}
