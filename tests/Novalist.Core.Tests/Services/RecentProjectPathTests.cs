using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class RecentProjectPathTests
{
    [Theory]
    [InlineData(@"\D:\Novels\Book", @"D:\Novels\Book")]
    [InlineData(@"D:\Novels\Book", @"D:\Novels\Book")]
    [InlineData(@"\\server\Book", @"\\server\Book")]
    [InlineData(@"\1:Book", @"\1:Book")]
    [InlineData(@"\D", @"\D")]
    public void WindowsDriveRepairPreservesOtherPrefixes(string path, string expected)
    {
        Assert.Equal(expected, RecentProjectPath.RemoveLeadingDriveSeparator(path, '\\'));
    }

    [Fact]
    public void RootRemainsDistinctFromAnEmptyPath()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."))!;
        Assert.NotEmpty(RecentProjectPath.Normalize(root));
        Assert.Equal(string.Empty, RecentProjectPath.Normalize(" "));
    }

    [Fact]
    public void UnixDriveLikeFolderKeepsItsLeadingSeparator()
    {
        if (OperatingSystem.IsWindows()) return;

        var path = Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "D:", "Book");
        Assert.Equal(path, RecentProjectPath.Normalize(path), ignoreCase: OperatingSystem.IsMacOS());
    }

    [Fact]
    public void MissingProjectUsesExistingAncestors()
    {
        var ancestor = Path.GetFullPath("Projects");
        var parent = Path.Combine(ancestor, "Missing");
        var project = Path.Combine(parent, "Book");
        var visited = new List<string>();

        Assert.True(RecentProjectPath.IsCaseInsensitive(project, path =>
        {
            visited.Add(path);
            return path == project || path == parent ? (-1L, 2) : (0L, 0);
        }));
        Assert.Equal(new[] { project, parent, ancestor }, visited.Take(3));
        Assert.Equal(Path.GetPathRoot(ancestor), visited[^1]);
    }

    [Theory]
    [InlineData(1L, 0)]
    [InlineData(-1L, 13)]
    public void InsensitiveChildDoesNotFoldSensitiveOrUnknownAncestor(long value, int error)
    {
        var ancestor = Path.GetFullPath("SensitiveMount");
        var child = Path.Combine(ancestor, "InsensitiveMount", "Book");

        Assert.False(RecentProjectPath.IsCaseInsensitive(child,
            path => path == ancestor ? (value, error) : (0L, 0)));
    }

    [Theory]
    [InlineData(1L, 0)]
    [InlineData(2L, 0)]
    [InlineData(-1L, 13)]
    [InlineData(-1L, 0)]
    public void SensitiveOrUnknownVolumeDoesNotFoldCase(long value, int error)
    {
        var calls = 0;
        Assert.False(RecentProjectPath.IsCaseInsensitive(Path.GetFullPath("Projects"), _ =>
        {
            calls++;
            return (value, error);
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MissingMountDoesNotInheritRootVolumePolicy()
    {
        var visited = new List<string>();
        Assert.False(RecentProjectPath.IsCaseInsensitive("/Volumes/Offline/Book", path =>
        {
            visited.Add(path);
            return (-1L, 2);
        }, path => path[..path.LastIndexOf('/')]));
        Assert.Equal(new[] { "/Volumes/Offline/Book", "/Volumes/Offline" }, visited);
    }

    [Fact]
    public void MissingRootStopsWithoutLooping()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."))!;
        Assert.False(RecentProjectPath.IsCaseInsensitive(root, _ => (-1L, 2)));
    }

    [Fact]
    public void UnexpectedProbeErrorPropagates()
    {
        Assert.Throws<InvalidOperationException>(() => RecentProjectPath.IsCaseInsensitive(
            Path.GetFullPath("Projects"), _ => throw new InvalidOperationException("probe failed")));
    }

    [Fact]
    public void ExistingProjectUsesActualVolumeCasePolicy()
    {
        using var directory = new TempDir();
        var lower = Path.Combine(directory.Path, "book");
        var upper = Path.Combine(directory.Path, "BOOK");
        Directory.CreateDirectory(lower);

        Assert.Equal(Directory.Exists(upper), RecentProjectPath.Normalize(lower) == RecentProjectPath.Normalize(upper));
    }

    [Fact]
    public void InvalidPathIsPreservedWithoutNativeLookup()
    {
        Assert.Equal("not\0a path", RecentProjectPath.Normalize("not\0a path"));
        Assert.Equal(OperatingSystem.IsWindows(), RecentProjectPath.IsCaseInsensitive("not\0a path"));
    }

    [Fact]
    public void RelocationUsesVolumeCasePolicyForCoverPath()
    {
        using var directory = new TempDir();
        var oldRoot = Path.Combine(directory.Path, "book");
        var upperRoot = Path.Combine(directory.Path, "BOOK");
        var newRoot = Path.Combine(directory.Path, "moved");
        Directory.CreateDirectory(oldRoot);
        var cover = Path.Combine(upperRoot, "Images", "cover.png");
        var settings = new SettingsService(directory.Path);
        settings.AddRecentProject("Book", oldRoot, cover);

        settings.RelocateRecentProject(oldRoot, newRoot);

        Assert.Equal(Directory.Exists(upperRoot) ? Path.Combine(newRoot, "Images", "cover.png") : cover,
            Assert.Single(settings.Settings.RecentProjects).CoverImagePath);
    }
}
