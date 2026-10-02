using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class StructuredImportServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    public StructuredImportServiceTests() => _projects = new(_files);
    public void Dispose() => _dir.Dispose();
    private async Task<StructuredImportService> OpenAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
        return NewService();
    }
    private StructuredImportService NewService() => new(_projects, _files, "Source writing");
    private static StructuredImportEntry Entry(string source, string target = "research", string data = "\"title\":\"Note\"", string? folder = null)
        => new(source, JsonSerializer.Deserialize<JsonElement>($$"""{"novalistImport":1,"target":"{{target}}","data":{ {{data}} },"content":"Preserved **writing**."}"""), folder);

    [Fact]
    public async Task RequiresAnOpenDestinationAndBoundedBatchesWithUsableSourceIds()
    {
        Assert.Throws<InvalidOperationException>(() => NewService());
        var service = await OpenAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(Enumerable.Range(0, 41).Select(i => Entry(i.ToString())).ToArray()));
        var invalid = await service.ImportAsync([
            Entry(null!), Entry(" "), Entry(new string('x', 2049)), Entry("long-folder", folder: new string('x', 257)),
            new("bad-shape", JsonSerializer.Deserialize<JsonElement>("[]")),
            new("no-target", JsonSerializer.Deserialize<JsonElement>("{}")),
            new("numeric-target", JsonSerializer.Deserialize<JsonElement>("{\"target\":1}")),
            Entry("unknown-type", "missing"), Entry("missing-name", "character", "\"surname\":\"North\"")
        ]);
        Assert.All(invalid, result => Assert.Equal("invalid", result.Status));
        Assert.All(invalid, result => Assert.NotNull(result.Error));
        Assert.Empty(_projects.CurrentProject!.ResearchItems);
        Assert.Empty(await new EntityService(_projects).LoadCharactersAsync());
    }

    [Fact]
    public async Task ValidationAndMixedImportsUseCurrentCustomDefinitionsAndPreserveTypedProperties()
    {
        var service = await OpenAsync();
        _projects.ActiveBook!.CharacterTemplates = [new() { Id = "people", Name = "People", CustomPropertyDefs = [new() { Key = "Skill" }, new() { Key = "Alive", Type = CustomPropertyType.Bool }] }];
        _projects.ActiveBook.ActiveCharacterTemplateId = "people";
        _projects.CurrentProject!.CustomEntityTypes.Add(new() { TypeKey = "faction", FolderName = "Factions", DefaultFields = [new() { Key = "strength", Type = CustomPropertyType.Int, Required = true }] });
        _projects.ActiveBook.ManuscriptProperties.Add(new() { Key = "Checked", Type = CustomPropertyType.Bool, Scope = ManuscriptPropertyScope.Research });
        var entries = new[]
        {
            Entry("cast/ada.md", "character", "\"name\":\"Ada\",\"surname\":\"Lovelace\",\"age\":\"32\",\"customProperties\":{\"Skill\":\"Mathematics\",\"Alive\":\"true\"}"),
            Entry("places/port.md", "location", "\"name\":\"Harbour\",\"type\":\"Port\""),
            Entry("items/ship.md", "item", "\"name\":\"Ship\",\"origin\":\"Harbour\""),
            Entry("lore/tides.md", "lore", "\"name\":\"Tides\",\"category\":\"Nature\""),
            Entry("groups/guild.md", "faction", "\"name\":\"Guild\",\"fields\":{\"strength\":\"42\"}"),
            Entry("scenes/arrival.md", "scene", "\"title\":\"Arrival\",\"synopsis\":\"Ada arrives.\",\"intensity\":7", "Chapter 1"),
            Entry("notes/route.md", data: "\"title\":\"Route\",\"rating\":4,\"properties\":{\"Checked\":\"true\"}")
        };
        Assert.Contains("\"Alive\"", service.Schema().ExportTarget("character"));
        Assert.Throws<ArgumentException>(() => service.Schema().ExportTarget("unknown"));
        var standalone = JsonNode.Parse(service.Schema().ExportTarget("faction"))!;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", standalone["$schema"]!.GetValue<string>());
        Assert.Equal("faction", standalone["properties"]!["target"]!["const"]!.GetValue<string>());
        Assert.All(await service.ImportAsync(entries, true), result => { Assert.Equal("valid", result.Status); Assert.Null(result.Id); });
        Assert.Empty(await new EntityService(_projects).LoadCharactersAsync());
        Assert.Empty(_projects.CurrentProject.ResearchItems);
        Assert.Empty(_projects.ActiveBook.Chapters);
        Assert.All(await service.ImportAsync(entries), result => { Assert.Equal("imported", result.Status); Assert.NotNull(result.Id); });
        var entity = Assert.Single(await new EntityService(_projects).LoadCharactersAsync());
        Assert.Equal("Lovelace", entity.Surname);
        Assert.Equal("32", entity.Age);
        Assert.Equal("people", entity.TemplateId);
        Assert.Equal("Mathematics", entity.CustomProperties!["Skill"]);
        Assert.Equal("true", entity.CustomProperties["Alive"]);
        Assert.Equal("cast/ada.md", entity.CustomProperties["importedFrom"]);
        Assert.Equal("Preserved **writing**.", Assert.Single(entity.Sections).Content);
        Assert.Equal("42", Assert.Single(await new EntityService(_projects).LoadCustomEntitiesAsync("faction")).Fields["strength"]);
        var chapter = Assert.Single(_projects.ActiveBook.Chapters);
        var scene = Assert.Single(_projects.GetScenesForChapter(chapter.Guid));
        Assert.Equal("Chapter 1", chapter.Title);
        Assert.Equal("Ada arrives.", scene.Synopsis);
        Assert.Equal(7, scene.AnalysisOverrides!.Intensity);
        Assert.Contains("<strong>writing</strong>", await _projects.ReadSceneContentAsync(chapter, scene));
        Assert.Equal("true", Assert.Single(_projects.CurrentProject.ResearchItems).Properties!["Checked"]);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
        Assert.Single(_projects.ActiveBook!.Chapters);
        Assert.All(await NewService().ImportAsync(entries), result => Assert.Equal("skipped", result.Status));
    }

    [Fact]
    public async Task RetriesPreserveEditsAndSourceFragmentsCanCreateMultipleEntries()
    {
        var service = await OpenAsync();
        var first = Entry("people.md#Ada", "character", "\"name\":\"Ada\"");
        var second = Entry("people.md#Ben", "character", "\"name\":\"Ben\"");
        var results = await service.ImportAsync([first, first, second]);
        Assert.Equal(new[] { "imported", "skipped", "imported" }, results.Select(result => result.Status));
        Assert.Equal(results[0].Id, results[1].Id);
        var entities = new EntityService(_projects);
        var ada = (await entities.LoadCharactersAsync()).Single(entity => entity.Id == results[0].Id);
        ada.Surname = "Edited";
        await entities.SaveCharacterAsync(ada);
        Assert.Equal("skipped", Assert.Single(await NewService().ImportAsync([first])).Status);
        Assert.Equal("Edited", (await entities.LoadCharactersAsync()).Single(entity => entity.Id == ada.Id).Surname);
        Assert.Equal("imported", Assert.Single(await service.ImportAsync([Entry("people.md#Ada", "location", "\"name\":\"Home\"")])).Status);
        Assert.Equal(2, (await entities.LoadCharactersAsync()).Count);
    }

    [Fact]
    public async Task SubsequentBatchesRefreshSceneOrderAndStopWhenTheDraftChanges()
    {
        var service = await OpenAsync();
        await service.ImportAsync([Entry("one", "scene", "\"title\":\"One\"")]);
        var chapter = Assert.Single(_projects.ActiveBook!.Chapters);
        Assert.Equal("External import", chapter.Title);
        var written = await _projects.CreateSceneAsync(chapter.Guid, "Written in editor");
        written.Order = 50;
        await _projects.SaveScenesAsync();
        await service.ImportAsync([Entry("two", "scene", "\"title\":\"Two\"")]);
        Assert.Equal(51, _projects.GetScenesForChapter(chapter.Guid).Single(scene => scene.Title == "Two").Order);
        var other = await _projects.CreateDraftAsync("Other");
        await _projects.SwitchDraftAsync(other.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync([Entry("three")]));
    }

    [Fact]
    public async Task InvalidDocumentsAndFailedEntrySavesDoNotDiscardOtherRecords()
    {
        await OpenAsync();
        var proxy = Substitute.For<IFileService>();
        proxy.CreateDirectoryAsync(Arg.Any<string>()).Returns(Task.FromException(new UnauthorizedAccessException()));
        var service = new StructuredImportService(_projects, proxy, "Source writing");
        var results = await service.ImportAsync([
            Entry("bad", "character", "\"name\":\"Ada\",\"age\":42"),
            new("duplicate-key", JsonSerializer.Deserialize<JsonElement>("{\"novalistImport\":1,\"target\":\"character\",\"data\":{\"name\":\"One\",\"name\":\"Two\"}}")),
            Entry("blocked", "scene", "\"title\":\"Blocked\""), Entry("good")
        ]);
        Assert.Equal(new[] { "invalid", "invalid", "failed", "imported" }, results.Select(result => result.Status));
        Assert.NotNull(results[2].Error);
        Assert.Empty(_projects.ActiveBook!.Chapters);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task FailedManifestSavesCanBeRetriedWithoutDuplicatingInMemoryEntries()
    {
        await OpenAsync();
        var proxy = Substitute.For<IProjectService>();
        proxy.IsProjectLoaded.Returns(true);
        proxy.ActiveBookRoot.Returns(_projects.ActiveBookRoot);
        proxy.ActiveDraftRoot.Returns(_projects.ActiveDraftRoot);
        proxy.ActiveBook.Returns(_projects.ActiveBook);
        proxy.CurrentProject.Returns(_projects.CurrentProject);
        proxy.ScenesManifest.Returns(_projects.ScenesManifest);
        proxy.SaveProjectAsync().Returns(_ => Task.FromException(new IOException()), _ => _projects.SaveProjectAsync());
        var service = new StructuredImportService(proxy, _files, "Source writing");
        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync([Entry("one")]));
        Assert.Equal("skipped", Assert.Single(await service.ImportAsync([Entry("one")])).Status);
        await proxy.Received(2).SaveProjectAsync();
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }

    [Fact]
    public async Task ARealProjectWriteFailurePreservesChaptersAndActsForTheNextSave()
    {
        var service = await OpenAsync();
        var chapter = await _projects.CreateChapterAsync("Existing");
        var chapters = _projects.ActiveBook!.Chapters;
        var acts = _projects.ActiveBook.Acts;
        var metadata = Path.Combine(_projects.ProjectRoot!, ".novalist", "project.json");
        File.Move(metadata, metadata + ".backup");
        Directory.CreateDirectory(metadata);
        var failure = await Record.ExceptionAsync(() => service.ImportAsync([Entry("one")]));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Same(chapters, _projects.ActiveBook.Chapters);
        Assert.Same(acts, _projects.ActiveBook.Acts);
        Assert.Equal(chapter.Guid, Assert.Single(_projects.ActiveBook.Chapters).Guid);
        Directory.Delete(metadata);
        File.Move(metadata + ".backup", metadata);
        await service.ImportAsync([Entry("one")]);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        Assert.Equal("Existing", Assert.Single(_projects.ActiveBook!.Chapters).Title);
        Assert.Single(_projects.CurrentProject!.ResearchItems);
    }
}
