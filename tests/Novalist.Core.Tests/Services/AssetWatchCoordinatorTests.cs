using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Watching the Themes, Locales and Analysis folders.
///
/// All three were read once at startup and a restart was needed after any
/// change, so iterating on a theme was a relaunch per edit - the wrong loop for
/// something anybody tunes by eye. The decisions live here, away from the
/// native watcher, so they can be tested without real OS events or timers.
/// </summary>
public class AssetWatchCoordinatorTests
{
    private sealed class Harness
    {
        public List<IReadOnlyCollection<UserAssetKind>> Reloads { get; } = [];
        public int FlushesScheduled { get; private set; }
        public Exception? Throw { get; set; }

        public AssetWatchCoordinator Coordinator { get; }

        public Harness()
        {
            Coordinator = new AssetWatchCoordinator(
                kinds =>
                {
                    Reloads.Add(kinds);
                    return Throw != null ? Task.FromException(Throw) : Task.CompletedTask;
                },
                () => FlushesScheduled++);
        }
    }

    [Theory]
    [InlineData("nord.json", true)]
    [InlineData("crimson.css", true)]
    [InlineData("THEME.JSON", true)]
    [InlineData("notes.txt", false)]
    [InlineData("screenshot.png", false)]
    [InlineData("theme.json.swp", false)]
    [InlineData("nofileextension", false)]
    public void OnlyFilesALoaderWouldReadCount(string name, bool expected)
        => Assert.Equal(expected, AssetWatchCoordinator.IsRelevant(name));

    [Fact]
    public async Task AChangeReloadsTheFolderItLandedIn()
    {
        var harness = new Harness();

        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "nord.json");
        await harness.Coordinator.FlushAsync();

        Assert.Equal([UserAssetKind.Themes], Assert.Single(harness.Reloads));
        Assert.Equal(1, harness.FlushesScheduled);
    }

    [Fact]
    public async Task AnIrrelevantFileCostsNothing()
    {
        var harness = new Harness();

        // An editor's swap file, a note, a screenshot dropped in the folder.
        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "notes.txt");
        await harness.Coordinator.FlushAsync();

        Assert.Empty(harness.Reloads);
        Assert.Equal(0, harness.FlushesScheduled);
    }

    [Fact]
    public async Task ABurstCoalescesIntoOneReload()
    {
        var harness = new Harness();

        // Saving one file produces several events on Windows.
        for (var i = 0; i < 5; i++)
            harness.Coordinator.NotifyChange(UserAssetKind.Locales, "de.json");
        await harness.Coordinator.FlushAsync();

        Assert.Equal([UserAssetKind.Locales], Assert.Single(harness.Reloads));
        // The window is re-armed on every event; the flush is what coalesces.
        Assert.Equal(5, harness.FlushesScheduled);
    }

    [Fact]
    public async Task TwoFoldersTouchedInOneWindowReloadTogether()
    {
        var harness = new Harness();

        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "nord.json");
        harness.Coordinator.NotifyChange(UserAssetKind.Analysis, "en.json");
        await harness.Coordinator.FlushAsync();

        // Both, in one call. The order is the set's and says nothing.
        var reloaded = Assert.Single(harness.Reloads);
        Assert.Equal(2, reloaded.Count);
        Assert.Contains(UserAssetKind.Themes, reloaded);
        Assert.Contains(UserAssetKind.Analysis, reloaded);
    }

    [Fact]
    public async Task FlushingWithNothingPendingDoesNothing()
    {
        var harness = new Harness();

        await harness.Coordinator.FlushAsync();

        Assert.Empty(harness.Reloads);
    }

    [Fact]
    public async Task TheSecondFlushAfterOneChangeIsQuiet()
    {
        var harness = new Harness();
        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "nord.json");

        await harness.Coordinator.FlushAsync();
        await harness.Coordinator.FlushAsync();

        // The debounce timer can fire again after the burst has been handled.
        Assert.Single(harness.Reloads);
    }

    [Fact]
    public async Task AFailedReloadDoesNotEndTheWatching()
    {
        var harness = new Harness { Throw = new IOException("half-written file") };
        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "nord.json");
        await harness.Coordinator.FlushAsync();

        // A theme somebody is midway through editing is unreadable for a
        // moment, and that must not stop the next save from being noticed.
        harness.Throw = null;
        harness.Coordinator.NotifyChange(UserAssetKind.Themes, "nord.json");
        await harness.Coordinator.FlushAsync();

        Assert.Equal(2, harness.Reloads.Count);
    }
}
