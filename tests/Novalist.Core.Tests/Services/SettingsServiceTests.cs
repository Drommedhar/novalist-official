using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_MissingFile_UsesDefaults()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        await sut.LoadAsync();
        Assert.NotNull(sut.Settings);
        Assert.NotEmpty(sut.Settings.AutoReplacements); // EnsureDefaults populated them
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.Settings.Theme = "midnight";
        await sut.SaveAsync();

        var reloaded = new SettingsService(dir.Path);
        await reloaded.LoadAsync();
        Assert.Equal("midnight", reloaded.Settings.Theme);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_FallsBackToDefaults()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "settings.json"), "null");
        var sut = new SettingsService(dir.Path);
        await sut.LoadAsync();
        Assert.NotNull(sut.Settings);
    }

    [Fact]
    public void AddRecentProject_InsertsNewestFirst_AndDeduplicates()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("A", "/a");
        sut.AddRecentProject("B", "/b");
        sut.AddRecentProject("A2", "/a"); // same path -> dedupe + move to front

        Assert.Equal(2, sut.Settings.RecentProjects.Count);
        Assert.Equal("/a", sut.Settings.RecentProjects[0].Path);
        Assert.Equal("A2", sut.Settings.RecentProjects[0].Name);
    }

    [Fact]
    public void AddRecentProject_TrimsToTen()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        for (int i = 0; i < 12; i++)
            sut.AddRecentProject($"P{i}", $"/p{i}");
        Assert.Equal(10, sut.Settings.RecentProjects.Count);
    }

    [Fact]
    public void RemoveRecentProject_RemovesByPathCaseInsensitive()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("A", "/a");
        sut.RemoveRecentProject("/A");
        Assert.Empty(sut.Settings.RecentProjects);
    }

    [Fact]
    public void Effective_ReflectsActiveOverrides()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.Settings.Theme = "global";
        Assert.Equal("global", sut.Effective.Theme);

        sut.SetActiveOverrides(new SettingsOverrides { Theme = "override" });
        Assert.Equal("override", sut.Effective.Theme);

        sut.SetActiveOverrides(null);
        Assert.Equal("global", sut.Effective.Theme);
    }
}
