using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// ProjectService-level guarantees for the filesystem-source-of-truth model: scene
/// content reads strip front-matter, writes re-stamp it, new scenes are born stamped,
/// mention-sync round-trips the marker, and external edits are reconciled on reload.
/// </summary>
public sealed class FilesystemRoundTripTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _sut = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private async Task<(ChapterData chapter, SceneData scene)> SetupSceneAsync()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _sut.CreateChapterAsync("Intro");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "Opening");
        return (ch, sc);
    }

    [Fact]
    public async Task ReopeningAfterAnEdit_RefreshesOnlyTheIndexAndSkipsSceneReadsNextTime()
    {
        var (chapter, scene) = await SetupSceneAsync();
        var root = _sut.ProjectRoot!;
        await _sut.LoadProjectAsync(root);
        await _sut.WriteSceneContentAsync(chapter, scene, "<p>Edited prose.</p>");
        var path = _sut.GetSceneFilePath(chapter, scene);
        var modified = File.GetLastWriteTimeUtc(path);
        var files = new SceneReadCounter();
        var reader = new ProjectService(files);

        await reader.LoadProjectAsync(root);

        Assert.Equal(1, files.SceneReads);
        Assert.Equal(1, files.IndexWrites);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.Equal(FileFrontMatter.Build(scene.Id) + "<p>Edited prose.</p>", await File.ReadAllTextAsync(path));
        files.SceneReads = files.IndexWrites = 0;

        await reader.LoadProjectAsync(root);

        Assert.Equal(0, files.SceneReads);
        Assert.Equal(0, files.IndexWrites);
        await _sut.WriteSceneContentAsync(chapter, scene, "<p>Edited once more.</p>");
        await reader.LoadProjectAsync(root);
        Assert.Equal(1, files.SceneReads);
        Assert.Equal(1, files.IndexWrites);
    }

    [Fact]
    public async Task RecheckingUnreliableTimestampsDoesNotRewriteAnUnchangedIndex()
    {
        var (chapter, scene) = await SetupSceneAsync();
        var path = _sut.GetSceneFilePath(chapter, scene);
        File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch);
        var files = new SceneReadCounter();
        var reader = new ProjectService(files);
        await reader.LoadProjectAsync(_sut.ProjectRoot!);
        files.SceneReads = files.IndexWrites = 0;

        await reader.ReconcileActiveDraftAsync();

        Assert.Equal(1, files.SceneReads);
        Assert.Equal(0, files.IndexWrites);
        Assert.Equal(scene.Id, Assert.Single(reader.GetScenesForChapter(chapter.Guid)).Id);
    }

    private sealed class SceneReadCounter : FileService, IFileService
    {
        public int SceneReads { get; set; }
        public int IndexWrites { get; set; }

        Task<string> IFileService.ReadTextAsync(string path)
        {
            if (path.EndsWith(".novalist", StringComparison.Ordinal)) SceneReads++;
            return ReadTextAsync(path);
        }

        Task IFileService.WriteTextAsync(string path, string content)
        {
            if (Path.GetFileName(path) == ".nvindex.json") IndexWrites++;
            return WriteTextAsync(path, content);
        }
    }

    [Fact]
    public async Task CreateScene_FileBornStamped()
    {
        var (ch, sc) = await SetupSceneAsync();
        var raw = await File.ReadAllTextAsync(_sut.GetSceneFilePath(ch, sc));
        Assert.True(FileFrontMatter.TryParse(raw, out var p));
        Assert.Equal(sc.Id, p.Id);
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips_AndStampsFile()
    {
        var (ch, sc) = await SetupSceneAsync();
        await _sut.WriteSceneContentAsync(ch, sc, "<p>Hello.</p>");

        var raw = await File.ReadAllTextAsync(_sut.GetSceneFilePath(ch, sc));
        Assert.True(FileFrontMatter.TryParse(raw, out var p));
        Assert.Equal(sc.Id, p.Id);
        Assert.Equal("<p>Hello.</p>", await _sut.ReadSceneContentAsync(ch, sc));   // read strips
    }

    [Fact]
    public async Task WriteSceneContent_EchoedFrontMatter_NotDoubled()
    {
        var (ch, sc) = await SetupSceneAsync();
        await _sut.WriteSceneContentAsync(ch, sc, "<p>v1</p>");
        // Caller echoes back the raw (stamped) content — must not produce two markers.
        var raw = await File.ReadAllTextAsync(_sut.GetSceneFilePath(ch, sc));
        await _sut.WriteSceneContentAsync(ch, sc, raw);

        var again = await File.ReadAllTextAsync(_sut.GetSceneFilePath(ch, sc));
        Assert.Equal("<p>v1</p>", FileFrontMatter.Strip(again));
        Assert.False(FileFrontMatter.HasFrontMatter(FileFrontMatter.Strip(again))); // only one marker
    }

    [Fact]
    public async Task ReadArchivedSceneContent_Strips()
    {
        var (ch, sc) = await SetupSceneAsync();
        await _sut.WriteSceneContentAsync(ch, sc, "<p>archive me</p>");
        await _sut.ArchiveSceneAsync(ch.Guid, sc.Id);

        var archived = _sut.GetArchivedScenes().Single();
        Assert.Equal("<p>archive me</p>", await _sut.ReadArchivedSceneContentAsync(archived));
    }

    [Fact]
    public async Task SyncMentionDisplayText_PreservesFrontMatter()
    {
        var (ch, sc) = await SetupSceneAsync();
        await _sut.WriteSceneContentAsync(ch, sc,
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"e1\">Bob</span></p>");

        var modified = await _sut.SyncMentionDisplayTextAsync("e1", "Robert");

        Assert.Equal(1, modified);
        var raw = await File.ReadAllTextAsync(_sut.GetSceneFilePath(ch, sc));
        Assert.True(FileFrontMatter.HasFrontMatter(raw));      // marker round-tripped
        Assert.Contains("Robert", raw);
    }

    [Fact]
    public async Task ReconcileOnLoad_ExternalNewScene_AppearsStamped()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _sut.CreateChapterAsync("Intro");
        await _sut.CreateSceneAsync(ch.Guid, "First");
        var root = _sut.ProjectRoot!;
        // Drop a brand-new, id-less scene file straight into the chapter folder on disk.
        var newFile = Path.Combine(_sut.GetChapterFolderPath(ch), "scene-50.novalist");
        await File.WriteAllTextAsync(newFile, "<p>external</p>");

        var reloaded = new ProjectService(new FileService());
        await reloaded.LoadProjectAsync(root);

        var scenes = reloaded.GetScenesForChapter(ch.Guid);
        Assert.Equal(2, scenes.Count);
        Assert.True(FileFrontMatter.HasFrontMatter(await File.ReadAllTextAsync(newFile))); // stamped on reconcile
    }

    [Fact]
    public async Task ReconcileOnLoad_ExternalMoveAcrossChapters_Reflected()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch1 = await _sut.CreateChapterAsync("One");
        var ch2 = await _sut.CreateChapterAsync("Two");
        var sc = await _sut.CreateSceneAsync(ch1.Guid, "Wanderer");
        await _sut.WriteSceneContentAsync(ch1, sc, "<p>body</p>");
        var root = _sut.ProjectRoot!;

        // Move the file from ch1's folder to ch2's folder externally.
        var from = _sut.GetSceneFilePath(ch1, sc);
        var to = Path.Combine(_sut.GetChapterFolderPath(ch2), sc.FileName);
        File.Move(from, to);

        var reloaded = new ProjectService(new FileService());
        await reloaded.LoadProjectAsync(root);

        Assert.Empty(reloaded.GetScenesForChapter(ch1.Guid));
        Assert.Single(reloaded.GetScenesForChapter(ch2.Guid), s => s.Id == sc.Id);
    }

    [Fact]
    public async Task ReconcileActiveDraft_FiresDraftReconciled_OnExternalChange()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _sut.CreateChapterAsync("Intro");
        await _sut.CreateSceneAsync(ch.Guid, "First");
        await File.WriteAllTextAsync(Path.Combine(_sut.GetChapterFolderPath(ch), "scene-44.novalist"), "<p>x</p>");

        ReconciliationReport? fired = null;
        _sut.DraftReconciled += (_, r) => fired = r;
        await _sut.ReconcileActiveDraftAsync();

        Assert.NotNull(fired);
        Assert.True(fired!.HasChanges);
    }

    [Fact]
    public async Task ReconcileActiveDraft_NoChanges_DoesNotFireEvent()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        await _sut.CreateChapterAsync("Intro");
        var fired = false;
        _sut.DraftReconciled += (_, _) => fired = true;

        await _sut.ReconcileActiveDraftAsync();

        Assert.False(fired);
    }

    [Fact]
    public void WatchFilesystem_DefaultsTrue()
    {
        Assert.True(new ProjectSettings().WatchFilesystem);
    }

    [Fact]
    public async Task ReconcileActiveDraft_NoProjectLoaded_EmptyReport()
    {
        var svc = new ProjectService(new FileService());
        var report = await svc.ReconcileActiveDraftAsync();
        Assert.False(report.HasChanges);
    }

    [Fact]
    public async Task ReconcileActiveDraft_ApplyFalse_ReportsButDoesNotMutate()
    {
        await _sut.CreateProjectAsync(_dir.Path, "P", "Book");
        var ch = await _sut.CreateChapterAsync("Intro");
        await _sut.CreateSceneAsync(ch.Guid, "First");
        await File.WriteAllTextAsync(Path.Combine(_sut.GetChapterFolderPath(ch), "scene-77.novalist"), "<p>x</p>");

        var report = await _sut.ReconcileActiveDraftAsync(apply: false);

        Assert.True(report.HasChanges);
        Assert.Contains(report.Scenes, s => s.Kind == SceneChangeKind.New);
        Assert.Single(_sut.GetScenesForChapter(ch.Guid)); // manifest untouched
    }
}
