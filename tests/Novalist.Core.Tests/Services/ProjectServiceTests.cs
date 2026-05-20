using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ProjectServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _sut = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private Task<ProjectMetadata> Create(string name = "My Project", string book = "Book One")
        => _sut.CreateProjectAsync(_dir.Path, name, book);

    // ── creation / load ──

    [Fact]
    public async Task CreateProject_BuildsStructureAndState()
    {
        var meta = await Create();
        Assert.True(_sut.IsProjectLoaded);
        Assert.NotNull(_sut.ActiveBook);
        Assert.Equal(meta.ActiveBookId, _sut.ActiveBook!.Id);
        Assert.True(Directory.Exists(Path.Combine(_sut.ProjectRoot!, ".novalist")));
        Assert.True(File.Exists(Path.Combine(_sut.ProjectRoot!, ".novalist", "project.json")));
        Assert.True(Directory.Exists(_sut.ActiveDraftRoot!));
        Assert.NotNull(_sut.WorldBibleRoot);
    }

    [Fact]
    public async Task LoadProject_RoundTrips()
    {
        var meta = await Create();
        await _sut.CreateChapterAsync("Chapter A");
        var root = _sut.ProjectRoot!;

        var loaded = new ProjectService(new FileService());
        var reloaded = await loaded.LoadProjectAsync(root);

        Assert.Equal(meta.Name, reloaded.Name);
        Assert.Single(loaded.GetChaptersOrdered());
    }

    [Fact]
    public async Task LoadProject_MissingFile_Throws()
    {
        var loaded = new ProjectService(new FileService());
        await Assert.ThrowsAsync<FileNotFoundException>(() => loaded.LoadProjectAsync(_dir.Path));
    }

    [Fact]
    public async Task LoadProject_CorruptMetadata_Throws()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, ".novalist"));
        await File.WriteAllTextAsync(Path.Combine(_dir.Path, ".novalist", "project.json"), "null");
        var loaded = new ProjectService(new FileService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => loaded.LoadProjectAsync(_dir.Path));
    }

    [Fact]
    public async Task RootProperties_NullWhenNoProject()
    {
        Assert.Null(_sut.ActiveBookRoot);
        Assert.Null(_sut.ActiveDraftRoot);
        Assert.Null(_sut.WorldBibleRoot);
        Assert.False(_sut.IsProjectLoaded);
        await Task.CompletedTask;
    }

    // ── chapters / scenes ──

    [Fact]
    public async Task CreateChapter_AndScene_PersistOnDisk()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("Intro", "2024-01-01");
        Assert.Equal(1, ch.Order);
        Assert.True(Directory.Exists(_sut.GetChapterFolderPath(ch)));

        var sc = await _sut.CreateSceneAsync(ch.Guid, "Opening");
        Assert.Equal(1, sc.Order);
        Assert.True(File.Exists(_sut.GetSceneFilePath(ch, sc)));
        Assert.Single(_sut.GetScenesForChapter(ch.Guid));

        var ch2 = await _sut.CreateChapterAsync("Second");
        Assert.Equal(2, ch2.Order);
    }

    [Fact]
    public async Task CreateChapter_NoBook_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateChapterAsync("x"));

    [Fact]
    public async Task CreateScene_UnknownChapter_Throws()
    {
        await Create();
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateSceneAsync("nope", "x"));
    }

    [Fact]
    public async Task ReadWriteSceneContent_RoundTrips()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc, "<p>hi</p>");
        Assert.Equal("<p>hi</p>", await _sut.ReadSceneContentAsync(ch, sc));
    }

    [Fact]
    public async Task ReadSceneContent_MissingFile_ReturnsEmpty()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = new SceneData { FileName = "ghost.novalist", ChapterGuid = ch.Guid };
        Assert.Equal(string.Empty, await _sut.ReadSceneContentAsync(ch, sc));
    }

    [Fact]
    public async Task SceneAndChapterMutators()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");

        await _sut.SetChapterDateAsync(ch.Guid, " 2024-02-02 ");
        Assert.Equal("2024-02-02", ch.Date);
        await _sut.SetChapterFavoriteAsync(ch.Guid, true);
        Assert.True(ch.IsFavorite);
        await _sut.SetChapterDateRangeAsync(ch.Guid, new StoryDateRange { Start = "2024-01-01", End = "2024-01-05" });
        Assert.NotNull(ch.DateRange);

        await _sut.SetSceneDateAsync(ch.Guid, sc.Id, " 2024-03-03 ");
        Assert.Equal("2024-03-03", sc.Date);
        await _sut.SetSceneFavoriteAsync(ch.Guid, sc.Id, true);
        Assert.True(sc.IsFavorite);
        await _sut.SetSceneLabelColorAsync(ch.Guid, sc.Id, " #fff ");
        Assert.Equal("#fff", sc.LabelColor);
        await _sut.SetSceneLabelColorAsync(ch.Guid, sc.Id, "  ");
        Assert.Null(sc.LabelColor);
        await _sut.SetSceneDateRangeAsync(ch.Guid, sc.Id, new StoryDateRange { Start = "2024-01-01", End = "2024-01-02" });
        Assert.NotNull(sc.DateRange);
        await _sut.SetSceneAnalysisOverridesAsync(ch.Guid, sc.Id, new SceneAnalysisOverrides { Pov = "Alice" });
        Assert.NotNull(sc.AnalysisOverrides);
    }

    [Fact]
    public async Task Mutators_UnknownTargets_NoOp()
    {
        await Create();
        // No throw on unknown chapter/scene ids.
        await _sut.SetChapterDateAsync("x", "d");
        await _sut.SetSceneDateAsync("x", "y", "d");
        await _sut.SetChapterFavoriteAsync("x", true);
        await _sut.SetSceneFavoriteAsync("x", "y", true);
        await _sut.SetSceneLabelColorAsync("x", "y", "c");
        await _sut.SetChapterDateRangeAsync("x", null);
        await _sut.SetSceneDateRangeAsync("x", "y", null);
        await _sut.SetSceneAnalysisOverridesAsync("x", "y", null);
        await _sut.RenameChapterAsync("x", "n");
        await _sut.RenameSceneAsync("x", "y", "n");
        await _sut.DeleteChapterAsync("x");
        await _sut.DeleteSceneAsync("x", "y");
    }

    [Fact]
    public async Task RenameChapter_MovesFolder()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("Old");
        await _sut.RenameChapterAsync(ch.Guid, "New Title");
        Assert.Equal("New Title", ch.Title);
        Assert.True(Directory.Exists(_sut.GetChapterFolderPath(ch)));
    }

    [Fact]
    public async Task RenameScene()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "Old");
        await _sut.RenameSceneAsync(ch.Guid, sc.Id, "New");
        Assert.Equal("New", sc.Title);
    }

    [Fact]
    public async Task DeleteChapterAndScene()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.DeleteSceneAsync(ch.Guid, sc.Id);
        Assert.Empty(_sut.GetScenesForChapter(ch.Guid));
        await _sut.DeleteChapterAsync(ch.Guid);
        Assert.Empty(_sut.GetChaptersOrdered());
    }

    [Fact]
    public async Task ReorderChapters_BothDirections()
    {
        await Create();
        var a = await _sut.CreateChapterAsync("A");
        var b = await _sut.CreateChapterAsync("B");
        var c = await _sut.CreateChapterAsync("C");
        await _sut.ReorderChapterAsync(a.Guid, 3); // move down
        Assert.Equal(3, a.Order);
        await _sut.ReorderChapterAsync(c.Guid, 1); // move up
        Assert.Equal(1, c.Order);
        await _sut.ReorderChapterAsync(c.Guid, 1); // no-op (same order)
    }

    [Fact]
    public async Task MoveChapters_Reorders()
    {
        await Create();
        var a = await _sut.CreateChapterAsync("A");
        var b = await _sut.CreateChapterAsync("B");
        var c = await _sut.CreateChapterAsync("C");
        await _sut.MoveChaptersAsync(new[] { c.Guid, a.Guid }, 0);
        var ordered = _sut.GetChaptersOrdered();
        // Moved items keep their existing document order (a before c), placed at the front.
        Assert.Equal(a.Guid, ordered[0].Guid);
        Assert.Equal(c.Guid, ordered[1].Guid);
        Assert.Equal(b.Guid, ordered[2].Guid);
        await _sut.MoveChaptersAsync(new[] { "ghost" }, 0); // none moving -> no-op
        await _sut.MoveChaptersAsync(Array.Empty<string>(), 0);
    }

    [Fact]
    public async Task ReorderScene_BothDirections()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var s1 = await _sut.CreateSceneAsync(ch.Guid, "1");
        var s2 = await _sut.CreateSceneAsync(ch.Guid, "2");
        var s3 = await _sut.CreateSceneAsync(ch.Guid, "3");
        await _sut.ReorderSceneAsync(ch.Guid, s1.Id, 3);
        Assert.Equal(3, s1.Order);
        await _sut.ReorderSceneAsync(ch.Guid, s3.Id, 1);
        Assert.Equal(1, s3.Order);
        await _sut.ReorderSceneAsync(ch.Guid, s3.Id, 1); // no-op
    }

    [Fact]
    public async Task MoveScenes_AcrossChapters_MovesFiles()
    {
        await Create();
        var c1 = await _sut.CreateChapterAsync("C1");
        var c2 = await _sut.CreateChapterAsync("C2");
        var s1 = await _sut.CreateSceneAsync(c1.Guid, "S1");
        await _sut.WriteSceneContentAsync(c1, s1, "content");

        await _sut.MoveScenesAsync(new[] { s1.Id }, c2.Guid, 0);
        Assert.Empty(_sut.GetScenesForChapter(c1.Guid));
        Assert.Single(_sut.GetScenesForChapter(c2.Guid));
        Assert.Equal(c2.Guid, s1.ChapterGuid);
    }

    [Fact]
    public async Task MoveScenes_WithinChapter_Reorders()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var s1 = await _sut.CreateSceneAsync(ch.Guid, "1");
        var s2 = await _sut.CreateSceneAsync(ch.Guid, "2");
        await _sut.MoveScenesAsync(new[] { s2.Id }, ch.Guid, 0);
        Assert.Equal(s2.Id, _sut.GetScenesForChapter(ch.Guid)[0].Id);
    }

    [Fact]
    public async Task MoveScenes_EdgeCases_NoOp()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        await _sut.MoveScenesAsync(Array.Empty<string>(), ch.Guid, 0);
        await _sut.MoveScenesAsync(new[] { "x" }, "unknown-chapter", 0);
        await _sut.MoveScenesAsync(new[] { "no-such-scene" }, ch.Guid, 0);
    }

    // ── archive / restore ──

    [Fact]
    public async Task ArchiveRestoreDeleteScene()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc, "body");

        await _sut.ArchiveSceneAsync(ch.Guid, sc.Id);
        Assert.Empty(_sut.GetScenesForChapter(ch.Guid));
        Assert.Single(_sut.GetArchivedScenes());
        Assert.Equal("body", await _sut.ReadArchivedSceneContentAsync(sc));

        await _sut.RestoreArchivedSceneAsync(sc.Id, ch.Guid, 0);
        Assert.Single(_sut.GetScenesForChapter(ch.Guid));
        Assert.Empty(_sut.GetArchivedScenes());

        await _sut.ArchiveSceneAsync(ch.Guid, sc.Id);
        await _sut.DeleteArchivedSceneAsync(sc.Id);
        Assert.Empty(_sut.GetArchivedScenes());
    }

    [Fact]
    public async Task Archive_FileNameCollision_SuffixesWithId()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var s1 = await _sut.CreateSceneAsync(ch.Guid, "1");
        var s2 = await _sut.CreateSceneAsync(ch.Guid, "2");
        await _sut.WriteSceneContentAsync(ch, s1, "a");
        await _sut.WriteSceneContentAsync(ch, s2, "b");
        // Both have scene-01/scene-02; archive both. Force a collision by giving s2 the same name.
        await _sut.ArchiveSceneAsync(ch.Guid, s1.Id);
        s2.FileName = s1.FileName; // collide on archive target name
        await _sut.WriteSceneContentAsync(ch, s2, "b2");
        await _sut.ArchiveSceneAsync(ch.Guid, s2.Id);
        Assert.Equal(2, _sut.GetArchivedScenes().Count);
    }

    [Fact]
    public async Task RestoreArchived_UnknownTargets_NoOp()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        await _sut.RestoreArchivedSceneAsync("no-scene", ch.Guid, null);
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.ArchiveSceneAsync(ch.Guid, sc.Id);
        await _sut.RestoreArchivedSceneAsync(sc.Id, "no-chapter", null); // unknown target chapter
        Assert.Single(_sut.GetArchivedScenes());
    }

    // ── books ──

    [Fact]
    public async Task CreateSwitchRenameDeleteBook()
    {
        await Create();
        var b2 = await _sut.CreateBookAsync("Book Two");
        Assert.Equal(2, _sut.CurrentProject!.Books.Count);
        await _sut.SwitchBookAsync(b2.Id);
        Assert.Equal(b2.Id, _sut.ActiveBook!.Id);
        await _sut.RenameBookAsync(b2.Id, "Renamed Book");
        Assert.Equal("Renamed Book", b2.Name);
        await _sut.DeleteBookAsync(b2.Id);
        Assert.Single(_sut.CurrentProject.Books);
    }

    [Fact]
    public async Task DeleteBook_LastBook_Throws()
    {
        await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteBookAsync(_sut.ActiveBook!.Id));
    }

    [Fact]
    public async Task SwitchBook_Unknown_Throws()
    {
        await Create();
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SwitchBookAsync("nope"));
    }

    [Fact]
    public async Task CreateBook_NoProject_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateBookAsync("x"));

    [Fact]
    public async Task RenameProject()
    {
        await Create();
        await _sut.RenameProjectAsync(" New Name ");
        Assert.Equal("New Name", _sut.CurrentProject!.Name);
        await _sut.RenameProjectAsync("   "); // blank ignored
        Assert.Equal("New Name", _sut.CurrentProject.Name);
    }

    // ── drafts ──

    [Fact]
    public async Task CreateSwitchRenameDeleteDraft()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var d2 = await _sut.CreateDraftAsync("Draft 2");
        Assert.Equal(2, _sut.ActiveBook!.Drafts.Count);

        await _sut.SwitchDraftAsync(d2.Id);
        Assert.Equal(d2.Id, _sut.ActiveBook.ActiveDraftId);
        Assert.Empty(_sut.GetChaptersOrdered()); // new empty draft

        await _sut.RenameDraftAsync(d2.Id, "Renamed Draft");
        Assert.Equal("Renamed Draft", _sut.ActiveBook.Drafts.First(d => d.Id == d2.Id).Name);

        await _sut.DeleteDraftAsync(d2.Id);
        Assert.Single(_sut.ActiveBook.Drafts);
    }

    [Fact]
    public async Task CreateDraft_Clone_CopiesTree()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc, "data");
        var active = _sut.ActiveBook!.ActiveDraftId;

        var clone = await _sut.CreateDraftAsync("Clone", cloneFromDraftId: active);
        await _sut.SwitchDraftAsync(clone.Id);
        Assert.Single(_sut.GetChaptersOrdered()); // cloned chapter present
    }

    [Fact]
    public async Task DeleteDraft_LastDraft_Throws()
    {
        await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteDraftAsync(_sut.ActiveBook!.ActiveDraftId));
    }

    [Fact]
    public async Task SwitchDraft_SameOrUnknown_NoOp()
    {
        await Create();
        await _sut.SwitchDraftAsync(_sut.ActiveBook!.ActiveDraftId); // same -> no-op
        await _sut.SwitchDraftAsync("unknown");
    }

    [Fact]
    public async Task CreateDraft_NoBook_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateDraftAsync("x"));

    [Fact]
    public async Task CreateDraft_DuplicateName_GetsUniqueFolder()
    {
        await Create();
        var d1 = await _sut.CreateDraftAsync("Same");
        var d2 = await _sut.CreateDraftAsync("Same");
        Assert.NotEqual(d1.FolderName, d2.FolderName);
    }

    // ── mention sync ──

    [Fact]
    public async Task SyncMentionDisplayText_RewritesMatchingSpans()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc,
            "<p><span data-entity-id=\"e1\" class=\"nv-entity-mention\">Old</span></p>");

        var count = await _sut.SyncMentionDisplayTextAsync("e1", "New");
        Assert.Equal(1, count);
        Assert.Contains("New", await _sut.ReadSceneContentAsync(ch, sc));
    }

    [Fact]
    public async Task SyncMentionDisplayText_RespectsManualAndAlias_AndNonMatch()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc,
            "<span data-entity-id=\"e1\" data-mention-source=\"manual\" class=\"nv-entity-mention\">M</span>" +
            "<span data-entity-id=\"e1\" data-mention-source=\"alias\" class=\"nv-entity-mention\">A</span>" +
            "<span data-entity-id=\"other\" class=\"nv-entity-mention\">O</span>");
        var count = await _sut.SyncMentionDisplayTextAsync("e1", "New");
        Assert.Equal(0, count); // manual+alias preserved, other id ignored
    }

    [Fact]
    public async Task SyncMentionDisplayText_Guards()
    {
        Assert.Equal(0, await _sut.SyncMentionDisplayTextAsync("e1", "x")); // no project
        await Create();
        Assert.Equal(0, await _sut.SyncMentionDisplayTextAsync("", "x"));   // empty id
    }

    [Fact]
    public async Task SyncMentionDisplayText_IncludesArchived_AndSkipsPlainScenes()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var plain = await _sut.CreateSceneAsync(ch.Guid, "Plain");
        await _sut.WriteSceneContentAsync(ch, plain, "<p>no mentions here</p>");
        var withMention = await _sut.CreateSceneAsync(ch.Guid, "M");
        await _sut.WriteSceneContentAsync(ch, withMention,
            "<span data-entity-id=\"e1\" class=\"nv-entity-mention\">Old</span>");
        await _sut.ArchiveSceneAsync(ch.Guid, withMention.Id); // now archived

        var count = await _sut.SyncMentionDisplayTextAsync("e1", "New");
        Assert.Equal(1, count); // archived scene rewritten; plain scene skipped
    }

    // ── no-project / manifest-on-the-fly branches ──

    [Fact]
    public async Task NoProjectGuards()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateSceneAsync("c", "s"));
        Assert.Empty(_sut.GetArchivedScenes());        // ScenesManifest null
        Assert.Empty(_sut.GetScenesForChapter("c"));   // ScenesManifest null
        Assert.Empty(_sut.GetChaptersOrdered());       // ActiveBook null
    }

    [Fact]
    public async Task CreateScene_ManifestEntryMissing_IsRecreated()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        _sut.ScenesManifest!.Chapters.Remove(ch.Guid); // simulate missing manifest entry
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        Assert.Single(_sut.GetScenesForChapter(ch.Guid));
    }

    [Fact]
    public async Task MoveScenes_TargetChapterMissingManifest_CreatesEntry()
    {
        await Create();
        var c1 = await _sut.CreateChapterAsync("C1");
        var c2 = await _sut.CreateChapterAsync("C2");
        var s = await _sut.CreateSceneAsync(c1.Guid, "S");
        _sut.ScenesManifest!.Chapters.Remove(c2.Guid);
        await _sut.MoveScenesAsync(new[] { s.Id }, c2.Guid, 0);
        Assert.Single(_sut.GetScenesForChapter(c2.Guid));
    }

    [Fact]
    public async Task RestoreArchived_TargetChapterMissingManifest_CreatesEntry()
    {
        await Create();
        var c1 = await _sut.CreateChapterAsync("C1");
        var c2 = await _sut.CreateChapterAsync("C2");
        var s = await _sut.CreateSceneAsync(c1.Guid, "S");
        await _sut.ArchiveSceneAsync(c1.Guid, s.Id);
        _sut.ScenesManifest!.Chapters.Remove(c2.Guid);
        await _sut.RestoreArchivedSceneAsync(s.Id, c2.Guid, null);
        Assert.Single(_sut.GetScenesForChapter(c2.Guid));
    }

    // ── legacy migration (pre-multi-draft layout) ──

    private void WriteLegacyProject(out string root, bool withScenesJson = true)
    {
        root = Path.Combine(_dir.Path, "legacy");
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        var chapter = new ChapterData { Title = "Old Chapter", Order = 1, FolderName = "01 - Old Chapter" };
        var book = new BookData
        {
            Id = "book-legacy",
            Name = "Legacy Book",
            FolderName = "LegacyBook",
            Drafts = new(),               // pre-multi-draft -> triggers migration
            Chapters = new() { chapter }
        };
        var meta = new ProjectMetadata { Id = "p", Name = "Legacy", ActiveBookId = book.Id, Books = { book } };

        Directory.CreateDirectory(Path.Combine(root, ".novalist"));
        File.WriteAllText(Path.Combine(root, ".novalist", "project.json"), JsonSerializer.Serialize(meta, jsonOpts));

        // Legacy chapter content at book root.
        var chDir = Path.Combine(root, "LegacyBook", "Chapters", chapter.FolderName);
        Directory.CreateDirectory(chDir);
        var scene = new SceneData { Title = "Scene", Order = 1, FileName = "scene-01.novalist", ChapterGuid = chapter.Guid };
        File.WriteAllText(Path.Combine(chDir, scene.FileName), "<p>legacy</p>");

        if (withScenesJson)
        {
            var manifest = new ScenesManifest();
            manifest.Chapters[chapter.Guid] = new() { scene };
            Directory.CreateDirectory(Path.Combine(root, "LegacyBook", ".book"));
            File.WriteAllText(Path.Combine(root, "LegacyBook", ".book", "scenes.json"),
                JsonSerializer.Serialize(manifest, jsonOpts));
        }
    }

    [Fact]
    public async Task LoadProject_MigratesLegacyToMultiDraft()
    {
        WriteLegacyProject(out var root);
        var svc = new ProjectService(new FileService());
        await svc.LoadProjectAsync(root);

        // A default draft was created and chapter content moved under it.
        Assert.NotEmpty(svc.ActiveBook!.Drafts);
        Assert.True(Directory.Exists(Path.Combine(root, "LegacyBook", "Drafts", "default", "Chapters")));
        Assert.True(File.Exists(Path.Combine(root, "LegacyBook", "Drafts", "default", "draft.json")));
        // Scenes manifest loaded from the migrated location.
        Assert.Single(svc.GetChaptersOrdered());
        Assert.Single(svc.GetScenesForChapter(svc.GetChaptersOrdered()[0].Guid));
        // No settings.json existed -> defaults.
        Assert.NotNull(svc.ProjectSettings);
    }

    [Fact]
    public async Task LoadProject_LegacyWithoutScenesJson_StillLoads()
    {
        WriteLegacyProject(out var root, withScenesJson: false);
        var svc = new ProjectService(new FileService());
        await svc.LoadProjectAsync(root);
        Assert.Single(svc.GetChaptersOrdered());
    }

    [Fact]
    public async Task LoadProject_NoBooks_Throws()
    {
        var root = Path.Combine(_dir.Path, "nobooks");
        Directory.CreateDirectory(Path.Combine(root, ".novalist"));
        var meta = new ProjectMetadata { Id = "p", Name = "Empty", Books = { } };
        File.WriteAllText(Path.Combine(root, ".novalist", "project.json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        var svc = new ProjectService(new FileService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.LoadProjectAsync(root));
    }

    [Fact]
    public async Task SwitchBook_NoProject_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SwitchBookAsync("x"));

    [Fact]
    public async Task CreateDraft_ThirdDuplicate_IncrementsSuffix()
    {
        await Create();
        await _sut.CreateDraftAsync("Same");
        await _sut.CreateDraftAsync("Same");
        var d3 = await _sut.CreateDraftAsync("Same");
        Assert.EndsWith("-3", d3.FolderName);
    }

    [Fact]
    public async Task DeleteChapter_ReindexesRemaining()
    {
        await Create();
        var a = await _sut.CreateChapterAsync("A");
        var b = await _sut.CreateChapterAsync("B");
        await _sut.DeleteChapterAsync(a.Guid);
        Assert.Equal(1, b.Order); // reindexed
    }

    [Fact]
    public async Task DeleteScene_ReindexesRemaining()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var s1 = await _sut.CreateSceneAsync(ch.Guid, "1");
        var s2 = await _sut.CreateSceneAsync(ch.Guid, "2");
        await _sut.DeleteSceneAsync(ch.Guid, s1.Id);
        Assert.Equal(1, s2.Order);
    }

    [Fact]
    public async Task ReadArchivedSceneContent_MissingFile_ReturnsEmpty()
    {
        await Create();
        var sc = new SceneData { FileName = "ghost.novalist" };
        Assert.Equal(string.Empty, await _sut.ReadArchivedSceneContentAsync(sc));
    }

    [Fact]
    public async Task SyncMentionDisplayText_AlreadyCurrent_NoChange()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var sc = await _sut.CreateSceneAsync(ch.Guid, "S");
        await _sut.WriteSceneContentAsync(ch, sc,
            "<span data-entity-id=\"e1\" class=\"nv-entity-mention\">Same</span>");
        Assert.Equal(0, await _sut.SyncMentionDisplayTextAsync("e1", "Same")); // inner already equal
    }

    [Fact]
    public async Task LoadScenesManifest_LegacyFallback_AndMissingDraftData()
    {
        await Create();
        var ch = await _sut.CreateChapterAsync("C");
        var root = _sut.ProjectRoot!;
        var book = _sut.ActiveBook!;
        // Remove the active-draft data so loaders fall back to the legacy .book location.
        File.Delete(Path.Combine(_sut.ActiveDraftRoot!, "scenes.json"));
        var draftData = Path.Combine(_sut.ActiveDraftRoot!, "draft.json");
        if (File.Exists(draftData)) File.Delete(draftData);
        var bookMeta = Path.Combine(root, book.FolderName, ".book");
        Directory.CreateDirectory(bookMeta);
        var manifest = new ScenesManifest();
        manifest.Chapters[ch.Guid] = new() { new SceneData { Title = "Legacy", Order = 1, FileName = "scene-01.novalist" } };
        File.WriteAllText(Path.Combine(bookMeta, "scenes.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        await _sut.SwitchBookAsync(book.Id); // re-runs LoadScenesManifest + LoadActiveDraftData
        Assert.Single(_sut.GetScenesForChapter(ch.Guid));
    }

    [Fact]
    public async Task LoadProject_ActiveDraftIdMismatch_SkipsDraftDataLoad()
    {
        var root = Path.Combine(_dir.Path, "mismatch");
        var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        var draft = new BookDraftMetadata { Id = "dA", Name = "A", FolderName = "a" };
        var book = new BookData
        {
            Id = "b", Name = "B", FolderName = "B",
            Drafts = new() { draft },
            ActiveDraftId = "does-not-exist" // -> ActiveDraft null -> draft data path null
        };
        var meta = new ProjectMetadata { Id = "p", Name = "M", ActiveBookId = "b", Books = { book } };
        Directory.CreateDirectory(Path.Combine(root, ".novalist"));
        File.WriteAllText(Path.Combine(root, ".novalist", "project.json"), JsonSerializer.Serialize(meta, opts));
        Directory.CreateDirectory(Path.Combine(root, "B", "Drafts", "a"));

        var svc = new ProjectService(new FileService());
        await svc.LoadProjectAsync(root); // no throw; LoadActiveDraftData returns early
        Assert.NotNull(svc.CurrentProject);
    }

    [Fact]
    public async Task LoadProject_HalfMigratedLayout_MergesAndMovesSnapshots()
    {
        var root = Path.Combine(_dir.Path, "half");
        var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        var chMerge = new ChapterData { Title = "Merge", Order = 1, FolderName = "01 - Merge" };
        var chKeep = new ChapterData { Title = "Keep", Order = 2, FolderName = "02 - Keep" };
        var defaultDraft = new BookDraftMetadata { Id = "draft-default", Name = "Draft 1", FolderName = "default" };
        var book = new BookData
        {
            Id = "b", Name = "B", FolderName = "B",
            Drafts = new() { defaultDraft },          // already has a draft -> Fixup path (not Migrate)
            ActiveDraftId = "draft-default",
            Chapters = new() { chMerge, chKeep }
        };
        var meta = new ProjectMetadata { Id = "p", Name = "Half", ActiveBookId = "b", Books = { book } };

        Directory.CreateDirectory(Path.Combine(root, ".novalist"));
        File.WriteAllText(Path.Combine(root, ".novalist", "project.json"), JsonSerializer.Serialize(meta, opts));

        var bookRoot = Path.Combine(root, "B");
        // Legacy chapters at book root.
        Directory.CreateDirectory(Path.Combine(bookRoot, "Chapters", chMerge.FolderName));
        File.WriteAllText(Path.Combine(bookRoot, "Chapters", chMerge.FolderName, "scene-01.novalist"), "merge-me");
        Directory.CreateDirectory(Path.Combine(bookRoot, "Chapters", chKeep.FolderName));
        File.WriteAllText(Path.Combine(bookRoot, "Chapters", chKeep.FolderName, "scene-01.novalist"), "keep-legacy");
        // Draft already has: an empty stub for Merge (-> files merged in) and a populated Keep (-> skipped).
        var draftChapters = Path.Combine(bookRoot, "Drafts", "default", "Chapters");
        Directory.CreateDirectory(Path.Combine(draftChapters, chMerge.FolderName)); // empty stub
        Directory.CreateDirectory(Path.Combine(draftChapters, chKeep.FolderName));
        File.WriteAllText(Path.Combine(draftChapters, chKeep.FolderName, "scene-01.novalist"), "keep-draft");
        // Legacy snapshots folder at book root (moved into the draft).
        Directory.CreateDirectory(Path.Combine(bookRoot, "Snapshots"));
        File.WriteAllText(Path.Combine(bookRoot, "Snapshots", "snap.json"), "{}");
        // draft.json so LoadActiveDraftData restores the chapters.
        File.WriteAllText(Path.Combine(bookRoot, "Drafts", "default", "draft.json"),
            JsonSerializer.Serialize(new BookDraftData { Chapters = new() { chMerge, chKeep } }, opts));

        var svc = new ProjectService(new FileService());
        await svc.LoadProjectAsync(root);

        // Merge stub received the legacy file; Keep draft file left intact; snapshots moved.
        Assert.True(File.Exists(Path.Combine(draftChapters, chMerge.FolderName, "scene-01.novalist")));
        Assert.Equal("keep-draft",
            await File.ReadAllTextAsync(Path.Combine(draftChapters, chKeep.FolderName, "scene-01.novalist")));
        Assert.True(Directory.Exists(Path.Combine(bookRoot, "Drafts", "default", "Snapshots")));
    }
}
