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
    public async Task SavesThatOverlapDoNotCollideOnTheFile()
    {
        // Two edits in quick succession - tabbing between two fields of one
        // form - used to reach the file at the same moment. Windows refuses the
        // second write outright, and the edit it carried was gone with nothing
        // to show the writer that anything had failed.
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => sut.SaveAsync()));

        var reloaded = new SettingsService(dir.Path);
        await reloaded.LoadAsync();
        Assert.NotNull(reloaded.Settings);
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

    // The recents list showed the same project twice, because the two spellings
    // Windows produced for it did not compare equal.
    [Theory]
    [InlineData("d:/git/book", @"D:\git\book")]        // separator and case
    [InlineData(@"D:\git\book", @"D:\git\book\")]      // trailing separator
    [InlineData(@"D:\git\book", @"\D:\git\book")]      // separator before the drive
    [InlineData(@"D:\git\book", "  D:\\git\\book  ")]  // surrounding whitespace
    public void AddRecentProject_TreatsTheSameFolderAsOneEntry(string first, string second)
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("A", first);
        sut.AddRecentProject("A again", second);

        var only = Assert.Single(sut.Settings.RecentProjects);
        Assert.Equal("A again", only.Name);
        Assert.Equal(second, only.Path);   // the spelling we were last given is kept
    }

    [Fact]
    public void RemoveRecentProject_MatchesADifferentSpellingOfTheSameFolder()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("A", "d:/git/book");
        sut.RemoveRecentProject(@"D:\git\book\");
        Assert.Empty(sut.Settings.RecentProjects);
    }

    [Fact]
    public void AddRecentProject_KeepsAPathItCannotResolve()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("A", "not\0a path");
        sut.AddRecentProject("B", "   ");

        Assert.Equal(2, sut.Settings.RecentProjects.Count);
        Assert.Contains(sut.Settings.RecentProjects, r => r.Path == "not\0a path");
    }

    // ── following a project the platform has moved ──

    private static string P(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void RelocateRecentProject_FollowsThePathAndTheCover_WithoutReordering()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        var oldRoot = P("/old", "container", "The Chart");
        var newRoot = P("/new", "container", "The Chart");
        sut.AddRecentProject("Older", "/elsewhere");
        sut.AddRecentProject("The Chart", oldRoot, P(oldRoot, "Images", "cover.png"));

        sut.RelocateRecentProject(oldRoot, newRoot);

        // Still the newest entry, still one entry - a move is not a re-open.
        Assert.Equal(2, sut.Settings.RecentProjects.Count);
        var moved = sut.Settings.RecentProjects[0];
        Assert.Equal(newRoot, moved.Path);
        Assert.Equal(P(newRoot, "Images", "cover.png"), moved.CoverImagePath);
    }

    [Fact]
    public void RelocateRecentProject_LeavesACoverThatWasNeverInsideTheProject()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        var outside = P("/pictures", "cover.png");
        sut.AddRecentProject("The Chart", P("/old", "The Chart"), outside);

        sut.RelocateRecentProject(P("/old", "The Chart"), P("/new", "The Chart"));

        Assert.Equal(outside, sut.Settings.RecentProjects[0].CoverImagePath);
    }

    [Fact]
    public void RelocateRecentProject_AbsorbsARowThatAlreadyNamedTheDestination()
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        var newRoot = P("/new", "The Chart");
        sut.AddRecentProject("The Chart (stale)", newRoot);
        sut.AddRecentProject("The Chart", P("/old", "The Chart"));

        sut.RelocateRecentProject(P("/old", "The Chart"), newRoot);

        var only = Assert.Single(sut.Settings.RecentProjects);
        Assert.Equal("The Chart", only.Name);
        Assert.Equal(newRoot, only.Path);
    }

    [Theory]
    [InlineData("/nobody/has/this", "/new")]   // nothing to move
    [InlineData("/old", "")]                   // nowhere to move it to
    [InlineData("", "/new")]                   // no idea what to move
    public void RelocateRecentProject_DoesNothingItCannotDo(string from, string to)
    {
        using var dir = new TempDir();
        var sut = new SettingsService(dir.Path);
        sut.AddRecentProject("The Chart", "/old");

        sut.RelocateRecentProject(from, to);

        Assert.Equal("/old", Assert.Single(sut.Settings.RecentProjects).Path);
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
