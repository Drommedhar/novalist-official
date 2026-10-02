using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class FolderImportServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    private readonly string _source;

    public FolderImportServiceTests()
    {
        _projects = new ProjectService(_files);
        _source = Path.Combine(_dir.Path, "source");
        Directory.CreateDirectory(_source);
    }

    public void Dispose() => _dir.Dispose();
    private Task OpenAsync() => _projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
    private void Write(string path, string text = "# Title\n\nSome **bold** prose.")
    {
        var full = Path.Combine(_source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
    private FolderImportService Scan(IFileService? files = null)
    {
        var service = new FolderImportService(_projects, files ?? _files);
        service.Scan(_source);
        return service;
    }
    private static async Task<FolderImportProgress> FinishAsync(FolderImportService service)
    {
        FolderImportProgress result;
        do { result = await service.ImportBatchAsync(); } while (!result.Done);
        return result;
    }

    [Fact]
    public async Task Scan_CountsOnlySupportedFilesAndKeepsParentFoldersWithoutReadingBodies()
    {
        await OpenAsync();
        Write("root.TXT");
        Write("World/Cast/Hero.MD");
        Write("World/place.markdown");
        Write("World/map.png");
        Write(".obsidian/state.md");
        Write(".git/nested/file.md");
        var probe = Substitute.For<IFileService>();
        var service = Scan(probe);
        Assert.Equal(3, service.Total);
        Assert.Equal(1, service.Unsupported);
        Assert.Equal(new ImportFolder("", 1, 3), service.Folders[0]);
        Assert.Contains(new ImportFolder("World", 1, 2), service.Folders);
        Assert.Contains(new ImportFolder("World/Cast", 1, 1), service.Folders);
        await probe.DidNotReceiveWithAnyArgs().ReadTextAsync(default!);
        Assert.Empty(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task ScanAndStart_RejectUnavailableFoldersProjectsAndInvalidRules()
    {
        var service = new FolderImportService(_projects, _files);
        Assert.Throws<InvalidOperationException>(() => service.Scan(_source));
        await OpenAsync();
        Assert.Throws<DirectoryNotFoundException>(() => service.Scan(Path.Combine(_source, "missing")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportBatchAsync());
        Write("World/one.md");
        service = Scan();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportBatchAsync());
        Assert.Throws<ArgumentException>(() => service.Configure("unknown", [], "Content"));
        Assert.Throws<ArgumentException>(() => service.Configure("skip", new() { ["missing"] = "research" }, "Content"));
        Assert.Throws<ArgumentException>(() => service.Configure("skip", new() { ["World"] = "unknown" }, "Content"));
        service.Configure("skip", [], "Content");
        Assert.Throws<InvalidOperationException>(() => service.Configure("research", [], "Content"));
        Assert.Equal(1, (await FinishAsync(service)).Skipped);
    }

    [Theory]
    [InlineData("", "research")]
    [InlineData("World", "character")]
    [InlineData("World/Cast/Heroes", "character")]
    [InlineData("World/Places", "location")]
    [InlineData("World/Places/Secret", "skip")]
    [InlineData("Worlds", "research")]
    public void FolderRules_InheritOnlyFromTheirAncestors(string folder, string expected)
    {
        Assert.Equal(expected, FolderImportService.ResolveTarget(folder, new Dictionary<string, string>
        {
            [""] = "research", ["World"] = "character", ["World/Places"] = "location", ["World/Places/Secret"] = "skip"
        }));
        Assert.Equal("skip", FolderImportService.ResolveTarget("unmapped", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task MixedFolders_CreateAllTargetsAndPreserveTitlesTagsAndContent()
    {
        await OpenAsync();
        _projects.CurrentProject!.CustomEntityTypes.Add(new CustomEntityTypeDefinition
            { TypeKey = "faction", DisplayName = "Faction", FolderName = "Factions" });
        Write("World/Cast/hero.md", "---\ntitle: Ada\ntags: [lead, World]\n---\n# Ada\n\nA brave navigator.");
        Write("World/Places/port.md", "# Harbour\n\nAn old port.");
        Write("Items/key.md", "# Key\n\nOpens the gate.");
        Write("Lore/custom.markdown", "# Legend\n\nA whispered story.");
        Write("Factions/union.md", "# Union\n\nThe northern alliance.");
        Write("Scenes/01.md", "# Arrival\n\nShe **entered** the harbour.\n\n<script>alert(1)</script>");
        Write("Scenes/02.txt", "# Literal heading\r\n<unsafe> & text");
        Write("Scenes/Nested/03.md", "# Departure\n\nThe ship left.");
        Write("note.txt", "An ordinary note.");
        Write("Skip/no.md");
        var service = Scan();
        service.Configure("research", new()
        {
            ["World"] = "character", ["World/Places"] = "location", ["Items"] = "item", ["Lore"] = "lore",
            ["Factions"] = "faction", ["Scenes"] = "scene", ["Skip"] = "skip"
        }, "Imported content", " archive ");
        var result = await FinishAsync(service);
        Assert.Equal(9, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        var entities = new EntityService(_projects);
        var character = Assert.Single(await entities.LoadCharactersAsync());
        Assert.Equal("Ada", character.Name);
        Assert.Contains("navigator", Assert.Single(character.Sections).Content);
        Assert.Equal("Imported content", character.Sections[0].Title);
        Assert.Contains("lead", character.Tags);
        Assert.Contains("archive", character.Tags);
        Assert.Equal(1, character.Tags.Count(tag => tag == "World"));
        Assert.Contains("old port", Assert.Single(await entities.LoadLocationsAsync()).Description);
        Assert.Contains("gate", Assert.Single(await entities.LoadItemsAsync()).Description);
        Assert.Contains("whispered", Assert.Single(await entities.LoadLoreAsync()).Description);
        Assert.Contains("alliance", Assert.Single((await entities.LoadCustomEntitiesAsync("faction"))[0].Sections).Content);
        Assert.Equal("An ordinary note.", Assert.Single(_projects.CurrentProject.ResearchItems).Content);
        var chapter = Assert.Single(_projects.ActiveBook!.Chapters, chapter => chapter.Title == "Scenes");
        var scenes = _projects.GetScenesForChapter(chapter.Guid);
        Assert.Equal(new[] { 1, 2 }, scenes.Select(scene => scene.Order));
        Assert.Equal("02", scenes[1].Title);
        var html = await _projects.ReadSceneContentAsync(chapter, scenes[0]);
        Assert.Contains("<strong>entered</strong>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;unsafe&gt; &amp; text", await _projects.ReadSceneContentAsync(chapter, scenes[1]));
        Assert.Contains("<br>", await _projects.ReadSceneContentAsync(chapter, scenes[1]));
        Assert.True(scenes[0].WordCount > 0);
        Assert.Contains("archive", scenes[0].AnalysisOverrides!.Tags!);
        Assert.True(FileFrontMatter.TryParse(File.ReadAllText(_projects.GetSceneFilePath(chapter, scenes[0])), out var stamp));
        Assert.Equal(scenes[0].Id, stamp.Id);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Assert.Equal(3, _projects.ScenesManifest!.Chapters.Values.Sum(scenes => scenes.Count));
        Assert.Equal(2, _projects.ActiveBook!.Chapters.Count);
    }

    [Fact]
    public async Task Batches_AreBoundedAndResumeWithoutDuplicatingOrRewritingEditedEntries()
    {
        await OpenAsync();
        for (var index = 0; index < 105; index++) Write($"Cast/{index:D3}.md");
        var service = Scan();
        service.Configure("character", [], "Imported");
        var first = await service.ImportBatchAsync();
        Assert.Equal(40, first.Processed);
        Assert.False(first.Done);
        var second = await service.ImportBatchAsync(0);
        Assert.Equal(41, second.Processed);
        var last = await service.ImportBatchAsync(999);
        Assert.True(last.Done);
        Assert.Equal(105, last.Imported);
        Assert.Equal(last, await service.ImportBatchAsync());
        var entities = new EntityService(_projects);
        var edited = (await entities.LoadCharactersAsync())[0];
        edited.Name = "Edited name";
        await entities.SaveCharacterAsync(edited);
        service = Scan();
        service.Configure("character", [], "Imported");
        last = await FinishAsync(service);
        Assert.Equal(0, last.Imported);
        Assert.Equal(105, last.Skipped);
        Assert.Contains(await entities.LoadCharactersAsync(), character => character.Name == "Edited name");
        var otherRoot = Path.Combine(_dir.Path, "another-source");
        Directory.CreateDirectory(Path.Combine(otherRoot, "Cast"));
        File.WriteAllText(Path.Combine(otherRoot, "Cast/000.md"), "# Different hero");
        service = new FolderImportService(_projects, _files);
        service.Scan(otherRoot);
        service.Configure("character", [], "Imported");
        Assert.Equal(1, (await FinishAsync(service)).Imported);
        Assert.Equal(106, (await entities.LoadCharactersAsync()).Count);
    }

    [Fact]
    public async Task RepeatSceneAndResearchImports_ReuseChaptersAndKeepArchivedScenes()
    {
        await OpenAsync();
        await _projects.CreateChapterAsync("Existing chapter");
        Write("Scenes/01.md");
        Write("research.md");
        var service = Scan();
        service.Configure("research", new() { ["Scenes"] = "scene" }, "Content");
        Assert.Equal(2, (await FinishAsync(service)).Imported);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Write("Scenes/02.md");
        service = Scan();
        service.Configure("research", new() { ["Scenes"] = "scene" }, "Content");
        var result = await FinishAsync(service);
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Skipped);
        var chapter = _projects.ActiveBook!.Chapters.Single(chapter => chapter.Title == "Scenes");
        Assert.Equal(2, chapter.Order);
        Assert.Equal(new[] { 1, 2 }, _projects.GetScenesForChapter(chapter.Guid).Select(scene => scene.Order));
        await _projects.ArchiveSceneAsync(chapter.Guid, _projects.GetScenesForChapter(chapter.Guid)[0].Id);
        service = Scan();
        service.Configure("research", new() { ["Scenes"] = "scene" }, "Content");
        Assert.Equal(3, (await FinishAsync(service)).Skipped);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task EntriesMovedToWorldBible_AreStillSkippedOnReimport()
    {
        await OpenAsync();
        Write("hero.md");
        var service = Scan();
        service.Configure("character", [], "Content");
        await FinishAsync(service);
        var entities = new EntityService(_projects);
        var hero = Assert.Single(await entities.LoadCharactersAsync());
        await _projects.InitializeWorldBibleAsync();
        await entities.MoveEntityToWorldBibleAsync(EntityType.Character, hero.Id);
        service = Scan();
        service.Configure("character", [], "Content");
        Assert.Equal(1, (await FinishAsync(service)).Skipped);
    }

    [Fact]
    public async Task UnreadableFiles_AreReportedWithoutAbortingAndIssueListIsBounded()
    {
        await OpenAsync();
        for (var index = 0; index < 12; index++) Write($"{index}.md");
        Write("readable.txt", "Good content");
        var probe = Substitute.For<IFileService>();
        probe.ReadTextAsync(Arg.Any<string>()).Returns(call =>
            call.Arg<string>().EndsWith("readable.txt") ? File.ReadAllTextAsync(call.Arg<string>())
                : Task.FromException<string>(new IOException("busy")));
        var service = Scan(probe);
        service.Configure("research", [], "Content", "");
        var result = await FinishAsync(service);
        Assert.Equal(12, result.Failed);
        Assert.Equal(1, result.Imported);
        Assert.Equal(10, result.Issues.Length);
        Assert.All(result.Issues, issue => Assert.Equal("read", issue.Kind));
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task FailedSceneWrite_DoesNotLeaveAnEmptyChapterOrSceneInTheModels()
    {
        await OpenAsync();
        Write("scene.md");
        var probe = Substitute.For<IFileService>();
        probe.ReadTextAsync(Arg.Any<string>()).Returns("# Scene\n\nProse");
        probe.CreateDirectoryAsync(Arg.Any<string>()).Returns(Task.FromException(new UnauthorizedAccessException()));
        var service = Scan(probe);
        service.Configure("scene", [], "Content");
        var result = await FinishAsync(service);
        Assert.Equal(1, result.Failed);
        Assert.Equal("write", Assert.Single(result.Issues).Kind);
        Assert.Empty(_projects.ActiveBook!.Chapters);
        Assert.Empty(_projects.ScenesManifest!.Chapters);
    }

    [Fact]
    public async Task BookOrDraftChanges_InvalidateTheImport()
    {
        await OpenAsync();
        Write("one.md");
        var service = Scan();
        service.Configure("research", [], "Content");
        var nextDraft = await _projects.CreateDraftAsync("Other");
        await _projects.SwitchDraftAsync(nextDraft.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportBatchAsync());
        service = Scan();
        var nextBook = await _projects.CreateBookAsync("Other book");
        await _projects.SwitchBookAsync(nextBook.Id);
        Assert.Throws<InvalidOperationException>(() => service.Configure("research", [], "Content"));
    }

    [Fact]
    public async Task FailedManifestSave_IsRetriedWhenContinuingTheImport()
    {
        await OpenAsync();
        Write("note.md");
        var proxy = Substitute.For<IProjectService>();
        proxy.IsProjectLoaded.Returns(true);
        proxy.ActiveBookRoot.Returns(_projects.ActiveBookRoot);
        proxy.ActiveDraftRoot.Returns(_projects.ActiveDraftRoot);
        proxy.ActiveBook.Returns(_projects.ActiveBook);
        proxy.CurrentProject.Returns(_projects.CurrentProject);
        proxy.ScenesManifest.Returns(_projects.ScenesManifest);
        proxy.SaveProjectAsync().Returns(_ => Task.FromException(new IOException("disk full")), _ => _projects.SaveProjectAsync());
        var service = new FolderImportService(proxy, _files);
        service.Scan(_source);
        service.Configure("research", [], "Content");
        await Assert.ThrowsAsync<IOException>(() => service.ImportBatchAsync());
        var result = await service.ImportBatchAsync();
        Assert.True(result.Done);
        Assert.Equal(1, result.Imported);
        await proxy.Received(2).SaveProjectAsync();
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task LargeDataset_ReadsOnlyTheCurrentBatchAndKeepsFolderCounts()
    {
        await OpenAsync();
        for (var folderIndex = 0; folderIndex < 100; folderIndex++)
        {
            var folder = Path.Combine(_source, $"Cast{folderIndex:D3}");
            Directory.CreateDirectory(folder);
            for (var fileIndex = 0; fileIndex < 100; fileIndex++)
                File.WriteAllText(Path.Combine(folder, $"{fileIndex:D3}.md"), "# Character\n\nProse.");
        }
        var probe = Substitute.For<IFileService>();
        probe.ReadTextAsync(Arg.Any<string>()).Returns(call => File.ReadAllTextAsync(call.Arg<string>()));
        var service = Scan(probe);
        Assert.Equal(10000, service.Total);
        Assert.Equal(101, service.Folders.Count);
        await probe.DidNotReceiveWithAnyArgs().ReadTextAsync(default!);
        service.Configure("character", [], "Content");
        var result = await service.ImportBatchAsync();
        Assert.Equal(40, result.Processed);
        Assert.Equal(40, result.Imported);
        Assert.False(result.Done);
        await probe.Received(40).ReadTextAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task EmptyFolder_CompletesWithoutWrites()
    {
        await OpenAsync();
        var service = Scan();
        Assert.Equal(0, service.Total);
        Assert.True(service.Configure("research", [], "Content").Done);
        Assert.True((await service.ImportBatchAsync()).Done);
    }

    [Fact]
    public async Task StructuredFiles_PopulateEveryTarget_AndInvalidJsonDoesNotStopTheBatch()
    {
        await OpenAsync();
        _projects.CurrentProject!.CustomEntityTypes.Add(new() { TypeKey = "faction", FolderName = "Factions", DefaultFields = [new() { Key = "members", DisplayName = "Members", Type = CustomPropertyType.Int }] });
        _projects.ActiveBook!.CharacterTemplates = [new() { Id = "people", Name = "People", CustomPropertyDefs = [new() { Key = "Skill" }] }];
        _projects.ActiveBook.ActiveCharacterTemplateId = "people";
        Write("Cast/ada.json", """
            {"novalistImport":1,"target":"character","data":{"name":"Ada","surname":"Lovelace","age":"32","eyeColor":"brown","customProperties":{"Skill":"Mathematics"}},"content":"A **biography**."}
            """);
        Write("Places/port.md", "---\nname: Harbour\ntype: Port\nparent: Coast\nisWorld: no\ndescription: An old harbour.\n---\n## History\nLong history.");
        Write("Items/key.txt", "Name: Key\nType: Tool\nOrigin: Smith\nDescription: Opens a gate.");
        Write("Lore/legend.md", "---\nname: Legend\ncategory: History\ngroup: Sailors\n---\nA whispered story.");
        Write("Factions/guild.md", "# Guild\nMembers: 42");
        Write("Scenes/arrival.json", """
            {"novalistImport":1,"target":"scene","data":{"title":"Arrival","pov":"Ada","synopsis":"Docking","tags":["lead"],"wordTarget":1000,"properties":{"Check":"Harbour maps"}},"content":"She **arrived**."}
            """);
        Write("Notes/source.md", "---\ntitle: Source\nstatus: resolved\nrating: 4\nunknown: retained\n---\nKept note.");
        Write("Cast/bad.json", "{broken");
        Write("Cast/wrong-target.json", "{\"novalistImport\":1,\"target\":\"location\",\"data\":{\"name\":\"Wrong\"}}");
        Write("novalist-import.schema.json", new FolderImportSchema().Export());
        var service = Scan();
        Assert.Equal(9, service.Total);
        Assert.Equal(0, service.Unsupported);
        service.Configure("skip", new() { ["Cast"] = "character", ["Places"] = "location", ["Items"] = "item", ["Lore"] = "lore", ["Factions"] = "faction", ["Scenes"] = "scene", ["Notes"] = "research" }, "Imported content", "archive");
        var result = await FinishAsync(service);
        Assert.Equal(7, result.Imported);
        Assert.Equal(2, result.Failed);
        Assert.All(result.Issues, issue => Assert.Equal("parse", issue.Kind));
        var entities = new EntityService(_projects);
        var character = Assert.Single(await entities.LoadCharactersAsync());
        Assert.Equal("Lovelace", character.Surname);
        Assert.Equal("32", character.Age);
        Assert.Equal("brown", character.EyeColor);
        Assert.Equal("people", character.TemplateId);
        Assert.Equal("Mathematics", character.CustomProperties["Skill"]);
        Assert.Equal("A **biography**.", Assert.Single(character.Sections).Content);
        Assert.EndsWith("ada.json", character.CustomProperties["importedFrom"]);
        var location = Assert.Single(await entities.LoadLocationsAsync());
        Assert.Equal("Port", location.Type);
        Assert.Equal("Coast", location.Parent);
        Assert.False(location.IsWorld);
        Assert.Equal("An old harbour.", location.Description);
        Assert.Contains(location.Sections, section => section.Title == "History");
        var item = Assert.Single(await entities.LoadItemsAsync());
        Assert.Equal("Tool", item.Type);
        Assert.Equal("Smith", item.Origin);
        Assert.Equal("Opens a gate.", item.Description);
        var lore = Assert.Single(await entities.LoadLoreAsync());
        Assert.Equal("History", lore.Category);
        Assert.Equal("Sailors", lore.Group);
        Assert.Equal("42", Assert.Single(await entities.LoadCustomEntitiesAsync("faction")).Fields["members"]);
        var chapter = Assert.Single(_projects.ActiveBook.Chapters);
        var scene = Assert.Single(_projects.GetScenesForChapter(chapter.Guid));
        Assert.Equal("Ada", scene.AnalysisOverrides!.Pov);
        Assert.Equal(["lead", "Scenes", "archive"], scene.AnalysisOverrides.Tags);
        Assert.Equal("Docking", scene.Synopsis);
        Assert.Equal(1000, scene.WordTarget);
        Assert.Equal("Harbour maps", scene.Properties!["Check"]);
        Assert.Contains("<strong>arrived</strong>", await _projects.ReadSceneContentAsync(chapter, scene));
        var research = Assert.Single(_projects.CurrentProject.ResearchItems);
        Assert.Equal(ResearchStatus.Resolved, research.Status);
        Assert.Equal(4, research.Rating);
        Assert.Equal("Kept note.", research.Content);
        Assert.Contains("unknown: retained", research.Properties!["importedMetadata"]);
    }
    [Fact]
    public async Task MarkdownImages_AreCopiedFromSourceAncestorsAndSharedAcrossEntriesWithoutOverwritingAssets()
    {
        await OpenAsync();
        var imageSource = Path.Combine(_dir.Path, "Images", "Characters", "portrait.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imageSource)!);
        File.WriteAllBytes(imageSource, [1, 2, 3, 4]);
        Write("Cast/one.md", "# One\n## Images\n- Portrait: ![[Images/Characters/portrait.png]]");
        Write("Cast/two.md", "# Two\n## Bilder\n![A portrait](<Images/Characters/portrait.png>)");
        var existingAsset = Path.Combine(_projects.ActiveBookRoot!, "Images", "Characters", "portrait.png");
        Directory.CreateDirectory(Path.GetDirectoryName(existingAsset)!);
        File.WriteAllBytes(existingAsset, [9, 9]);
        var service = Scan();
        service.Configure("character", [], "Source");
        Assert.Equal(2, (await FinishAsync(service)).Imported);
        var characters = await new EntityService(_projects).LoadCharactersAsync();
        Assert.Equal(2, characters.Count);
        var images = characters.Select(character => Assert.Single(character.Images)).ToArray();
        Assert.Equal(images[0].Path, images[1].Path);
        Assert.StartsWith("Images/Imported/", images[0].Path);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(_projects.ActiveBookRoot!, images[0].Path)));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(existingAsset));
        Assert.Single(Directory.GetFiles(Path.Combine(_projects.ActiveBookRoot!, "Images", "Imported")));
        // The next import can reuse the asset even when the entity was removed.
        foreach (var character in characters) File.Delete(Path.Combine(_projects.ActiveBookRoot!, "Characters", character.Id + ".json"));
        service = Scan();
        service.Configure("character", [], "Source");
        Assert.Equal(2, (await FinishAsync(service)).Imported);
        Assert.Single(Directory.GetFiles(Path.Combine(_projects.ActiveBookRoot!, "Images", "Imported")));
    }

    [Fact]
    public async Task MissingImages_KeepSourceReferences_AndExistingBookImagesCanBeReused()
    {
        await OpenAsync();
        var existing = Path.Combine(_projects.ActiveBookRoot!, "Images", "existing.png");
        File.WriteAllBytes(existing, [1, 2]);
        var source = "# One\n## Images\n![[Images/existing.png]]\n![[Images/missing.png]]\n![[../private.png]]\n![[https://example.test/a.png]]\n![[/outside.png]]\n![[secret.txt]]";
        Write("one.md", source);
        var service = Scan();
        service.Configure("character", [], "Source");
        var result = await FinishAsync(service);
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Failed);
        var character = Assert.Single(await new EntityService(_projects).LoadCharactersAsync());
        Assert.Equal("Images/existing.png", Assert.Single(character.Images).Path);
        Assert.Equal(source, Assert.Single(character.Sections).Content);
    }

    [Fact]
    public async Task ImagesRelativeToTheDocument_AreResolvedWithoutSearchingUnrelatedDirectories()
    {
        await OpenAsync();
        Write("Cast/one.md", "# One\n## Images\n![[portrait.png]]");
        File.WriteAllBytes(Path.Combine(_source, "Cast", "portrait.png"), [1, 2]);
        var service = Scan();
        service.Configure("character", [], "Source");
        Assert.Equal(1, (await FinishAsync(service)).Imported);
        Assert.Single(Assert.Single(await new EntityService(_projects).LoadCharactersAsync()).Images);
    }
}
