using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class RecentActivityServiceTests
{
    private static ActivityItem Item(string sceneId, ActivityType type = ActivityType.Edit, DateTime? ts = null)
        => new() { SceneId = sceneId, Type = type, Timestamp = ts ?? DateTime.UtcNow };

    [Fact]
    public async Task LoadAsync_MissingFile_StartsEmpty_AndRaisesChanged()
    {
        using var dir = new TempDir();
        var sut = new RecentActivityService();
        var changed = false;
        sut.Changed += () => changed = true;

        await sut.LoadAsync(dir.Path);

        Assert.Empty(sut.Recent);
        Assert.True(changed);
        Assert.True(Directory.Exists(Path.Combine(dir.Path, ".novalist")));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_StartsEmpty()
    {
        using var dir = new TempDir();
        var nv = Path.Combine(dir.Path, ".novalist");
        Directory.CreateDirectory(nv);
        await File.WriteAllTextAsync(Path.Combine(nv, "activity.json"), "{ not valid json");

        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path);

        Assert.Empty(sut.Recent);
    }

    [Fact]
    public async Task LogAsync_BeforeLoad_NoOp()
    {
        var sut = new RecentActivityService();
        await sut.LogAsync(Item("s1"));
        Assert.Empty(sut.Recent);
    }

    [Fact]
    public async Task LogAsync_InsertsNewestFirst_AndPersists()
    {
        using var dir = new TempDir();
        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path);

        await sut.LogAsync(Item("s1", ts: DateTime.UtcNow.AddMinutes(-5)));
        await sut.LogAsync(Item("s2"));

        Assert.Equal("s2", sut.Recent[0].SceneId);
        // Reload to confirm persistence.
        var reloaded = new RecentActivityService();
        await reloaded.LoadAsync(dir.Path);
        Assert.Equal(2, reloaded.Recent.Count);
    }

    [Fact]
    public async Task LogAsync_DeduplicatesConsecutiveSameSceneWithin60s()
    {
        using var dir = new TempDir();
        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path);

        var t0 = DateTime.UtcNow;
        await sut.LogAsync(Item("s1", ts: t0));
        await sut.LogAsync(Item("s1", ts: t0.AddSeconds(10)));

        Assert.Single(sut.Recent);
        Assert.Equal(t0.AddSeconds(10), sut.Recent[0].Timestamp);
    }

    [Fact]
    public async Task LogAsync_DoesNotDeduplicate_WhenDifferentType()
    {
        using var dir = new TempDir();
        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path);

        var t0 = DateTime.UtcNow;
        await sut.LogAsync(Item("s1", ActivityType.Edit, t0));
        await sut.LogAsync(Item("s1", ActivityType.Delete, t0.AddSeconds(1)));

        Assert.Equal(2, sut.Recent.Count);
    }

    [Fact]
    public async Task LogAsync_TrimsToMaxEntries()
    {
        using var dir = new TempDir();
        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path);

        // 101 distinct scenes spaced > 60s apart so none dedupe.
        for (int i = 0; i < 101; i++)
            await sut.LogAsync(Item($"s{i}", ts: DateTime.UtcNow.AddMinutes(i * 2)));

        Assert.Equal(100, sut.Recent.Count);
    }

    [Fact]
    public async Task LogAsync_PersistFailure_IsSwallowed()
    {
        using var dir = new TempDir();
        var nv = Path.Combine(dir.Path, ".novalist");
        Directory.CreateDirectory(nv);
        // Make activity.json a directory so WriteAllText throws inside PersistAsync.
        Directory.CreateDirectory(Path.Combine(nv, "activity.json"));

        var sut = new RecentActivityService();
        await sut.LoadAsync(dir.Path); // File.Exists is false for a directory -> loads empty

        // Should not throw despite the persist failure.
        await sut.LogAsync(Item("s1"));
        Assert.Single(sut.Recent);
    }
}
