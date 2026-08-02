using NSubstitute;
using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Importing a Scrivener project end to end, against a fixture modelled on a
/// real project made by Scrivener 3 from its own novel template, and the
/// structural authoring API that lets a third-party importer do the same thing
/// for a format Novalist does not read itself.
/// </summary>
public sealed class ScrivenerImportRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ManuscriptImportRpc _rpc;

    public ScrivenerImportRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-scriv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ScrivNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ManuscriptImportRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string BuildProject() => ScrivenerProjectBuilder.BuildV3(_root);

    private BookData Book => _workspace.Projects.ActiveBook!;

    private IReadOnlyList<SceneData> ScenesOf(ChapterData chapter)
        => _workspace.Projects.GetScenesForChapter(chapter.Guid);

    [Fact]
    public void TheScrivExtensionIsOfferedAlongsideTheFileFormats()
    {
        Assert.Contains(".scriv", _rpc.Formats());
        Assert.Contains(".docx", _rpc.Formats());
    }

    // ── Preview ──

    [Fact]
    public void PreviewReadsTheBinderWithoutWritingAnything()
    {
        var plan = _rpc.Preview(BuildProject());

        Assert.Equal("scrivener3", plan.Format);
        // Three chapter folders plus the chapter the loose document lands in.
        Assert.Equal(4, plan.ChapterCount);
        Assert.Equal(5, plan.SceneCount);
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public void PreviewKeepsChaptersApartWhenTheyShareATitle()
    {
        // Scrivener's own template names every chapter "Chapter".
        var plan = _rpc.Preview(BuildProject());

        Assert.Equal(["Chapter", "Chapter", "Chapter", "Imported"], plan.Chapters.Select(c => c.Title));
        Assert.Equal(
            [["Arrival", "The Inn"], ["Departure"], ["Return"], ["Loose scene"]],
            plan.Chapters.Select(c => c.Scenes.Select(s => s.Title).ToArray()));
    }

    [Fact]
    public void PreviewNamesTheActsTheChaptersWillLandIn()
    {
        var plan = _rpc.Preview(BuildProject());

        Assert.Equal(["Part", "Part", "Part", ""], plan.Chapters.Select(c => c.PartTitle));
        // One act, from two Scrivener parts: an act is a name here, so two
        // parts the writer left called "Part" are one act however the binder
        // kept them apart. The count is what will exist afterwards, not what
        // the binder had, because that is the number worth showing.
        Assert.Equal(1, plan.PartCount);
    }

    [Fact]
    public void PreviewCountsTheCodexEntriesAndResearchItToo()
    {
        // Named before the import runs, so nothing about a writer's Codex
        // arrives as a surprise.
        var plan = _rpc.Preview(BuildProject());

        Assert.Equal(2, plan.CharacterCount);
        Assert.Equal(1, plan.LocationCount);
        Assert.Equal(8, plan.ResearchCount);
    }

    [Fact]
    public void PreviewNamesWhatWillNotComeAcross()
    {
        Assert.Equal(["Template Sheets", "Trash"], _rpc.Preview(BuildProject()).Losses.Order());
    }

    [Fact]
    public void PreviewOfAPlainFileCarriesNoScrivenerCounts()
    {
        var file = Path.Combine(_root, "book.txt");
        File.WriteAllText(file, "Chapter One\n\nShe arrived at dusk.\n");

        var plan = _rpc.Preview(file);

        Assert.Empty(plan.Losses);
        Assert.Equal(0, plan.PartCount);
        Assert.Equal(0, plan.CharacterCount);
        Assert.Equal(0, plan.LocationCount);
        Assert.Equal(0, plan.ResearchCount);
        Assert.All(plan.Chapters, c => Assert.Empty(c.PartTitle));
    }

    // ── The manuscript ──

    [Fact]
    public async Task RunCreatesEveryChapterAndScene()
    {
        var result = await _rpc.RunAsync(BuildProject());

        Assert.Equal(4, result.Chapters);
        Assert.Equal(5, result.Scenes);
        Assert.Equal(
            ["Arrival", "The Inn", "Departure", "Return", "Loose scene"],
            _workspace.Projects.GetChaptersOrdered().SelectMany(ScenesOf).Select(s => s.Title));
    }

    [Fact]
    public async Task PartsBecomeTheActsTheChaptersAreGroupedUnder()
    {
        await _rpc.RunAsync(BuildProject());

        var chapters = _workspace.Projects.GetChaptersOrdered();

        Assert.Equal(["Part", "Part", "Part", ""], chapters.Select(c => c.Act));
        Assert.Equal(["Part"], Book.Acts.Select(a => a.Name));
    }

    [Fact]
    public async Task TheProseLandsInTheSceneFiles()
    {
        await _rpc.RunAsync(BuildProject());

        var chapter = _workspace.Projects.GetChaptersOrdered().First();
        var scene = ScenesOf(chapter).First();
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);

        Assert.Contains("She arrived at dusk.", html);
        // Read through the paragraph markup the editor speaks, not as raw text.
        Assert.StartsWith("<p>", html);
    }

    [Fact]
    public async Task TheSynopsisCardAndTheDocumentNotesBothComeAcross()
    {
        await _rpc.RunAsync(BuildProject());

        var scene = ScenesOf(_workspace.Projects.GetChaptersOrdered().First()).First();

        Assert.Equal("She arrives and everything changes.", scene.Synopsis);
        Assert.Equal("Check the tide table against chapter four.", scene.Notes);
    }

    [Fact]
    public async Task TheStatusBecomesASceneStageAndTheLabelASceneLabel()
    {
        await _rpc.RunAsync(BuildProject());

        var scene = ScenesOf(_workspace.Projects.GetChaptersOrdered().First()).First();
        var stage = Book.SceneStages.Single(s => s.Key == scene.Stage);
        var label = Book.SceneLabels.Single(l => l.Key == scene.LabelKey);

        Assert.Equal("Revised Draft", stage.Label);
        Assert.Equal("Red", label.Label);
    }

    [Fact]
    public async Task AStatusTheBookAlreadyHasIsReusedRatherThanDuplicated()
    {
        // The book ships with a "Revised" stage; "Revised Draft" is a new one,
        // and importing twice must not add it twice.
        var project = BuildProject();
        await _rpc.RunAsync(project);
        var afterFirst = Book.SceneStages.Count;

        await _rpc.RunAsync(project);

        Assert.Equal(afterFirst, Book.SceneStages.Count);
        Assert.Single(Book.SceneStages, s => s.Label == "Revised Draft");
    }

    [Fact]
    public async Task ADocumentHeldBackFromTheCompileIsHeldBackFromTheExport()
    {
        await _rpc.RunAsync(BuildProject());

        var scenes = _workspace.Projects.GetChaptersOrdered().SelectMany(ScenesOf).ToList();

        Assert.True(scenes.Single(s => s.Title == "Departure").ExcludeFromExport);
        Assert.False(scenes.Single(s => s.Title == "Arrival").ExcludeFromExport);
    }

    [Fact]
    public async Task AnOutlinePlaceholderStillBecomesAScene()
    {
        // Outlining in empty documents is how a Scrivener project starts.
        await _rpc.RunAsync(BuildProject());

        var scene = _workspace.Projects.GetChaptersOrdered()
            .SelectMany(ScenesOf).Single(s => s.Title == "Return");

        Assert.Equal(0, scene.WordCount);
    }

    // ── Custom metadata ──

    [Fact]
    public async Task CustomMetadataBecomesSceneScopedManuscriptProperties()
    {
        await _rpc.RunAsync(BuildProject());

        var properties = Book.ManuscriptProperties
            .Where(p => p.Scope == ManuscriptPropertyScope.Scene)
            .ToList();

        Assert.Equal(["Tension", "POV"], properties.Select(p => p.Label));
        // A Scrivener list field is a closed vocabulary, which is what an enum
        // property is; a text field is not.
        Assert.Equal(CustomPropertyType.Enum, properties[0].Type);
        Assert.Equal(["Low", "High"], properties[0].EnumOptions);
        Assert.Equal(CustomPropertyType.String, properties[1].Type);
    }

    [Fact]
    public async Task TheCustomMetadataValuesLandOnTheScene()
    {
        await _rpc.RunAsync(BuildProject());

        var scene = ScenesOf(_workspace.Projects.GetChaptersOrdered().First()).First();
        var tension = Book.ManuscriptProperties.Single(p => p.Label == "Tension");
        var pov = Book.ManuscriptProperties.Single(p => p.Label == "POV");

        Assert.Equal("High", scene.Properties![tension.Key]);
        Assert.Equal("Mira", scene.Properties[pov.Key]);
    }

    [Fact]
    public async Task ASceneWithNoCustomMetadataCarriesNoProperties()
    {
        await _rpc.RunAsync(BuildProject());

        var inn = _workspace.Projects.GetChaptersOrdered()
            .SelectMany(ScenesOf).Single(s => s.Title == "The Inn");

        Assert.Null(inn.Properties);
    }

    [Fact]
    public async Task APropertyTheBookAlreadyHasIsReusedRatherThanDuplicated()
    {
        var project = BuildProject();
        await _rpc.RunAsync(project);

        await _rpc.RunAsync(project);

        Assert.Single(Book.ManuscriptProperties, p => p.Label == "Tension");
    }

    // ── The Codex ──

    [Fact]
    public async Task CharacterAndSettingSketchesBecomeCodexEntries()
    {
        var result = await _rpc.RunAsync(BuildProject());
        var entities = new EntityService(_workspace.Projects);

        Assert.Equal(2, result.Characters);
        Assert.Equal(1, result.Locations);
        Assert.Equal(
            ["Mira Vance", "Tomas Vance"],
            (await entities.LoadCharactersAsync()).Select(c => c.Name).Order());
        Assert.Equal(["Hillsford"], (await entities.LoadLocationsAsync()).Select(l => l.Name));
    }

    [Fact]
    public async Task ASketchKeepsItsProseAndItsNotesAsSections()
    {
        // A filled-in Scrivener sheet is already a set of headed answers, so it
        // belongs in a section rather than flattened into a description.
        await _rpc.RunAsync(BuildProject());

        var mira = (await new EntityService(_workspace.Projects).LoadCharactersAsync())
            .Single(c => c.Name == "Mira Vance");

        Assert.Equal(["Sketch", "Notes"], mira.Sections.Select(s => s.Title));
        Assert.Equal("Mira Vance, harbourmaster.", mira.Sections[0].Content);
        Assert.Equal("Do not reveal the brother until part two.", mira.Sections[1].Content);
    }

    [Fact]
    public async Task TheBlankTemplateSheetsDoNotBecomeCharacters()
    {
        await _rpc.RunAsync(BuildProject());

        Assert.DoesNotContain(
            await new EntityService(_workspace.Projects).LoadCharactersAsync(),
            c => c.Name == "Character Sketch");
    }

    // ── Research ──

    [Fact]
    public async Task EverythingElseThatCarriedContentBecomesResearch()
    {
        var result = await _rpc.RunAsync(BuildProject());
        var research = new ResearchService(_workspace.Projects, _workspace.FileService).GetAll();

        Assert.Equal(8, result.Research);
        Assert.Equal(
            ["Dedication", "Harbour survey", "Harbour walk-through", "Harbourmaster interview",
             "Map scan", "Novel Format", "Tide tables", "Tonnage returns"],
            research.Select(r => r.Title).Order());
    }

    [Fact]
    public async Task AResearchNoteKeepsItsProseAndTheFolderItSatIn()
    {
        await _rpc.RunAsync(BuildProject());

        var dedication = new ResearchService(_workspace.Projects, _workspace.FileService)
            .GetAll().Single(r => r.Title == "Dedication");

        Assert.Equal(ResearchItemType.Note, dedication.Type);
        Assert.Equal("For everyone who waited.", dedication.Content);
        Assert.Equal(["Front Matter"], dedication.Tags);
    }

    [Fact]
    public async Task AFileBackedResearchItemIsCopiedIntoTheProject()
    {
        // Copied rather than referenced, so the project stays portable when it
        // is zipped or moved - and the Scrivener project can be deleted.
        await _rpc.RunAsync(BuildProject());

        var service = new ResearchService(_workspace.Projects, _workspace.FileService);
        var pdf = service.GetAll().Single(r => r.Title == "Harbour survey");

        Assert.Equal(ResearchItemType.Pdf, pdf.Type);
        Assert.True(File.Exists(service.GetAbsolutePath(pdf.Content)));
        Assert.StartsWith(_workspace.Projects.ProjectRoot!, service.GetAbsolutePath(pdf.Content));
    }

    [Fact]
    public async Task APictureKeepsItsImageType()
    {
        await _rpc.RunAsync(BuildProject());

        var image = new ResearchService(_workspace.Projects, _workspace.FileService)
            .GetAll().Single(r => r.Title == "Map scan");

        Assert.Equal(ResearchItemType.Image, image.Type);
    }

    [Fact]
    public async Task AnImportedRecordingArrivesAsAudioRatherThanAnAnonymousFile()
    {
        // Scrivener has one type for everything it imported whole, so what the
        // file is has to come from its extension. A recording a scene was
        // written to should be playable in the Research view.
        await _rpc.RunAsync(BuildProject());

        var research = new ResearchService(_workspace.Projects, _workspace.FileService).GetAll();

        Assert.Equal(
            ResearchItemType.Audio,
            research.Single(r => r.Title == "Harbourmaster interview").Type);
        Assert.Equal(
            ResearchItemType.Video,
            research.Single(r => r.Title == "Harbour walk-through").Type);
        Assert.Equal(
            ResearchItemType.File,
            research.Single(r => r.Title == "Tonnage returns").Type);
    }

    // ── The Scrivener 2 layout ──

    [Fact]
    public async Task AScrivener2ProjectImportsTheSameWay()
    {
        var result = await _rpc.RunAsync(ScrivenerProjectBuilder.BuildV2(_root));

        Assert.Equal(4, result.Chapters);
        Assert.Equal(5, result.Scenes);
        Assert.Equal(2, result.Characters);
        Assert.Equal(1, result.Locations);
        Assert.Equal(8, result.Research);

        // The synopsis and notes that sat in the suffixed files all along.
        var scene = ScenesOf(_workspace.Projects.GetChaptersOrdered().First()).First();
        Assert.Equal("She arrives and everything changes.", scene.Synopsis);
        Assert.Equal("Check the tide table against chapter four.", scene.Notes);
    }

    // ── Edges ──

    [Fact]
    public async Task AnUnreadableProjectCreatesNothing()
    {
        var empty = Path.Combine(_root, "Empty.scriv");
        Directory.CreateDirectory(empty);

        var result = await _rpc.RunAsync(empty);

        Assert.Equal(0, result.Chapters);
        Assert.Equal(0, result.Research);
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public async Task ImportingTwiceAppendsRatherThanReplaces()
    {
        var project = BuildProject();
        await _rpc.RunAsync(project);

        await _rpc.RunAsync(project);

        Assert.Equal(8, _workspace.Projects.GetChaptersOrdered().Count);
    }

    [Fact]
    public async Task ImportingWithNoProjectOpenIsRefused()
    {
        var closed = new Workspace(Path.Combine(_root, "settings2"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ManuscriptImportRpc(closed).RunAsync(BuildProject()));
    }

    [Fact]
    public async Task APlainFileStillImportsAsChaptersAndScenes()
    {
        var file = Path.Combine(_root, "book.txt");
        File.WriteAllText(file, "Chapter One\n\nShe arrived at dusk.\n");

        var result = await _rpc.RunAsync(file);

        Assert.Equal(1, result.Chapters);
        Assert.Equal(0, result.Characters);
        Assert.Equal(0, result.Research);
    }

    [Fact]
    public async Task AFileWithNothingReadableCreatesNothing()
    {
        var file = Path.Combine(_root, "empty.txt");
        File.WriteAllText(file, "");

        var result = await _rpc.RunAsync(file);

        Assert.Equal(0, result.Chapters);
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    // ── The structural authoring API ──

    private IExtensionProjectService ExtensionHost()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        return new HostServices(
            _workspace.FileService,
            _workspace.Projects,
            new EntityService(_workspace.Projects),
            settings);
    }

    [Fact]
    public async Task AnExtensionCanBuildAChapterAndSceneAndWriteToIt()
    {
        // This is what a third-party .fdx or .scriv reader needs; before it
        // existed, every importer had to be written into core.
        var host = ExtensionHost();

        var chapterGuid = await host.CreateChapterAsync("From an extension");
        var sceneId = await host.CreateSceneAsync(chapterGuid, "Scene one");
        await host.WriteSceneContentAsync(chapterGuid, sceneId, "<p>Written by an extension.</p>");

        Assert.Equal(
            "<p>Written by an extension.</p>",
            await host.ReadSceneContentAsync(chapterGuid, sceneId));
    }

    [Fact]
    public async Task AnExtensionWriteUpdatesTheWordCountTheBinderShows()
    {
        var host = ExtensionHost();
        var chapterGuid = await host.CreateChapterAsync("One");
        var sceneId = await host.CreateSceneAsync(chapterGuid, "Scene");

        await host.WriteSceneContentAsync(chapterGuid, sceneId, "<p>one two three four</p>");

        Assert.Equal(
            4,
            _workspace.Projects.GetScenesForChapter(chapterGuid).Single(s => s.Id == sceneId).WordCount);
    }

    [Fact]
    public async Task ASceneUnderAChapterThatDoesNotExistIsRefused()
    {
        // An orphan scene would be unreachable in the binder.
        Assert.Empty(await ExtensionHost().CreateSceneAsync("no-such-chapter", "Scene"));
    }

    [Fact]
    public async Task WritingToASceneThatDoesNotExistDoesNothing()
    {
        var host = ExtensionHost();
        var chapterGuid = await host.CreateChapterAsync("One");

        await host.WriteSceneContentAsync(chapterGuid, "no-such-scene", "<p>x</p>");

        Assert.Empty(_workspace.Projects.GetScenesForChapter(chapterGuid));
    }
}
