using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Read-only reconciler: scans the on-disk draft tree and diffs it against the cached
/// manifest. Tests run against a real migrated (v3) project on a temp directory, then
/// mutate the disk the way an external file manager would.
/// </summary>
public sealed class ProjectReconcilerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _fs = new();

    public void Dispose() => _dir.Dispose();

    private string DraftRoot => Path.Combine(_dir.Path, "B", "Drafts", "default");
    private string Chapters => Path.Combine(DraftRoot, "Chapters");

    private const string Ch1 = "ch-01 - A";
    private const string Ch2 = "ch-02 - B";

    /// <summary>Builds + migrates a small v3 project (2 chapters, 3 scenes, 1 archived).</summary>
    private async Task<(List<ChapterData> chapters, ScenesManifest manifest, DraftIndex index)> SetupAsync()
    {
        var b = new V2ProjectBuilder();
        var book = b.AddBook("B", "B");
        var draft = b.AddDraft(book, "default");
        var ch1 = b.AddChapter(book, draft, "A", "01 - A", 1);
        b.AddScene(book, draft, ch1, "scene-01.novalist", "<p>One.</p>");
        b.AddScene(book, draft, ch1, "scene-02.novalist", "<p>Two.</p>");
        var ch2 = b.AddChapter(book, draft, "B", "02 - B", 2);
        b.AddScene(book, draft, ch2, "scene-01.novalist", "<p>Three.</p>");
        b.AddArchivedScene(book, draft, "scene-07.novalist", "<p>Archived.</p>");
        await b.WriteToAsync(_fs, _dir.Path);
        await new FilesystemMigrator(_fs).MigrateAsync(b.Project, _dir.Path);
        return await LoadInputsAsync();
    }

    private async Task<(List<ChapterData>, ScenesManifest, DraftIndex)> LoadInputsAsync()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var draftData = JsonSerializer.Deserialize<BookDraftData>(await File.ReadAllTextAsync(Path.Combine(DraftRoot, "draft.json")), opts)!;
        var manifest = JsonSerializer.Deserialize<ScenesManifest>(await File.ReadAllTextAsync(Path.Combine(DraftRoot, "scenes.json")), opts)!;
        var index = JsonSerializer.Deserialize<DraftIndex>(await File.ReadAllTextAsync(Path.Combine(DraftRoot, ".nvindex.json")), opts)!;
        return (draftData.Chapters, manifest, index);
    }

    private Task<ReconciliationReport> ScanAsync(IReadOnlyList<ChapterData> chapters, ScenesManifest manifest, DraftIndex index)
        => new ProjectReconciler(_fs).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

    private Task ApplyAsync(List<ChapterData> chapters, ScenesManifest manifest, DraftIndex index)
        => new ProjectReconciler(_fs).ApplyAsync(DraftRoot, "Chapters", chapters, manifest, index);

    private static SceneData? Find(ScenesManifest m, string id)
        => m.Chapters.Values.SelectMany(v => v).FirstOrDefault(s => s.Id == id);

    private string ScenePath(string folder, string file) => Path.Combine(Chapters, folder, file);

    // ── Stability ──

    [Fact]
    public async Task FreshlyMigrated_NoChanges()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var report = await ScanAsync(chapters, manifest, index);
        Assert.False(report.HasChanges);
    }

    [Fact]
    public async Task UnchangedIndexedScenes_DoNotReadSceneBodies()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var files = ReadProbe();

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        Assert.False(report.HasChanges);
        await files.DidNotReceive().ReadTextAsync(Arg.Is<string>(path => path.EndsWith(".novalist")));
    }

    [Fact]
    public async Task BatchCapableProvider_ReadsOnlyChangedAndNewScenesAfterCapturingMetadata()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var changedPath = ScenePath("01 - A", "scene-01.novalist");
        var newPath = ScenePath("01 - A", "scene-05.novalist");
        await File.AppendAllTextAsync(changedPath, "<p>Updated.</p>");
        await File.WriteAllTextAsync(newPath, FileFrontMatter.Build("new-scene") + "<p>New.</p>");
        var files = ReadProbe(batch: true);
        var batch = (IBatchTextReader)files;
        batch.ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var paths = call.ArgAt<IReadOnlyList<string>>(0);
            Assert.Equal(new[] { changedPath, newPath }, paths);
            var contents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in paths.Reverse())
            {
                await files.Received().GetFileSizeAsync(path);
                await files.Received().GetLastWriteTimeUtcAsync(path);
                contents[path] = await File.ReadAllTextAsync(path);
            }
            return (IReadOnlyDictionary<string, string>)contents;
        });

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        Assert.Equal("new-scene", Assert.Single(report.Scenes).Id);
        await batch.Received(1).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await files.DidNotReceive().ReadTextAsync(Arg.Is<string>(path => path.EndsWith(".novalist")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchCapableProvider_ReadsChapterMarkersTogetherAndPreservesMissingMarkerFallback(bool renamed)
    {
        var (chapters, manifest, index) = await SetupAsync();
        if (renamed) Directory.Move(Path.Combine(Chapters, "01 - A"), Path.Combine(Chapters, "01 - Renamed"));
        File.Delete(Path.Combine(Chapters, "02 - B", ChapterMarker.FileName));
        var files = ReadProbe(batch: true);

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        if (renamed)
        {
            var change = Assert.Single(report.Chapters);
            Assert.Equal(ChapterChangeKind.Renamed, change.Kind);
            Assert.Equal(Ch1, change.Guid);
        }
        else Assert.False(report.HasChanges);
        Assert.Empty(report.Scenes);
        var markerPaths = new[]
        {
            Path.Combine(Chapters, renamed ? "01 - Renamed" : "01 - A", ChapterMarker.FileName),
            Path.Combine(Chapters, "02 - B", ChapterMarker.FileName),
        };
        await ((IBatchTextReader)files).Received(1).ReadExistingTextsAsync(
            Arg.Is<IReadOnlyList<string>>(paths => paths.SequenceEqual(markerPaths)), Arg.Any<CancellationToken>());
        await files.DidNotReceive().ReadTextAsync(Arg.Is<string>(path => path.EndsWith(ChapterMarker.FileName)));
        await files.DidNotReceive().ExistsAsync(Arg.Is<string>(path => path.EndsWith(ChapterMarker.FileName)));
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("io")]
    [InlineData("permission")]
    public async Task BatchedChapterMarkerFailures_Propagate(string failure)
    {
        var (chapters, manifest, index) = await SetupAsync();
        var files = ReadProbe(batch: true);
        var batch = (IBatchTextReader)files;
        Exception expected = failure switch
        {
            "io" => new IOException("Provider read failed"),
            "permission" => new UnauthorizedAccessException("Provider permission denied"),
            _ => new JsonException(),
        };
        if (failure == "invalid-json")
            await File.WriteAllTextAsync(Path.Combine(Chapters, "01 - A", ChapterMarker.FileName), "{invalid");
        else
            batch.ReadExistingTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyDictionary<string, string>>(expected));

        var actual = await Record.ExceptionAsync(() => new ProjectReconciler(files)
            .ScanAsync(DraftRoot, "Chapters", chapters, manifest, index));

        Assert.IsType(expected.GetType(), actual);
        await batch.Received(1).ReadExistingTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("size")]
    [InlineData("time")]
    public async Task ChangedSceneMetadata_ReadsOnlyTheChangedScene(string change)
    {
        var (chapters, manifest, index) = await SetupAsync();
        var path = ScenePath("01 - A", "scene-01.novalist");
        var entry = index.Entries["Chapters/01 - A/scene-01.novalist"];
        var content = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, change == "size" ? content + "<p>Added.</p>" : content.Replace("One", "New"));
        File.SetLastWriteTimeUtc(path, change == "size" ? entry.MtimeUtc : entry.MtimeUtc.AddMinutes(1));
        var files = ReadProbe();
        var originalIndex = JsonSerializer.Serialize(index);

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        Assert.False(report.HasChanges);
        Assert.Equal(originalIndex, JsonSerializer.Serialize(index));
        await files.Received(1).ReadTextAsync(path);
        await files.Received(1).ReadTextAsync(Arg.Is<string>(file => file.EndsWith(".novalist")));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("entries")]
    [InlineData("entry")]
    [InlineData("id")]
    [InlineData("path")]
    [InlineData("chapter")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("time")]
    [InlineData("epoch")]
    public async Task UnusableIndex_ReadsSceneIdentityFromDisk(string invalid)
    {
        var (chapters, manifest, index) = await SetupAsync();
        var entry = index.Entries["Chapters/01 - A/scene-01.novalist"];
        switch (invalid)
        {
            case "version": index.Version = 2; break;
            case "entries": index.Entries = null!; break;
            case "entry": index.Entries["Chapters/01 - A/scene-01.novalist"] = null!; break;
            case "id": entry.Id = "stale-id"; break;
            case "path": manifest.Chapters[Ch1][0].FileName = "old-name.novalist"; break;
            case "chapter": manifest.Chapters[Ch2].Add(manifest.Chapters[Ch1][0]); manifest.Chapters[Ch1].RemoveAt(0); break;
            case "hash": entry.Hash = string.Empty; break;
            case "size": entry.Size = 0; break;
            case "time": entry.MtimeUtc = default; break;
            case "epoch": entry.MtimeUtc = DateTime.UnixEpoch; break;
        }
        var files = ReadProbe();

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        await files.Received(1).ReadTextAsync(ScenePath("01 - A", "scene-01.novalist"));
        Assert.DoesNotContain(report.Scenes, scene => scene.Kind == SceneChangeKind.New);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("time")]
    public async Task UnavailableFileMetadata_ReadsSceneIdentityFromDisk(string unavailable)
    {
        var (chapters, manifest, index) = await SetupAsync();
        var path = ScenePath("01 - A", "scene-01.novalist");
        var files = ReadProbe();
        if (unavailable == "size") files.GetFileSizeAsync(path).Returns(0L);
        else files.GetLastWriteTimeUtcAsync(path).Returns(DateTime.UnixEpoch);

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        Assert.False(report.HasChanges);
        await files.Received(1).ReadTextAsync(path);
    }

    [Theory]
    [InlineData("scene-00.novalist")]
    [InlineData("scene-08.novalist")]
    public async Task DuplicateOfIndexedScene_IsDetectedRegardlessOfSortOrder(string name)
    {
        var (chapters, manifest, index) = await SetupAsync();
        var original = ScenePath("01 - A", "scene-01.novalist");
        File.Copy(original, ScenePath("01 - A", name));
        var files = ReadProbe();

        var report = await new ProjectReconciler(files).ScanAsync(DraftRoot, "Chapters", chapters, manifest, index);

        Assert.Equal("DuplicateSceneId", Assert.Single(report.Conflicts).Kind);
        await files.DidNotReceive().ReadTextAsync(original);
        await files.Received(1).ReadTextAsync(ScenePath("01 - A", name));
    }

    private IFileService ReadProbe(bool batch = false)
    {
        var files = batch ? Substitute.For<IFileService, IBatchTextReader>() : Substitute.For<IFileService>();
        files.CombinePath(Arg.Any<string[]>()).Returns(call => Path.Combine(call.Arg<string[]>()));
        files.GetFileName(Arg.Any<string>()).Returns(call => Path.GetFileName(call.Arg<string>()));
        files.ExistsAsync(Arg.Any<string>()).Returns(call => _fs.ExistsAsync(call.Arg<string>()));
        files.GetDirectoriesAsync(Arg.Any<string>()).Returns(call => _fs.GetDirectoriesAsync(call.Arg<string>()));
        files.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(call => _fs.GetFilesAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2)));
        files.ReadTextAsync(Arg.Any<string>()).Returns(call => _fs.ReadTextAsync(call.Arg<string>()));
        files.GetFileSizeAsync(Arg.Any<string>()).Returns(call => _fs.GetFileSizeAsync(call.Arg<string>()));
        files.GetLastWriteTimeUtcAsync(Arg.Any<string>()).Returns(call => _fs.GetLastWriteTimeUtcAsync(call.Arg<string>()));
        if (files is IBatchTextReader reader)
        {
            reader.ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(call => ReadContentsAsync(call.ArgAt<IReadOnlyList<string>>(0), existingOnly: false));
            reader.ReadExistingTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(call => ReadContentsAsync(call.ArgAt<IReadOnlyList<string>>(0), existingOnly: true));
        }
        return files;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadContentsAsync(IReadOnlyList<string> paths, bool existingOnly)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Reverse())
            if (!existingOnly || await _fs.ExistsAsync(path)) contents[path] = await _fs.ReadTextAsync(path);
        return contents;
    }

    // ── Scenes ──

    [Fact]
    public async Task NewSceneFile_WithId_ReportedNew()
    {
        var (chapters, manifest, index) = await SetupAsync();
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-05.novalist"), FileFrontMatter.Build("brand-new") + "<p>z</p>");

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Scenes);
        Assert.Equal(SceneChangeKind.New, change.Kind);
        Assert.Equal("brand-new", change.Id);
        Assert.Equal(Ch1, change.ChapterGuid);
    }

    [Fact]
    public async Task NewSceneFile_WithoutId_ReportedNewWithEmptyId()
    {
        var (chapters, manifest, index) = await SetupAsync();
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-06.novalist"), "<p>no id</p>");

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Scenes);
        Assert.Equal(SceneChangeKind.New, change.Kind);
        Assert.Equal("", change.Id);
    }

    [Fact]
    public async Task MoveStampedFile_AcrossChapters_ReportedMoved()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist"));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("02 - B", "scene-02.novalist"), content);

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Scenes);
        Assert.Equal(SceneChangeKind.Moved, change.Kind);
        Assert.Equal(Ch1, change.FromChapterGuid);
        Assert.Equal(Ch2, change.ChapterGuid);
    }

    [Fact]
    public async Task RenameSceneFile_SameChapter_ReportedRenamed()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist"));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-09.novalist"), content);

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Scenes);
        Assert.Equal(SceneChangeKind.Renamed, change.Kind);
        Assert.Equal("scene-09.novalist", change.FileName);
        Assert.Equal("scene-02.novalist", change.OldFileName);
    }

    [Fact]
    public async Task DeleteSceneFile_ReportedDeleted()
    {
        var (chapters, manifest, index) = await SetupAsync();
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Scenes);
        Assert.Equal(SceneChangeKind.Deleted, change.Kind);
        Assert.Equal("scene-02.novalist", change.FileName);
    }

    [Fact]
    public async Task MoveIdlessFile_RecoveredByHash_ReportedMoved()
    {
        var (chapters, manifest, index) = await SetupAsync();
        // Strip the id, delete original, drop the same body (no id) into another chapter.
        var stripped = FileFrontMatter.Strip(await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist")));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("02 - B", "scene-04.novalist"), stripped);

        var report = await ScanAsync(chapters, manifest, index);

        var moved = Assert.Single(report.Scenes, s => s.Kind == SceneChangeKind.Moved);
        Assert.Equal(Ch1, moved.FromChapterGuid);
        Assert.Equal(Ch2, moved.ChapterGuid);
    }

    [Fact]
    public async Task DuplicateStampedFile_ReportedConflict()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-01.novalist"));
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-08.novalist"), content); // same id twice

        var report = await ScanAsync(chapters, manifest, index);

        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("DuplicateSceneId", conflict.Kind);
    }

    // ── Chapters ──

    [Fact]
    public async Task RenameChapterFolder_MarkerKeepsGuid_ReportedRenamed()
    {
        var (chapters, manifest, index) = await SetupAsync();
        Directory.Move(Path.Combine(Chapters, "01 - A"), Path.Combine(Chapters, "01 - Arrival"));

        var report = await ScanAsync(chapters, manifest, index);

        var change = Assert.Single(report.Chapters);
        Assert.Equal(ChapterChangeKind.Renamed, change.Kind);
        Assert.Equal(Ch1, change.Guid);
        Assert.Equal("01 - Arrival", change.FolderName);
        Assert.Equal("01 - A", change.OldFolderName);
        Assert.DoesNotContain(report.Scenes, s => s.Kind == SceneChangeKind.Deleted); // scenes moved with folder
    }

    [Fact]
    public async Task NewChapterFolder_NoMarker_ReportedNew()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var dir = Path.Combine(Chapters, "03 - New");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "scene-01.novalist"), FileFrontMatter.Build("new-scene") + "<p>x</p>");

        var report = await ScanAsync(chapters, manifest, index);

        var ch = Assert.Single(report.Chapters);
        Assert.Equal(ChapterChangeKind.New, ch.Kind);
        Assert.Equal("03 - New", ch.FolderName);
        Assert.Contains(report.Scenes, s => s.Kind == SceneChangeKind.New && s.Id == "new-scene");
    }

    [Fact]
    public async Task DeleteChapterFolder_ReportedDeleted_WithCascadedScene()
    {
        var (chapters, manifest, index) = await SetupAsync();
        Directory.Delete(Path.Combine(Chapters, "02 - B"), recursive: true);

        var report = await ScanAsync(chapters, manifest, index);

        var ch = Assert.Single(report.Chapters);
        Assert.Equal(ChapterChangeKind.Deleted, ch.Kind);
        Assert.Equal(Ch2, ch.Guid);
        Assert.Contains(report.Scenes, s => s.Kind == SceneChangeKind.Deleted && s.ChapterGuid == Ch2);
    }

    [Fact]
    public async Task ChapterMarkerMissing_MatchesByFolderName_NoChange()
    {
        var (chapters, manifest, index) = await SetupAsync();
        // Pre-migration leftover: a chapter folder without a .nvchapter.json marker.
        File.Delete(Path.Combine(Chapters, "01 - A", ChapterMarker.FileName));

        var report = await ScanAsync(chapters, manifest, index);

        Assert.DoesNotContain(report.Chapters, c => c.FolderName == "01 - A"); // matched by name, not "new"
    }

    [Fact]
    public async Task ManifestChapterAbsentFromDraft_ScenesSkipped_NoCrash()
    {
        var (chapters, manifest, index) = await SetupAsync();
        // Manifest references a chapter guid that draft.json doesn't define, with a missing file.
        manifest.Chapters["ghost-guid"] = new List<SceneData>
        {
            new() { Id = "ghost", FileName = "scene-01.novalist", ChapterGuid = "ghost-guid", Order = 1 },
        };

        var report = await ScanAsync(chapters, manifest, index);

        Assert.DoesNotContain(report.Scenes, s => s.Id == "ghost"); // skipped, not reported deleted
    }

    // ── Archive ──

    [Fact]
    public async Task ArchiveFolder_ExcludedFromScan()
    {
        var (chapters, manifest, index) = await SetupAsync();
        // Archived scene file exists under __Archive but must not surface as a change.
        var report = await ScanAsync(chapters, manifest, index);
        Assert.DoesNotContain(report.Chapters, c => c.FolderName.Contains("__Archive"));
        Assert.False(report.HasChanges);
    }

    // ── Apply ──

    private const string S2Id = "sc-01-A-scene-02.novalist";
    private const string S1Id = "sc-01-A-scene-01.novalist";

    [Fact]
    public async Task Apply_FreshlyMigrated_NoSpuriousMutation()
    {
        var (chapters, manifest, index) = await SetupAsync();
        await ApplyAsync(chapters, manifest, index);
        // Stable: 2 chapters, 3 active scenes, archive untouched.
        Assert.Equal(2, chapters.Count);
        Assert.Equal(3, manifest.Chapters.Values.Sum(v => v.Count));
        Assert.Single(manifest.Archived);
    }

    [Fact]
    public async Task Apply_MoveStampedFile_UpdatesManifest_PreservesMetadata()
    {
        var (chapters, manifest, index) = await SetupAsync();
        Find(manifest, S2Id)!.Notes = "keep-me";
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist"));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("02 - B", "scene-02.novalist"), content);

        await ApplyAsync(chapters, manifest, index);

        var moved = Find(manifest, S2Id)!;
        Assert.Equal(Ch2, moved.ChapterGuid);
        Assert.Equal("scene-02.novalist", moved.FileName);
        Assert.Equal("keep-me", moved.Notes);                       // metadata carried across
        Assert.DoesNotContain(manifest.Chapters[Ch1], s => s.Id == S2Id);
    }

    [Fact]
    public async Task Apply_RenameFile_UpdatesFileName()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist"));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-09.novalist"), content);

        await ApplyAsync(chapters, manifest, index);

        Assert.Equal("scene-09.novalist", Find(manifest, S2Id)!.FileName);
    }

    [Fact]
    public async Task Apply_DeleteFile_RemovesEntry()
    {
        var (chapters, manifest, index) = await SetupAsync();
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));

        await ApplyAsync(chapters, manifest, index);

        Assert.Null(Find(manifest, S2Id));
        Assert.Single(manifest.Chapters[Ch1]); // scene-01 remains
    }

    [Fact]
    public async Task Apply_NewIdlessFile_MintsIdAndStampsOnDisk()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var path = ScenePath("01 - A", "scene-05.novalist");
        await File.WriteAllTextAsync(path, "<p>fresh</p>");

        await ApplyAsync(chapters, manifest, index);

        Assert.Equal(3, manifest.Chapters[Ch1].Count); // scene-01, scene-02, scene-05
        var stamped = await File.ReadAllTextAsync(path);
        Assert.True(FileFrontMatter.TryParse(stamped, out var p));
        Assert.Equal("<p>fresh</p>", FileFrontMatter.Strip(stamped));
        Assert.Contains(manifest.Chapters[Ch1], s => s.Id == p.Id && s.FileName == "scene-05.novalist");
    }

    [Fact]
    public async Task Apply_NewChapterFolder_AddsChapterAndScenes()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var dir = Path.Combine(Chapters, "03 - New");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "scene-01.novalist"), FileFrontMatter.Build("new-scene") + "<p>x</p>");

        await ApplyAsync(chapters, manifest, index);

        var newCh = Assert.Single(chapters, c => c.FolderName == "03 - New");
        Assert.Equal("New", newCh.Title);
        Assert.Contains(manifest.Chapters[newCh.Guid], s => s.Id == "new-scene");
    }

    [Fact]
    public async Task Apply_NewChapterFolder_WithMarker_AdoptsMarkerIdentity()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var dir = Path.Combine(Chapters, "03 - Marked");
        Directory.CreateDirectory(dir);
        var marker = new ChapterMarker { Guid = "ext-guid", Title = "Marked Ext", Order = 99 };
        await File.WriteAllTextAsync(Path.Combine(dir, ChapterMarker.FileName), System.Text.Json.JsonSerializer.Serialize(marker));
        await File.WriteAllTextAsync(Path.Combine(dir, "scene-01.novalist"), FileFrontMatter.Build("ms") + "<p>x</p>");

        await ApplyAsync(chapters, manifest, index);

        var ch = Assert.Single(chapters, c => c.Guid == "ext-guid");
        Assert.Equal("Marked Ext", ch.Title);
        Assert.True(manifest.Chapters.ContainsKey("ext-guid"));
    }

    [Fact]
    public async Task Apply_ChapterMarkerMissing_MatchedByName_KeepsGuid()
    {
        var (chapters, manifest, index) = await SetupAsync();
        File.Delete(Path.Combine(Chapters, "01 - A", ChapterMarker.FileName));

        await ApplyAsync(chapters, manifest, index);

        Assert.Contains(chapters, c => c.Guid == Ch1 && c.FolderName == "01 - A"); // matched by name, guid kept
    }

    [Fact]
    public async Task Apply_DeleteChapterFolder_RemovesChapterAndScenes()
    {
        var (chapters, manifest, index) = await SetupAsync();
        Directory.Delete(Path.Combine(Chapters, "02 - B"), recursive: true);

        await ApplyAsync(chapters, manifest, index);

        Assert.DoesNotContain(chapters, c => c.Guid == Ch2);
        Assert.False(manifest.Chapters.ContainsKey(Ch2));
    }

    [Fact]
    public async Task Apply_DuplicateId_RemintsTheCopy()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var content = await File.ReadAllTextAsync(ScenePath("01 - A", "scene-01.novalist"));
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-08.novalist"), content); // same id duplicated

        await ApplyAsync(chapters, manifest, index);

        // scene-01 (sorted first) keeps the id; scene-08 is re-minted + re-stamped with a new id.
        Assert.Equal(S1Id, manifest.Chapters[Ch1].First(s => s.FileName == "scene-01.novalist").Id);
        var copyId = manifest.Chapters[Ch1].First(s => s.FileName == "scene-08.novalist").Id;
        Assert.NotEqual(S1Id, copyId);
        Assert.True(FileFrontMatter.TryParse(await File.ReadAllTextAsync(ScenePath("01 - A", "scene-08.novalist")), out var p));
        Assert.Equal(copyId, p.Id);
    }

    [Fact]
    public async Task Apply_RenameChapterFolder_PreservesGuidAndOrder()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var order = chapters.First(c => c.Guid == Ch1).Order;
        Directory.Move(Path.Combine(Chapters, "01 - A"), Path.Combine(Chapters, "01 - Arrival"));

        await ApplyAsync(chapters, manifest, index);

        var ch = chapters.First(c => c.Guid == Ch1);
        Assert.Equal("01 - Arrival", ch.FolderName);
        Assert.Equal(order, ch.Order);                 // survivor order untouched
        Assert.Equal(2, manifest.Chapters[Ch1].Count); // scenes followed the folder
    }

    [Fact]
    public async Task Apply_IdlessMove_RecoveredByHash_KeepsSceneId()
    {
        var (chapters, manifest, index) = await SetupAsync();
        var stripped = FileFrontMatter.Strip(await File.ReadAllTextAsync(ScenePath("01 - A", "scene-02.novalist")));
        File.Delete(ScenePath("01 - A", "scene-02.novalist"));
        await File.WriteAllTextAsync(ScenePath("02 - B", "scene-04.novalist"), stripped); // no id

        await ApplyAsync(chapters, manifest, index);

        var moved = Find(manifest, S2Id);
        Assert.NotNull(moved);                         // original id recovered, not re-minted
        Assert.Equal(Ch2, moved!.ChapterGuid);
    }

    [Fact]
    public async Task Apply_RewritesIndex_AndIsIdempotent()
    {
        var (chapters, manifest, index) = await SetupAsync();
        await File.WriteAllTextAsync(ScenePath("01 - A", "scene-05.novalist"), "<p>new</p>");
        await ApplyAsync(chapters, manifest, index);

        // Index now reflects the new file.
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rebuilt = JsonSerializer.Deserialize<DraftIndex>(await File.ReadAllTextAsync(Path.Combine(DraftRoot, ".nvindex.json")), opts)!;
        Assert.Contains("Chapters/01 - A/scene-05.novalist", rebuilt.Entries.Keys);

        // Second scan (with rebuilt index + reconciled state) finds nothing more to do.
        var report = await ScanAsync(chapters, manifest, rebuilt);
        Assert.False(report.HasChanges);
    }
}
