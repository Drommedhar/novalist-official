using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class BackupRestoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _project;
    private readonly AppSettings _settings = new() { BackupEnabled = true };
    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();
    private readonly BackupService _sut;

    public BackupRestoreTests()
    {
        _project = new ProjectService(_files);
        _settingsService.Settings.Returns(_settings);
        _sut = Service(_project);
    }

    private BackupService Service(IProjectService project) =>
        new(project, _files, new ArchiveService(), _settingsService, _dir.Combine("backups"));

    public void Dispose() => _dir.Dispose();

    private async Task<BackupInfo> CreateBackupAsync()
    {
        await _project.CreateProjectAsync(_dir.Path, "Original", "Book");
        var chapter = await _project.CreateChapterAsync("Chapter");
        var scene = await _project.CreateSceneAsync(chapter.Guid, "Scene");
        await _project.WriteSceneContentAsync(chapter, scene, "<p>Archived writing.</p>");
        return (await _sut.CreateAsync("manual"))!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreAsNewProject_CopiesTheArchiveWithANewIdentityAndKeepsTheOriginal(bool noProjectOpen)
    {
        var archive = await CreateBackupAsync();
        var originalId = _project.CurrentProject!.Id;
        var originalRoot = _project.ProjectRoot!;
        var originalFiles = Directory.GetFiles(originalRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var service = noProjectOpen ? Service(new ProjectService(_files)) : _sut;

        var destination = await service.RestoreAsNewProjectAsync(archive.Path, _dir.Path, "  Restored copy  ");

        Assert.Equal(_dir.Combine("Restored copy"), destination);
        var restored = new ProjectService(_files);
        await restored.LoadProjectAsync(destination);
        Assert.Equal("Restored copy", restored.CurrentProject!.Name);
        Assert.StartsWith("project-", restored.CurrentProject.Id);
        Assert.NotEqual(originalId, restored.CurrentProject.Id);
        var chapter = Assert.Single(restored.GetChaptersOrdered());
        var scene = Assert.Single(restored.GetScenesForChapter(chapter.Guid));
        Assert.Contains("Archived writing.", await restored.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(originalFiles.Count, Directory.GetFiles(originalRoot, "*", SearchOption.AllDirectories).Length);
        foreach (var (path, bytes) in originalFiles) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("name.")]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("invalid:name")]
    public async Task RestoreAsNewProject_RejectsInvalidNamesBeforeExtracting(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.RestoreAsNewProjectAsync(_dir.Combine("unread.zip"), _dir.Path, name));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreAsNewProject_RequiresAnExistingParentFolder(bool blank)
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _sut.RestoreAsNewProjectAsync(_dir.Combine("unread.zip"), blank ? " " : _dir.Combine("missing"), "Copy"));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreAsNewProject_RefusesAnExistingFileOrDirectory(bool directory)
    {
        var destination = _dir.Combine("Copy");
        if (directory) Directory.CreateDirectory(destination);
        var original = directory ? Path.Combine(destination, "keep.txt") : destination;
        File.WriteAllText(original, "Keep this");

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.RestoreAsNewProjectAsync(_dir.Combine("unread.zip"), _dir.Path, "Copy"));

        Assert.Equal("Keep this", File.ReadAllText(original));
    }

    [Fact]
    public async Task RestoreAsNewProject_RefusesACopyInsideTheOpenProject()
    {
        var archive = await CreateBackupAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RestoreAsNewProjectAsync(archive.Path, _project.ProjectRoot!, "Nested copy"));

        Assert.False(Directory.Exists(Path.Combine(_project.ProjectRoot!, "Nested copy")));
        Assert.Equal("Original", _project.CurrentProject!.Name);
    }

    [Fact]
    public async Task CreateBackup_RefusesToArchiveTheProjectIntoItself()
    {
        await _project.CreateProjectAsync(_dir.Path, "Original", "Book");
        _settings.BackupFolder = _project.ProjectRoot!;
        var destination = _sut.GetBackupFolder()!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync("manual"));

        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFiles(_project.ProjectRoot!, "*.zip", SearchOption.AllDirectories));
    }
}
