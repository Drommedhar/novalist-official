using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// v2 to v3 migration: stamp scene files, write chapter markers, split acts.json,
/// build the per-draft index. Core regression guarantee is data safety — nothing
/// the user wrote is lost, reordered, or renumbered by the conversion.
/// </summary>
public class FilesystemMigratorTests
{
    private const string Root = @"C:\proj";

    /// <summary>Inline synchronous progress sink (System.Progress posts async — racy in tests).</summary>
    private sealed class CapturingProgress : IProgress<FilesystemMigrationProgress>
    {
        public readonly List<FilesystemMigrationProgress> Ticks = new();
        public void Report(FilesystemMigrationProgress value) => Ticks.Add(value);
    }

    private static T ReadJson<T>(InMemoryFileService fs, string path)
        => JsonSerializer.Deserialize<T>(fs.Files[path], new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static string P(params string[] parts) => Path.Combine(parts);

    /// <summary>
    /// Standard fixture with every plan §9 quirk: chapter NN gap (01, 02, 04),
    /// a chapter with no scene-01, an empty chapter, an archived scene, a dangling
    /// manifest entry, act strings on chapters while draft.json.acts is empty, and a
    /// scene whose body opens with a non-nv HTML comment.
    /// </summary>
    private static (V2ProjectBuilder builder, BookData book, BookDraftMetadata draft) BuildStandard()
    {
        var b = new V2ProjectBuilder();
        var book = b.AddBook("Frostschwur", "Frostschwur");
        var draft = b.AddDraft(book, "default");

        var ch1 = b.AddChapter(book, draft, "Arrival", "01 - Arrival", 1, act: "Act I", status: ChapterStatus.FirstDraft);
        b.AddScene(book, draft, ch1, "scene-01.novalist", "<p>Body one.</p>");
        b.AddScene(book, draft, ch1, "scene-02.novalist", "<!-- editor note -->\n<p>Body two.</p>");

        var ch2 = b.AddChapter(book, draft, "Middle", "02 - Middle", 2, act: "Act I");
        b.AddScene(book, draft, ch2, "scene-01.novalist", "<p>Mid.</p>");

        // Gap at 03; chapter "04" has no scene-01 (deleted earlier; allocator never backfills).
        var ch4 = b.AddChapter(book, draft, "Aftermath", "04 - Aftermath", 3, act: "Act II");
        b.AddScene(book, draft, ch4, "scene-02.novalist", "<p>After A.</p>");
        b.AddScene(book, draft, ch4, "scene-03.novalist", "<p>After B.</p>");
        b.AddSceneEntryOnly(book, draft, ch4, "scene-99.novalist"); // dangling: manifest entry, no file

        b.AddChapter(book, draft, "Empty", "05 - Empty", 4, act: "Act II"); // no scenes

        b.AddArchivedScene(book, draft, "scene-07.novalist", "<p>Archived.</p>");

        return (b, book, draft);
    }

    private static async Task<InMemoryFileService> MigrateStandardAsync(CapturingProgress? progress = null)
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root, progress);
        return fs;
    }

    private static string DraftRoot => P(Root, "Frostschwur", "Drafts", "default");

    // ── Gate ──

    [Fact]
    public async Task NeedsMigration_VersionBelow3_True()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        Assert.True(await new FilesystemMigrator(fs).NeedsMigrationAsync(builder.Project, Root));
    }

    [Fact]
    public async Task NeedsMigration_V3ButMissingIndex_True()
    {
        var (builder, _, _) = BuildStandard();
        builder.Project.Version = FilesystemMigrator.FilesystemVersion;
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        Assert.True(await new FilesystemMigrator(fs).NeedsMigrationAsync(builder.Project, Root));
    }

    [Fact]
    public async Task NeedsMigration_V3WithIndex_False()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        var migrator = new FilesystemMigrator(fs);
        await migrator.MigrateAsync(builder.Project, Root);
        Assert.False(await migrator.NeedsMigrationAsync(builder.Project, Root));
    }

    // ── Stamping ──

    [Fact]
    public async Task Stamping_ActiveScene_GetsFrontMatterWithSceneId()
    {
        var fs = await MigrateStandardAsync();
        var path = P(DraftRoot, "Chapters", "01 - Arrival", "scene-01.novalist");
        Assert.True(FileFrontMatter.TryParse(fs.Files[path], out var p));
        Assert.Equal("sc-01-Arrival-scene-01.novalist", p.Id);
    }

    [Fact]
    public async Task Stamping_ArchivedScene_IsStampedAndIndexed()
    {
        var fs = await MigrateStandardAsync();
        var path = P(DraftRoot, "Chapters", "__Archive", "scene-07.novalist");
        Assert.True(FileFrontMatter.HasFrontMatter(fs.Files[path]));
        var index = ReadJson<DraftIndex>(fs, P(DraftRoot, ".nvindex.json"));
        Assert.Contains("Chapters/__Archive/scene-07.novalist", index.Entries.Keys);
    }

    [Fact]
    public async Task Stamping_PreservesBodyExactly_IncludingLeadingComment()
    {
        var fs = await MigrateStandardAsync();
        var path = P(DraftRoot, "Chapters", "01 - Arrival", "scene-02.novalist");
        Assert.Equal("<!-- editor note -->\n<p>Body two.</p>", FileFrontMatter.Strip(fs.Files[path]));
    }

    [Fact]
    public async Task Stamping_Idempotent_NoDoubleStampOnSecondRun()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        var migrator = new FilesystemMigrator(fs);
        await migrator.MigrateAsync(builder.Project, Root);
        var afterFirst = fs.Files[P(DraftRoot, "Chapters", "01 - Arrival", "scene-01.novalist")];

        var result = await migrator.MigrateAsync(builder.Project, Root);
        var afterSecond = fs.Files[P(DraftRoot, "Chapters", "01 - Arrival", "scene-01.novalist")];

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(0, result.ScenesStamped);  // nothing re-stamped
        Assert.Equal(0, result.ChaptersMarked);  // markers already present
    }

    [Fact]
    public async Task Stamping_DanglingManifestEntry_NotStampedNoCrashNotPruned()
    {
        var fs = await MigrateStandardAsync();
        // No file was created for scene-99; migration must not invent one.
        Assert.False(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "04 - Aftermath", "scene-99.novalist")));
        // And the manifest entry survives (pruning is the reconciler's job, not migration's).
        var manifest = ReadJson<ScenesManifest>(fs, P(DraftRoot, "scenes.json"));
        Assert.Contains(manifest.Chapters["ch-04 - Aftermath"], s => s.FileName == "scene-99.novalist");
    }

    // ── Chapter markers ──

    [Fact]
    public async Task ChapterMarker_WrittenWithMetadata()
    {
        var fs = await MigrateStandardAsync();
        var marker = ReadJson<ChapterMarker>(fs, P(DraftRoot, "Chapters", "01 - Arrival", ".nvchapter.json"));
        Assert.Equal("ch-01 - Arrival", marker.Guid);
        Assert.Equal("Arrival", marker.Title);
        Assert.Equal("Act I", marker.Act);
        Assert.Equal(1, marker.Order);
        Assert.Equal(ChapterStatus.FirstDraft, marker.Status);
    }

    [Fact]
    public async Task ChapterMarker_EmptyChapter_StillMarked_NoIndexEntries()
    {
        var fs = await MigrateStandardAsync();
        Assert.True(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "05 - Empty", ".nvchapter.json")));
        var index = ReadJson<DraftIndex>(fs, P(DraftRoot, ".nvindex.json"));
        Assert.DoesNotContain(index.Entries.Keys, k => k.Contains("05 - Empty"));
    }

    [Fact]
    public async Task ChapterMarker_MissingFolder_IsCreated()
    {
        // Chapter present in draft.json but its folder/scenes never written to disk.
        var b = new V2ProjectBuilder();
        var book = b.AddBook("B", "B");
        var draft = b.AddDraft(book, "default");
        b.AddChapter(book, draft, "Ghost", "01 - Ghost", 1);  // no AddScene -> no folder written
        var fs = new InMemoryFileService();
        await b.WriteToAsync(fs, Root);

        await new FilesystemMigrator(fs).MigrateAsync(b.Project, Root);

        var markerPath = P(Root, "B", "Drafts", "default", "Chapters", "01 - Ghost", ".nvchapter.json");
        Assert.True(fs.Files.ContainsKey(markerPath));
    }

    [Fact]
    public async Task ChapterMarker_ExistingMarker_NotOverwritten()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        var markerPath = P(DraftRoot, "Chapters", "01 - Arrival", ".nvchapter.json");
        await fs.WriteTextAsync(markerPath, "{\"guid\":\"PRESERVE\"}");

        var result = await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root);

        Assert.Contains("PRESERVE", fs.Files[markerPath]); // left untouched
        Assert.Equal(3, result.ChaptersMarked); // the other 3 chapters still get markers
    }

    // ── Acts split ──

    [Fact]
    public async Task ActsJson_WrittenEmpty_WhenDraftHasNoActMetadata()
    {
        var fs = await MigrateStandardAsync();
        var acts = ReadJson<List<ActData>>(fs, P(DraftRoot, "acts.json"));
        Assert.Empty(acts); // chapters reference act strings but draft.json.acts == []
    }

    [Fact]
    public async Task ActsJson_PreservesExistingActMetadata()
    {
        var b = new V2ProjectBuilder();
        var book = b.AddBook("B", "B");
        var draft = b.AddDraft(book, "default");
        var ch = b.AddChapter(book, draft, "C", "01 - C", 1, act: "Act I");
        b.AddScene(book, draft, ch, "scene-01.novalist", "<p>x</p>");
        var fs = new InMemoryFileService();
        await b.WriteToAsync(fs, Root);
        // Inject existing act metadata into draft.json before migrating.
        var draftJsonPath = P(Root, "B", "Drafts", "default", "draft.json");
        var data = JsonSerializer.Deserialize<BookDraftData>(fs.Files[draftJsonPath])!;
        data.Acts.Add(new ActData { Name = "Act I", DateRange = new StoryDateRange { Start = "y1", End = "y2" } });
        fs.Files[draftJsonPath] = JsonSerializer.Serialize(data);

        await new FilesystemMigrator(fs).MigrateAsync(b.Project, Root);

        var acts = ReadJson<List<ActData>>(fs, P(Root, "B", "Drafts", "default", "acts.json"));
        Assert.Equal("Act I", Assert.Single(acts).Name);
    }

    // ── Index ──

    [Fact]
    public async Task Index_OneEntryPerSceneFile_HashMatchesStripped()
    {
        var fs = await MigrateStandardAsync();
        var index = ReadJson<DraftIndex>(fs, P(DraftRoot, ".nvindex.json"));

        // 5 real active files (ch1 x2, ch2 x1, ch4 x2) + 1 archived = 6. Dangling scene-99 excluded.
        Assert.Equal(6, index.Entries.Count);

        var path = P(DraftRoot, "Chapters", "01 - Arrival", "scene-01.novalist");
        var entry = index.Entries["Chapters/01 - Arrival/scene-01.novalist"];
        Assert.Equal(ContentHasher.Hash(fs.Files[path]), entry.Hash);
        Assert.Equal("sc-01-Arrival-scene-01.novalist", entry.Id);
        // Hash is over stripped content, so it equals the hash of the original raw body.
        Assert.Equal(ContentHasher.Hash("<p>Body one.</p>"), entry.Hash);
    }

    // ── Version ──

    [Fact]
    public async Task Version_SetTo3_AfterMigration()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root);
        Assert.Equal(3, builder.Project.Version);
    }

    // ── Multi-draft / multi-book + progress ──

    [Fact]
    public async Task MultiBookMultiDraft_AllMigrated_ProgressReported()
    {
        var b = new V2ProjectBuilder();
        var book1 = b.AddBook("One", "One");
        var d1a = b.AddDraft(book1, "default");
        var d1b = b.AddDraft(book1, "alt");
        var book2 = b.AddBook("Two", "Two");
        var d2 = b.AddDraft(book2, "default");
        foreach (var (bk, dr) in new[] { (book1, d1a), (book1, d1b), (book2, d2) })
        {
            var ch = b.AddChapter(bk, dr, "C", "01 - C", 1);
            b.AddScene(bk, dr, ch, "scene-01.novalist", "<p>x</p>");
        }
        var fs = new InMemoryFileService();
        await b.WriteToAsync(fs, Root);
        var progress = new CapturingProgress();

        var result = await new FilesystemMigrator(fs).MigrateAsync(b.Project, Root, progress);

        Assert.Equal(3, result.DraftsProcessed);
        Assert.True(fs.Files.ContainsKey(P(Root, "One", "Drafts", "alt", ".nvindex.json")));
        Assert.True(fs.Files.ContainsKey(P(Root, "Two", "Drafts", "default", ".nvindex.json")));
        // Progress: one tick per draft, non-decreasing, final tick reports the total.
        Assert.Equal(3, progress.Ticks.Count);
        Assert.Equal(3, progress.Ticks[^1].TotalDrafts);
        Assert.Equal(3, progress.Ticks[^1].DraftsProcessed);
        Assert.True(progress.Ticks.Zip(progress.Ticks.Skip(1)).All(p => p.Second.DraftsProcessed >= p.First.DraftsProcessed));
    }

    [Fact]
    public async Task MigrateDraft_MissingDraftAndScenesJson_NoThrow_WritesEmptyIndex()
    {
        var b = new V2ProjectBuilder();
        var book = b.AddBook("B", "B");
        var draft = b.AddDraft(book, "default");
        var fs = new InMemoryFileService();
        await b.WriteToAsync(fs, Root);
        // Simulate a draft folder with neither draft.json nor scenes.json.
        var draftRoot = P(Root, "B", "Drafts", "default");
        fs.Files.Remove(P(draftRoot, "draft.json"));
        fs.Files.Remove(P(draftRoot, "scenes.json"));

        await new FilesystemMigrator(fs).MigrateAsync(b.Project, Root);

        var index = ReadJson<DraftIndex>(fs, P(draftRoot, ".nvindex.json"));
        Assert.Empty(index.Entries);
    }

    [Fact]
    public async Task MigrateDraft_ManifestChapterMissingFromDraftJson_Skipped()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        // Inject a manifest chapter whose guid has no ChapterData in draft.json.
        var scenesPath = P(DraftRoot, "scenes.json");
        var manifest = ReadJson<ScenesManifest>(fs, scenesPath);
        manifest.Chapters["ghost-guid"] = new List<SceneData>
        {
            new() { Id = "ghost-scene", FileName = "scene-01.novalist", ChapterGuid = "ghost-guid", Order = 1 },
        };
        fs.Files[scenesPath] = JsonSerializer.Serialize(manifest);

        await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root);

        var index = ReadJson<DraftIndex>(fs, P(DraftRoot, ".nvindex.json"));
        Assert.DoesNotContain(index.Entries.Values, e => e.Id == "ghost-scene");
    }

    // ── Data-loss regression invariants ──

    [Fact]
    public async Task Regression_SceneCensusAndIdsUnchanged()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        var before = ReadJson<ScenesManifest>(fs, P(DraftRoot, "scenes.json"));
        var beforeIds = before.Chapters.Values.SelectMany(v => v).Concat(before.Archived).Select(s => s.Id).OrderBy(x => x).ToList();

        await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root);

        var after = ReadJson<ScenesManifest>(fs, P(DraftRoot, "scenes.json"));
        var afterIds = after.Chapters.Values.SelectMany(v => v).Concat(after.Archived).Select(s => s.Id).OrderBy(x => x).ToList();
        Assert.Equal(beforeIds, afterIds); // none minted, none dropped
    }

    [Fact]
    public async Task Regression_FolderPrefixGapPreserved()
    {
        var fs = await MigrateStandardAsync();
        // The 03 gap must remain; no renumber of 04/05 to close it.
        Assert.True(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "04 - Aftermath", ".nvchapter.json")));
        Assert.DoesNotContain(fs.Files.Keys, k => k.Contains("03 - "));
    }

    [Fact]
    public async Task Regression_SceneFilenameTokensPreserved()
    {
        var fs = await MigrateStandardAsync();
        // Chapter 04 keeps scene-02 / scene-03 and never gains a scene-01.
        Assert.True(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "04 - Aftermath", "scene-02.novalist")));
        Assert.True(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "04 - Aftermath", "scene-03.novalist")));
        Assert.False(fs.Files.ContainsKey(P(DraftRoot, "Chapters", "04 - Aftermath", "scene-01.novalist")));
    }

    [Fact]
    public async Task Regression_SceneOrderPreserved()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        await new FilesystemMigrator(fs).MigrateAsync(builder.Project, Root);

        var manifest = ReadJson<ScenesManifest>(fs, P(DraftRoot, "scenes.json"));
        var ch4 = manifest.Chapters["ch-04 - Aftermath"];
        Assert.Equal(new[] { 1, 2, 3 }, ch4.Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task Regression_SecondLoadProducesIdenticalSceneBytes()
    {
        var (builder, _, _) = BuildStandard();
        var fs = new InMemoryFileService();
        await builder.WriteToAsync(fs, Root);
        var migrator = new FilesystemMigrator(fs);
        await migrator.MigrateAsync(builder.Project, Root);
        var snapshot = fs.Files
            .Where(kv => kv.Key.EndsWith(".novalist"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        await migrator.MigrateAsync(builder.Project, Root);

        foreach (var (k, v) in snapshot)
            Assert.Equal(v, fs.Files[k]);
    }
}
