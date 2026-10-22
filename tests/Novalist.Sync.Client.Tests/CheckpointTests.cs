using System.IO.Compression;
using System.Text.Json;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class CheckpointTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "novalist-checkpoint-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MultipleProseFieldsInOneEntryKeepDistinctEditingHistoriesAfterJoining()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "prose-owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        Directory.CreateDirectory(Path.Combine(source, "World Bible/Locations"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"), """{"books":[],"worldBibleFolder":"World Bible"}""", ct);
        const string path = "World Bible/Locations/one.json";
        await File.WriteAllTextAsync(Path.Combine(source, path), """{"id":"one","description":"Place","sections":[{"id":"history","title":"History","content":"Long ago"}]}""", ct);
        SharedDocumentState[] documents = [
            new(Guid.NewGuid(), path, [0, 0]) { TextField = new("description") },
            new(Guid.NewGuid(), path, [0, 0]) { TextField = new("content", "history") }
        ];
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => ProjectContentSelection.Read(source), documents, ct);
        archive.Position = 0;
        var state = await ProjectCheckpoint.InstallAsync(archive, Path.Combine(root, "prose-guest"), 1024 * 1024, ct);
        Assert.Equal(documents.Select(item => item.TextField), state.Documents.Select(item => item.TextField));
        Assert.Equal(documents.Select(item => item.ItemId), state.Documents.Select(item => item.ItemId));
    }

    [Fact]
    public async Task FieldBindingsSurviveJoiningAndDistinguishBooksFromTheWorldBible()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = new ProjectService(new FileService());
        await project.CreateProjectAsync(root, "Shared codex", "First");
        var entities = new EntityService(project);
        var id = Guid.NewGuid().ToString();
        await entities.SaveCharacterAsync(new() { Id = id, Name = "A", Age = "42" });
        await entities.SaveCharacterAsync(new() { Id = id, Name = "World A", Age = "50", IsWorldBible = true });
        var second = await project.CreateBookAsync("Second");
        await project.SwitchBookAsync(second.Id);
        await entities.SaveCharacterAsync(new() { Id = id, Name = "Other A", Age = "20" });
        await project.SaveProjectAsync();
        var source = project.ProjectRoot ?? throw new InvalidOperationException();
        var paths = ProjectContentSelection.Read(source);
        var fields = await SharedCodexFields.CaptureAsync(source, paths, ct);
        var ages = fields.Where(field => field.Key == "age").ToArray();
        Assert.Equal(3, ages.Length);
        Assert.Equal(3, ages.Select(field => field.ItemId).Distinct().Count());
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => paths, new SharedProjectState([], fields), ct);
        archive.Position = 0;
        var destination = Path.Combine(root, "Joined codex");
        var restored = await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        Assert.Equal(fields, restored.Fields);
        var values = await SharedCodexFields.ReadAsync(destination, restored.Fields, ct);
        Assert.Equal(new[] { "20", "42", "50" }, ages.Select(field => values[field.ItemId]).Order(StringComparer.Ordinal));
        Assert.Equal(second.Id, project.ActiveBook?.Id);
    }

    [Fact]
    public async Task CustomFieldKeysRemainLiteralAndCannotRedirectProjectStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        Directory.CreateDirectory(Path.Combine(source, "World Bible/Factions"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"),
            """{"books":[],"worldBibleFolder":"World Bible","customEntityTypes":[{"typeKey":"faction","folderName":"Factions","defaultFields":[]}]}""", ct);
        await File.WriteAllTextAsync(Path.Combine(source, "World Bible/Factions/one.json"),
            """{"id":"one","name":"Faction","fields":{"Age":"old","age":"new","path":"../metaphor"}}""", ct);
        var fields = await SharedCodexFields.CaptureAsync(source, ProjectContentSelection.Read(source), ct);
        var values = await SharedCodexFields.ReadAsync(source, fields, ct);
        Assert.Equal("old", values[Assert.Single(fields, field => field.Key == "Age").ItemId]);
        Assert.Equal("new", values[Assert.Single(fields, field => field.Key == "age").ItemId]);
        Assert.Equal("../metaphor", values[Assert.Single(fields, field => field.Key == "path").ItemId]);
        Assert.All(fields.Where(field => field.Key != "name"), field => Assert.True(field.Custom));
        var invalid = Assert.Single(fields, field => field.Key == "path") with { Custom = false };
        await Assert.ThrowsAsync<InvalidDataException>(() => SharedCodexFields.ReadAsync(source, [invalid], ct));
    }

    [Fact]
    public async Task InitialTransferKeepsProjectContentButExcludesCredentialsAndPersonalPreferences()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"),
            """{"books":[],"worldBibleFolder":"World Bible"}""", ct);
        const string settings = """
            {"author":"Shared author","wordsPerPage":320,"disabledContinuityRules":["example"],
             "overrides":{"grammarCheckApiKey":"private-key","grammarCheckUsername":"private-account",
               "grammarCheckApiUrl":"https://private.example/?token=secret","theme":"personal-theme","bookFontSize":11},
             "wordCountGoals":{"projectGoal":80000,"dailyGoal":500,"dailyBaselineWords":1234},
             "viewState":{"isExplorerVisible":false},"sprints":[{"notes":"personal-sprint"}],"futureCredential":"private-future"}
            """;
        var settingsPath = Path.Combine(source, ".novalist/settings.json");
        await File.WriteAllTextAsync(settingsPath, settings, ct);
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/word-history.jsonl"), "private-writing-history", ct);
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => ProjectContentSelection.Read(source), ct);
        archive.Position = 0;
        var destination = Path.Combine(root, "guest");
        await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        var shared = await File.ReadAllTextAsync(Path.Combine(destination, ".novalist/settings.json"), ct);

        Assert.DoesNotContain("private-", shared);
        Assert.DoesNotContain("personal-", shared);
        using var json = JsonDocument.Parse(shared);
        Assert.Equal("Shared author", json.RootElement.GetProperty("author").GetString());
        Assert.Equal(320, json.RootElement.GetProperty("wordsPerPage").GetInt32());
        Assert.Equal(11, json.RootElement.GetProperty("overrides").GetProperty("bookFontSize").GetInt32());
        Assert.Equal(80000, json.RootElement.GetProperty("wordCountGoals").GetProperty("projectGoal").GetInt32());
        Assert.False(json.RootElement.GetProperty("wordCountGoals").TryGetProperty("dailyGoal", out _));
        Assert.False(json.RootElement.TryGetProperty("viewState", out _));
        Assert.False(File.Exists(Path.Combine(destination, ".novalist/word-history.jsonl")));
        Assert.Equal(settings, await File.ReadAllTextAsync(settingsPath, ct));
    }

    [Theory]
    [InlineData("Book")]
    [InlineData("Research")]
    public async Task InitialProjectTransferExcludesLocalSnapshotsFromEveryDraft(string bookName)
    {
        var ct = TestContext.Current.CancellationToken;
        var project = new ProjectService(new FileService());
        await project.CreateProjectAsync(root, "Shared project", bookName);
        var book = Assert.IsType<Novalist.Core.Models.BookData>(project.ActiveBook);
        var second = await project.CreateDraftAsync("Second");
        var localSnapshot = Path.Combine(project.ActiveBookRoot!, "Drafts", second.FolderName, book.SnapshotFolder, "private.novalist");
        await File.WriteAllTextAsync(localSnapshot, "Local snapshot", ct);
        var relative = Path.GetRelativePath(project.ProjectRoot!, localSnapshot).Replace('\\', '/');

        Assert.DoesNotContain(relative, ProjectContentSelection.Read(project.ProjectRoot!));
    }

    [Fact]
    public async Task SharedProjectSelectionKeepsWorldBibleRevisionsLocal()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = new ProjectService(new FileService());
        await project.CreateProjectAsync(root, "Shared project", "Book");
        var history = EntityHistory.ForEntity(project, worldBible: true);
        await history.RecordAsync("world-character", "{\"age\":\"42\"}", "{\"age\":\"43\"}");
        Assert.Single(await history.ListAsync("world-character"));
        var files = ProjectContentSelection.Read(project.ProjectRoot!);
        Assert.DoesNotContain(files, path => path.StartsWith(project.CurrentProject!.WorldBibleFolder + "/Snapshots/", StringComparison.Ordinal));
        using var checkpoint = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(project.ProjectRoot!, checkpoint, () => files, [], ct);
        checkpoint.Position = 0;
        var destination = Path.Combine(root, "joined");
        await ProjectCheckpoint.InstallAsync(checkpoint, destination, long.MaxValue, ct);
        Assert.False(Directory.Exists(Path.Combine(destination, project.CurrentProject!.WorldBibleFolder, "Snapshots")));
    }

    [Fact]
    public async Task ASceneChangedAfterDocumentCaptureCannotBePublishedWithStaleSharedState()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        Directory.CreateDirectory(Path.Combine(source, "Book"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"), "{}", ct);
        var path = Path.Combine(source, "Book/scene.novalist");
        await File.WriteAllTextAsync(path, "<p>Original.</p>", ct);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
        var state = new SharedDocumentState(Guid.NewGuid(), "Book/scene.novalist", [1, 2, 3]) { SourceSha256 = hash };
        await File.WriteAllTextAsync(path, "<p>New writing.</p>", ct);
        using var archive = new MemoryStream();

        await Assert.ThrowsAsync<IOException>(() => ProjectCheckpoint.CreateAsync(source, archive,
            () => [".novalist/project.json", "Book/scene.novalist"], [state], ct));

        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        Assert.Null(zip.GetEntry("manifest.json"));
    }

    [Fact]
    public async Task CheckpointReturnsSharedDocumentIdentitiesAlongsideTheLocalFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        Directory.CreateDirectory(Path.Combine(source, "Book"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"), "{}", ct);
        await File.WriteAllTextAsync(Path.Combine(source, "Book/scene.html"), "<p>Story.</p>", ct);
        var state = new SharedDocumentState(Guid.NewGuid(), "Book/scene.html", [1, 2, 3, 4]);
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => [".novalist/project.json", "Book/scene.html"], [state], ct);
        archive.Position = 0;
        var destination = Path.Combine(root, "guest");

        var restored = Assert.Single((await ProjectCheckpoint.InstallAsync(archive, destination, 1024, ct)).Documents);

        Assert.Equal(state.ItemId, restored.ItemId);
        Assert.Equal(state.Path, restored.Path);
        Assert.Equal(state.State, restored.State);
        Assert.Equal("<p>Story.</p>", await File.ReadAllTextAsync(Path.Combine(destination, state.Path), ct));
    }

    [Fact]
    public async Task TransferredProjectReopensThroughNovalistsNormalProjectService()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = new ProjectService(new FileService());
        var project = await owner.CreateProjectAsync(root, "Shared project", "First book");
        var chapter = await owner.CreateChapterAsync("Opening");
        var scene = await owner.CreateSceneAsync(chapter.Guid, "Arrival");
        await owner.WriteSceneContentAsync(chapter, scene, "<p>A <b>shared</b> story.</p>");
        var secondDraft = await owner.CreateDraftAsync("Revised draft", owner.ActiveBook?.ActiveDraftId);
        await owner.SaveProjectAsync();
        var source = owner.ProjectRoot ?? throw new InvalidOperationException();
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => ProjectContentSelection.Read(source), ct);
        archive.Position = 0;
        var destination = Path.Combine(root, "Collaborator copy");
        await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        var guest = new ProjectService(new FileService());
        var loaded = await guest.LoadProjectAsync(destination);
        Assert.Equal(project.Id, loaded.Id);
        Assert.Contains(loaded.Books[0].Drafts, draft => draft.Id == secondDraft.Id);
        await guest.SwitchDraftAsync("draft-default");
        Assert.Equal("<p>A <b>shared</b> story.</p>", await guest.ReadSceneContentAsync(chapter, scene));
        Assert.Equal("<p>A <b>shared</b> story.</p>", await owner.ReadSceneContentAsync(chapter, scene));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSharedStateNeverPublishesAProject(bool exceedQuota)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"), "{}", ct);
        var document = new SharedDocumentState(Guid.NewGuid(), ".novalist/project.json", [1, 2, 3, 4]);
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => [".novalist/project.json"], [document], ct);
        if (!exceedQuota)
        {
            archive.Position = 0;
            using var zip = new ZipArchive(archive, ZipArchiveMode.Update, leaveOpen: true);
            var entry = zip.GetEntry($"state/{document.ItemId:N}") ?? throw new InvalidOperationException();
            await using var content = entry.Open();
            await content.WriteAsync(new byte[] { 4, 3, 2, 1 }, ct);
        }
        archive.Position = 0;
        var destination = Path.Combine(root, "guest");

        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, destination, exceedQuota ? 5 : 1024, ct));

        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(root, ".novalist-join-*"));
    }

    [Fact]
    public async Task JoinInstallsAllSelectedProjectContentIntoANewUsableLocalCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(root, "owner");
        var destination = Path.Combine(root, "guest");
        string[] paths = [".novalist/project.json", "First Book/Drafts/one/scene.novalist", "Second Book/Drafts/two/scene.novalist", "World Bible/Characters/Bea.json", "Research/map.png"];
        foreach (var path in paths)
        {
            var full = Path.Combine(source, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? throw new InvalidOperationException());
            await File.WriteAllTextAsync(full, path.EndsWith(".json") ? "{}" : "Shared project content", ct);
        }
        await File.WriteAllTextAsync(Path.Combine(source, "private.txt"), "Not selected", ct);
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"),
            """{"books":[{"folderName":"First Book"},{"folderName":"Second Book"}],"worldBibleFolder":"World Bible"}""", ct);
        Directory.CreateDirectory(Path.Combine(source, ".novalist/extensions/private"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/extensions/private/key.txt"), "Private extension configuration", ct);
        using var archive = new MemoryStream();
        await ProjectCheckpoint.CreateAsync(source, archive, () => ProjectContentSelection.Read(source), ct);
        archive.Position = 0;
        await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        foreach (var path in paths)
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(source, path), ct), await File.ReadAllBytesAsync(Path.Combine(destination, path), ct));
        Assert.False(File.Exists(Path.Combine(destination, "private.txt")));
        Assert.False(Directory.Exists(Path.Combine(destination, ".novalist/extensions")));
        archive.Position = 0;
        await Assert.ThrowsAsync<IOException>(() => ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("folder\\escaped.txt")]
    [InlineData("folder/file:stream")]
    [InlineData("folder/CON.txt")]
    [InlineData(".git/hooks/post-checkout")]
    public async Task UnsafeCheckpointPathsNeverCreateADestination(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = zip.CreateEntry("manifest.json").Open();
            await JsonSerializer.SerializeAsync(entry, new CheckpointManifest(1, [new(path, 1, new string('0', 64))]), cancellationToken: ct);
        }
        archive.Position = 0;
        var destination = Path.Combine(root, "guest");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, destination, 1024, ct));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task IncorrectHashesDoNotPublishAPartialProject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var manifest = zip.CreateEntry("manifest.json").Open())
                await JsonSerializer.SerializeAsync(manifest, new CheckpointManifest(1, [new(".novalist/project.json", 2, new string('0', 64))]), cancellationToken: ct);
            await using var data = zip.CreateEntry("files/.novalist/project.json").Open();
            await data.WriteAsync("{}"u8.ToArray(), ct);
        }
        archive.Position = 0;
        var destination = Path.Combine(root, "guest");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, destination, 1024, ct));
        Assert.False(Directory.Exists(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
