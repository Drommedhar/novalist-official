using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class SceneAnalysisStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _project;
    private readonly SceneAnalysisStore _store;

    public SceneAnalysisStoreTests()
    {
        _project = new ProjectService(_files);
        _store = new SceneAnalysisStore(_project, _files);
    }

    public void Dispose() => _dir.Dispose();

    private static SceneAnalysisRecord Record(string sceneId = "s1") => new()
    {
        SceneId = sceneId,
        ChapterGuid = "c1",
        ChapterTitle = "Chapter One",
        SceneTitle = "Arrival",
        ModelId = "gemma",
        Entities =
        [
            new() { Name = "Amy Calder", EntityId = "hero", EntityType = "character",
                    Presence = ScenePresence.Present, Note = "Meets Mina." },
            new() { Name = "Dana Harrow", EntityType = "character", Presence = ScenePresence.Mentioned }
        ],
        Characters =
        [
            new() { CharacterId = "hero", Name = "Amy Calder", Presence = ScenePresence.Present,
                    Observed = ["The playground is full."], Emotion = "nervous" }
        ],
        Findings = [new() { Type = "reference", Title = "Amy meets Mina", EntityName = "Amy Calder" }]
    };

    [Fact]
    public void ComputeSceneHash_IsStableAndSensitive()
    {
        Assert.Equal(SceneAnalysisStore.ComputeSceneHash("abc"), SceneAnalysisStore.ComputeSceneHash("abc"));
        Assert.NotEqual(SceneAnalysisStore.ComputeSceneHash("abc"), SceneAnalysisStore.ComputeSceneHash("abd"));
        // Null and empty are the same thing for hashing purposes.
        Assert.Equal(SceneAnalysisStore.ComputeSceneHash(null), SceneAnalysisStore.ComputeSceneHash(""));
    }

    [Fact]
    public async Task Read_NullWhenNoProjectLoaded()
        => Assert.Null(await _store.ReadAsync("s1"));

    [Fact]
    public async Task Write_NoProject_IsNoOp()
    {
        await _store.WriteAsync(Record(), "text");     // must not throw
        Assert.Null(await _store.ReadAsync("s1"));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsEverything()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        await _store.WriteAsync(Record(), "the scene text");

        var read = await _store.ReadAsync("s1");
        Assert.NotNull(read);
        Assert.Equal("Arrival", read!.SceneTitle);
        Assert.Equal("gemma", read.ModelId);
        Assert.Equal(SceneAnalysisRecord.CurrentSchemaVersion, read.SchemaVersion);
        Assert.Equal(SceneAnalysisStore.ComputeSceneHash("the scene text"), read.SceneContentHash);
        Assert.NotEmpty(read.GeneratedAt);

        // Entities keep their resolution and three-way presence.
        Assert.Equal(2, read.Entities.Count);
        Assert.Equal("hero", read.Entities[0].EntityId);
        Assert.Equal(ScenePresence.Present, read.Entities[0].Presence);
        Assert.Null(read.Entities[1].EntityId);          // unresolved name is kept, not dropped
        Assert.Equal(ScenePresence.Mentioned, read.Entities[1].Presence);

        var knowledge = Assert.Single(read.Characters);
        Assert.Equal("nervous", knowledge.Emotion);
        Assert.Equal(["The playground is full."], knowledge.Observed);
        Assert.Equal("Amy meets Mina", Assert.Single(read.Findings).Title);
    }

    [Fact]
    public async Task Write_OneScenePerFile_LeavesSiblingsAlone()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        await _store.WriteAsync(Record("s1"), "one");
        await _store.WriteAsync(Record("s2"), "two");

        // Rewriting s1 must not disturb s2 — that is the point of per-scene files.
        await _store.WriteAsync(Record("s1"), "one edited");
        Assert.Equal(SceneAnalysisStore.ComputeSceneHash("two"), (await _store.ReadAsync("s2"))!.SceneContentHash);
        Assert.Equal(SceneAnalysisStore.ComputeSceneHash("one edited"), (await _store.ReadAsync("s1"))!.SceneContentHash);
    }

    [Fact]
    public async Task IsStale_NeverAnalysed_ChangedText_AndOldSchema()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");

        Assert.True(await _store.IsStaleAsync("s1", "text"));       // never analysed

        await _store.WriteAsync(Record(), "text");
        Assert.False(await _store.IsStaleAsync("s1", "text"));      // unchanged
        Assert.True(await _store.IsStaleAsync("s1", "edited"));     // text changed

        // A record written under an older schema is re-analysed even if unchanged.
        var stale = Record();
        await _store.WriteAsync(stale, "text");
        var onDisk = await _store.ReadAsync("s1");
        onDisk!.SchemaVersion = SceneAnalysisRecord.CurrentSchemaVersion - 1;
        await _files.WriteTextAsync(
            _files.CombinePath(_project.ProjectRoot!, ".novalist", "analysis", "s1.json"),
            System.Text.Json.JsonSerializer.Serialize(onDisk,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));
        Assert.True(await _store.IsStaleAsync("s1", "text"));
    }

    [Fact]
    public async Task GetStaleSceneIds_ReturnsOnlyWhatChanged()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        await _store.WriteAsync(Record("s1"), "one");
        await _store.WriteAsync(Record("s2"), "two");

        var stale = await _store.GetStaleSceneIdsAsync(
            [("s1", "one"), ("s2", "two edited"), ("s3", "brand new")]);

        Assert.Equal(["s2", "s3"], stale);
    }

    [Fact]
    public async Task Read_CorruptFile_TreatedAsNeverAnalysed()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var dir = _files.CombinePath(_project.ProjectRoot!, ".novalist", "analysis");
        await _files.CreateDirectoryAsync(dir);
        await _files.WriteTextAsync(_files.CombinePath(dir, "s1.json"), "{ not json");

        Assert.Null(await _store.ReadAsync("s1"));
        Assert.True(await _store.IsStaleAsync("s1", "text"));
    }

    [Fact]
    public async Task Clear_RemovesTheRecord_AndIsSafeWhenAbsent()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        await _store.ClearAsync("never-written");   // must not throw

        await _store.WriteAsync(Record(), "text");
        await _store.ClearAsync("s1");
        Assert.Null(await _store.ReadAsync("s1"));
    }

    [Fact]
    public async Task BlankSceneId_IsIgnoredRatherThanWritingAStrayFile()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        await _store.WriteAsync(Record(" "), "text");
        Assert.Null(await _store.ReadAsync(" "));
        await _store.ClearAsync(" ");               // must not throw
    }
}
