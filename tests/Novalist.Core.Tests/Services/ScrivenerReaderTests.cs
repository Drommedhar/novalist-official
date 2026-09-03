using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a Scrivener project.
///
/// The exhaustive cases run against <see cref="ScrivenerProjectBuilder"/>, a
/// fixture modelled on a real project made by Scrivener 3 from its own novel
/// template: the same Type attributes, icon names, label and status
/// vocabularies, and the same trap the template sets by naming every part
/// "Part" and every chapter "Chapter". Both on-disk layouts are built from one
/// binder, because both are in the wild.
/// </summary>
public class ScrivenerReaderTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Rtf(string text) => ScrivenerProjectBuilder.Rtf(text);

    private string NewProject(string name = "Book.scriv")
    {
        var root = Path.Combine(_dir.Path, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteScrivx(string root, string binderXml, string extra = "")
        => File.WriteAllText(
            Path.Combine(root, "Book.scrivx"),
            $"<?xml version=\"1.0\"?><ScrivenerProject><Binder>{binderXml}</Binder>{extra}</ScrivenerProject>",
            Encoding.UTF8);

    /// <summary>Scrivener 3: a UUID folder per document.</summary>
    private static void WriteDoc3(string root, string uuid, string text, string? synopsis = null)
    {
        var folder = Path.Combine(root, "Files", "Data", uuid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.rtf"), Rtf(text));
        if (synopsis != null) File.WriteAllText(Path.Combine(folder, "synopsis.txt"), synopsis);
    }

    /// <summary>Scrivener 2: a numbered file under Files/Docs.</summary>
    private static void WriteDoc2(string root, string id, string text)
    {
        var folder = Path.Combine(root, "Files", "Docs");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".rtf"), Rtf(text));
    }

    // ── Recognition ──

    [Fact]
    public void AProjectFolderIsRecognisedWithoutDependingOnItsName()
    {
        Assert.True(ScrivenerReader.LooksLikeScrivener(NewProject("Arit")));
    }

    [Fact]
    public void TheScrivxInsideIsRecognisedToo()
    {
        // A file picker gives one or the other depending on the platform.
        var root = NewProject();
        WriteScrivx(root, "");

        Assert.True(ScrivenerReader.LooksLikeScrivener(Path.Combine(root, "Book.scrivx")));
    }

    [Fact]
    public void AMissingScrivPathStillUsesScrivenerDiagnostics()
    {
        Assert.True(ScrivenerReader.LooksLikeScrivener(
            Path.Combine(_dir.Path, "Missing.scriv")));
    }

    [Fact]
    public void TheSelectedScrivxIsUsedEvenWhenAnotherManifestIsBesideIt()
    {
        // Linux users select the binder file itself. The reader used to ignore
        // that path and enumerate the parent folder, so a sync conflict copy or
        // another manifest beside it could be opened instead.
        var root = NewProject("Arit");
        File.WriteAllText(Path.Combine(root, "00-broken.scrivx"), "<broken");
        var selected = Path.Combine(root, "Arit.scrivx");
        File.WriteAllText(
            selected,
            """
            <?xml version="1.0"?>
            <ScrivenerProject><Binder>
              <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
                <BinderItem UUID="S1" Type="Text"><Title>Chosen scene</Title></BinderItem>
              </Children></BinderItem>
            </Binder></ScrivenerProject>
            """,
            Encoding.UTF8);
        var data = Path.Combine(root, "FILES", "dAtA", "s1");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "CONTENT.RTF"), Rtf("Selected Linux prose."));

        var scene = Assert.Single(ScrivenerReader.Read(selected).Scenes);
        Assert.Equal("Chosen scene", scene.Title);
        Assert.Equal("Selected Linux prose.", scene.Text);
        Assert.Equal(["D", "S1"], ScrivenerReader.Outline(selected).Select(row => row.Key));
    }

    [Fact]
    public void AFolderWithSeveralManifestsIsAmbiguousEvenWhenOneMatchesItsName()
    {
        var root = NewProject("Original.scriv");
        File.WriteAllText(Path.Combine(root, "Original.scrivx"), "<ScrivenerProject/>");
        File.WriteAllText(Path.Combine(root, "Original-conflict.scrivx"), "<ScrivenerProject/>");
        var diagnostics = new List<ScrivenerReadDiagnostic>();

        Assert.True(ScrivenerReader.Read(root, null, diagnostics.Add).IsEmpty);

        Assert.Equal(
            new ScrivenerReadDiagnostic("manifest", "ambiguous"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void ProjectPackagePathsAreResolvedWithoutAssumingLinuxCasing()
    {
        // NTFS hides this bug; ext4 does not. Archives and sync tools can alter
        // the spelling of package components even though they remain the same
        // Scrivener names.
        var root = NewProject("Case.scriv");
        File.WriteAllText(
            Path.Combine(root, "CASE.SCRIVX"),
            """
            <?xml version="1.0"?>
            <ScrivenerProject><Binder>
              <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
                <BinderItem UUID="S1" Type="Text"><Title>Scene</Title></BinderItem>
              </Children></BinderItem>
            </Binder></ScrivenerProject>
            """,
            Encoding.UTF8);
        var data = Path.Combine(root, "FILES", "dAtA", "s1");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "CONTENT.RTF"), Rtf("Linux prose."));

        var project = ScrivenerReader.Read(root);

        Assert.Equal("3", project.Version);
        Assert.Equal("Linux prose.", Assert.Single(project.Scenes).Text);
    }

    [Fact]
    public void ADefaultXmlNamespaceDoesNotHideTheBinder()
    {
        // XML repair/copying tools sometimes add a default namespace. The
        // Scrivener vocabulary is still identified completely by local name.
        var root = NewProject("Namespaced.scriv");
        File.WriteAllText(
            Path.Combine(root, "Namespaced.scrivx"),
            """
            <?xml version="1.0"?>
            <ScrivenerProject xmlns="urn:scrivener-project">
              <Binder>
                <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
                  <BinderItem UUID="S1" Type="Text"><Title>Namespaced scene</Title></BinderItem>
                </Children></BinderItem>
              </Binder>
            </ScrivenerProject>
            """,
            Encoding.UTF8);

        var scene = Assert.Single(ScrivenerReader.Read(root).Scenes);

        Assert.Equal("Namespaced scene", scene.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAProject(string path)
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener(path));
    }

    [Fact]
    public void AnySelectedFolderUsesTheOnlyDirectoryBasedImporter()
    {
        Assert.True(ScrivenerReader.LooksLikeScrivener(_dir.Path));
    }

    [Fact]
    public void AWordFileIsNotAProject()
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener("book.docx"));
    }

    // ── The whole project, both layouts ──

    public static TheoryData<bool> BothLayouts => new() { true, false };

    private ScrivenerProject ReadExhaustive(bool v3)
        => ScrivenerReader.Read(v3
            ? ScrivenerProjectBuilder.BuildV3(_dir.Path)
            : ScrivenerProjectBuilder.BuildV2(_dir.Path));

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void TheLayoutIsIdentifiedFromWhereTheDocumentsLive(bool v3)
    {
        Assert.Equal(v3 ? "3" : "2", ReadExhaustive(v3).Version);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void OnlyTheDraftBecomesScenes(bool v3)
    {
        // Everything else in the binder carried content too, and none of it is
        // manuscript: the template's instruction sheet, two character sketches,
        // a place, a dedication, a research note, a PDF and a picture.
        Assert.Equal(
            ["Arrival", "The Inn", "Departure", "Return", "Loose scene"],
            ReadExhaustive(v3).Scenes.Select(s => s.Title));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void PartsAndChaptersKeepTheirIdentityWhenTheyShareATitle(bool v3)
    {
        // The stock template names every part "Part" and every chapter
        // "Chapter". Grouping on the title collapsed the whole book into one.
        var scenes = ReadExhaustive(v3).Scenes;

        Assert.Equal(2, scenes.Select(s => s.PartKey).Where(k => k.Length > 0).Distinct().Count());
        Assert.Equal(3, scenes.Select(s => s.ChapterKey).Distinct().Count()
            - 1); // the loose scene's chapter is the fourth
        Assert.All(scenes.Take(4), s => Assert.Equal("Chapter", s.ChapterTitle));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void ADocumentWithNoFolderOfItsOwnLandsInADefaultChapter(bool v3)
    {
        var loose = ReadExhaustive(v3).Scenes.Single(s => s.Title == "Loose scene");

        Assert.Equal(ScrivenerReader.DefaultChapterTitle, loose.ChapterTitle);
        Assert.Empty(loose.PartTitle);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void AnEmptyDraftDocumentStillComesAcrossAsAScene(bool v3)
    {
        // Outlining in empty documents is how a Scrivener project starts. An
        // importer that reads only the ones with prose in them turns a planned
        // book into an empty one.
        var placeholder = ReadExhaustive(v3).Scenes.Single(s => s.Title == "Return");

        Assert.Empty(placeholder.Text);
        Assert.Equal("Chapter", placeholder.ChapterTitle);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void TheProseSynopsisAndNotesAllComeAcross(bool v3)
    {
        var arrival = ReadExhaustive(v3).Scenes.First();

        Assert.Equal("She arrived at dusk.", arrival.Text);
        Assert.Equal("She arrives and everything changes.", arrival.Synopsis);
        Assert.Equal("Check the tide table against chapter four.", arrival.Notes);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void TheLabelAndStatusAreResolvedToTheirNames(bool v3)
    {
        var arrival = ReadExhaustive(v3).Scenes.First();

        Assert.Equal("Red", arrival.Label);
        Assert.Equal("Revised Draft", arrival.Status);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void ADocumentWithNoLabelOrStatusCarriesNeither(bool v3)
    {
        var inn = ReadExhaustive(v3).Scenes.Single(s => s.Title == "The Inn");

        Assert.Empty(inn.Label);
        Assert.Empty(inn.Status);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void ExcludingADocumentFromTheCompileIsCarried(bool v3)
    {
        var scenes = ReadExhaustive(v3).Scenes;

        Assert.False(scenes.Single(s => s.Title == "Departure").IncludeInCompile);
        Assert.True(scenes.Single(s => s.Title == "Arrival").IncludeInCompile);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void CustomMetadataIsReadAgainstTheFieldsTheProjectDeclares(bool v3)
    {
        var project = ReadExhaustive(v3);

        Assert.Equal(["Tension", "POV"], project.CustomFields.Select(f => f.Title));
        Assert.Equal(["Low", "High"], project.CustomFields[0].Options);

        var arrival = project.Scenes.First();
        Assert.Equal("High", arrival.CustomFields["tension"]);
        Assert.Equal("Mira", arrival.CustomFields["pov"]);
        // A value for a field the project never declared has nothing to be
        // called, so it is dropped rather than guessed at.
        Assert.False(arrival.CustomFields.ContainsKey("unknown-field"));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void CharacterAndSettingSketchesBecomeCodexEntries(bool v3)
    {
        var entities = ReadExhaustive(v3).Entities;

        Assert.Equal(
            [("Mira Vance", ScrivenerEntityKind.Character),
             ("Tomas Vance", ScrivenerEntityKind.Character),
             ("Hillsford", ScrivenerEntityKind.Location)],
            entities.Select(e => (e.Name, e.Kind)));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void ASketchKeepsItsProseAndItsNotes(bool v3)
    {
        var mira = ReadExhaustive(v3).Entities.First();

        Assert.Equal("Mira Vance, harbourmaster.", mira.Text);
        Assert.Equal("Do not reveal the brother until part two.", mira.Notes);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void ASketchInsideAGroupingFolderStillLands(bool v3)
    {
        // A writer who files characters by house still gets the characters.
        Assert.Contains(ReadExhaustive(v3).Entities, e => e.Name == "Tomas Vance");
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void EverythingElseThatCarriedContentBecomesResearch(bool v3)
    {
        var research = ReadExhaustive(v3).Research;

        Assert.Equal(
            ["Novel Format", "Dedication", "Harbour survey", "Map scan", "Tide tables",
             "Harbourmaster interview", "Tonnage returns", "Harbour walk-through"],
            research.Select(r => r.Title));
        Assert.Equal(
            [ScrivenerResearchKind.Note, ScrivenerResearchKind.Note, ScrivenerResearchKind.Pdf,
             ScrivenerResearchKind.Image, ScrivenerResearchKind.Note, ScrivenerResearchKind.File,
             ScrivenerResearchKind.File, ScrivenerResearchKind.File],
            research.Select(r => r.Kind));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void AResearchItemKeepsTheFolderItSatInAsATag(bool v3)
    {
        var research = ReadExhaustive(v3).Research;

        Assert.Equal("Front Matter", research.Single(r => r.Title == "Dedication").FolderTag);
        Assert.Equal("Sources", research.Single(r => r.Title == "Map scan").FolderTag);
        // A document loose at the top level sat in no folder at all.
        Assert.Empty(research.Single(r => r.Title == "Novel Format").FolderTag);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void AFileBackedResearchItemPointsAtItsFile(bool v3)
    {
        var pdf = ReadExhaustive(v3).Research.Single(r => r.Kind == ScrivenerResearchKind.Pdf);

        Assert.True(File.Exists(pdf.SourcePath));
        Assert.Empty(pdf.Text);
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void AnImportedFileNamesItsOwnExtension(bool v3)
    {
        // A recording or a spreadsheet has no binder type of its own; the
        // extension in its metadata is what says where the bytes are.
        var audio = ReadExhaustive(v3).Research.Single(r => r.Title == "Harbourmaster interview");

        Assert.Equal(ScrivenerResearchKind.File, audio.Kind);
        Assert.EndsWith(".m4a", audio.SourcePath);
        Assert.True(File.Exists(audio.SourcePath));
    }

    [Theory]
    [MemberData(nameof(BothLayouts))]
    public void TheTemplateSheetsAndTheTrashAreReportedRatherThanImported(bool v3)
    {
        // The template sheets are Scrivener's blank forms: importing them
        // produces a character called "Character Sketch" whose every field is
        // a prompt.
        var project = ReadExhaustive(v3);

        Assert.Equal(["Template Sheets", "Trash"], project.Losses.Order());
        Assert.DoesNotContain(project.Entities, e => e.Name == "Character Sketch");
        Assert.DoesNotContain(project.Scenes, s => s.Title == "Cut scene");
        Assert.DoesNotContain(project.Research, r => r.Title == "Cut scene");
    }

    // ── The draft folder is what marks the manuscript ──

    [Fact]
    public void TheDraftFolderIsFoundByItsTypeRatherThanItsTitle()
    {
        // A German Scrivener calls it "Manuskript" and a writer may call it
        // anything at all; the Type attribute is what does not move.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Meine Kapitel</Title><Children>
              <BinderItem UUID="F1" Type="Folder"><Title>Erstes Kapitel</Title><Children>
                <BinderItem UUID="S1" Type="Text"><Title>Ankunft</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "S1", "Sie kam bei Einbruch der Dunkelheit an.");

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Erstes Kapitel", scene.ChapterTitle);
        Assert.Empty(scene.PartTitle);
    }

    [Fact]
    public void AFolderOfFoldersIsAPartAndAFolderOfDocumentsIsAChapter()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="P1" Type="Folder"><Title>Act One</Title><Children>
                <BinderItem UUID="C1" Type="Folder"><Title>Chapter One</Title><Children>
                  <BinderItem UUID="S1" Type="Text"><Title>Deep</Title></BinderItem>
                </Children></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "S1", "Text.");

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Act One", scene.PartTitle);
        Assert.Equal("Chapter One", scene.ChapterTitle);
        Assert.Equal("Deep", scene.Title);
    }

    [Fact]
    public void AnythingBelowAChapterIsFlattenedIntoIt()
    {
        // Scrivener nests arbitrarily; Novalist is three levels. Losing nesting
        // is the right trade against losing text.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="P1" Type="Folder"><Title>Act One</Title><Children>
                <BinderItem UUID="C1" Type="Folder"><Title>Chapter One</Title><Children>
                  <BinderItem UUID="G1" Type="Folder"><Title>A sequence</Title><Children>
                    <BinderItem UUID="S1" Type="Text"><Title>Deeper still</Title></BinderItem>
                  </Children></BinderItem>
                </Children></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "S1", "Text.");

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Act One", scene.PartTitle);
        Assert.Equal("Chapter One", scene.ChapterTitle);
    }

    [Fact]
    public void AChapterFolderCarryingTextOfItsOwnBecomesASceneToo()
    {
        // A chapter with an epigraph in the folder.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="C1" Type="Folder"><Title>One</Title><Children>
                <BinderItem UUID="S1" Type="Text"><Title>Scene</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "C1", "An epigraph.");
        WriteDoc3(root, "S1", "The scene.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal(2, project.Scenes.Count);
        Assert.Contains(project.Scenes, s => s.Text == "An epigraph." && s.ChapterTitle == "One");
    }

    [Fact]
    public void APartFolderCarryingTextOfItsOwnLandsUnderItsOwnName()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="P1" Type="Folder"><Title>Act One</Title><Children>
                <BinderItem UUID="C1" Type="Folder"><Title>One</Title><Children>
                  <BinderItem UUID="S1" Type="Text"><Title>Scene</Title></BinderItem>
                </Children></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "P1", "A part epigraph.");
        WriteDoc3(root, "S1", "The scene.");

        var epigraph = ScrivenerReader.Read(root).Scenes.Single(s => s.Text == "A part epigraph.");

        Assert.Equal("Act One", epigraph.PartTitle);
        Assert.Equal("Act One", epigraph.ChapterTitle);
    }

    [Fact]
    public void ABinderWithNoDraftFolderIsReadAsAllManuscript()
    {
        // A hand-made project, or a fragment. There is nothing else it could be.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>Chapter One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Arrival</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "D1", "She arrived at dusk.");

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Chapter One", scene.ChapterTitle);
        Assert.Equal("She arrived at dusk.", scene.Text);
    }

    // ── Codex detection ──

    [Fact]
    public void AFolderWithNoIconFallsBackToItsTitle()
    {
        // A project old enough to carry no icon names at all.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title></BinderItem>
            <BinderItem UUID="C" Type="Folder"><Title>Characters</Title><Children>
              <BinderItem UUID="E1" Type="Text"><Title>Mira</Title></BinderItem>
            </Children></BinderItem>
            <BinderItem UUID="P" Type="Folder"><Title>Places</Title><Children>
              <BinderItem UUID="E2" Type="Text"><Title>Hillsford</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "E1", "A character.");
        WriteDoc3(root, "E2", "A place.");

        var entities = ScrivenerReader.Read(root).Entities;

        Assert.Equal(ScrivenerEntityKind.Character, entities.Single(e => e.Name == "Mira").Kind);
        Assert.Equal(ScrivenerEntityKind.Location, entities.Single(e => e.Name == "Hillsford").Kind);
    }

    [Fact]
    public void AnEmptySketchIsNotAnEntry()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title></BinderItem>
            <BinderItem UUID="C" Type="Folder">
              <Title>Cast</Title>
              <MetaData><IconFileName>Characters (Photo)</IconFileName></MetaData>
              <Children>
                <BinderItem UUID="E1" Type="Text"><Title>Not filled in yet</Title></BinderItem>
              </Children>
            </BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Entities);
    }

    // ── Scrivener 2 ──

    [Fact]
    public void AScrivener2ProjectReadsFromItsNumberedDocuments()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem ID="10" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem ID="11" Type="Folder"><Title>Chapter One</Title><Children>
                <BinderItem ID="12" Type="Text"><Title>Arrival</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc2(root, "12", "Old project prose.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal("2", project.Version);
        Assert.Equal("Old project prose.", project.Scenes.Single().Text);
    }

    [Fact]
    public void AScrivener2SynopsisAndNotesAreReadFromTheirSuffixedFiles()
    {
        // Both sat beside the prose the whole time and nothing read either.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem ID="10" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem ID="11" Type="Folder"><Title>One</Title><Children>
                <BinderItem ID="12" Type="Text"><Title>Arrival</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        var docs = Path.Combine(root, "Files", "Docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "12.rtf"), Rtf("Prose."));
        File.WriteAllText(Path.Combine(docs, "12_synopsis.txt"), "The card.");
        File.WriteAllText(Path.Combine(docs, "12_notes.rtf"), Rtf("The notes."));

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("The card.", scene.Synopsis);
        Assert.Equal("The notes.", scene.Notes);
    }

    // ── Unreadable input ──

    [Fact]
    public void AFolderThatIsNotAProjectReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(_dir.Path).IsEmpty);
    }

    [Fact]
    public void AProjectWithNoScrivxReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(NewProject()).IsEmpty);
    }

    [Fact]
    public void ReadFailuresExposeOnlyContentFreeDiagnosticReasons()
    {
        var diagnostics = new List<ScrivenerReadDiagnostic>();

        Assert.True(ScrivenerReader.Read(
            Path.Combine(_dir.Path, "missing.scriv"), null, diagnostics.Add).IsEmpty);
        Assert.Equal(
            new ScrivenerReadDiagnostic("path", "not-found"),
            Assert.Single(diagnostics));

        diagnostics.Clear();
        Assert.True(ScrivenerReader.Read(NewProject("NoManifest.scriv"), null, diagnostics.Add).IsEmpty);
        Assert.Equal(
            new ScrivenerReadDiagnostic("manifest", "not-found"),
            Assert.Single(diagnostics));

        diagnostics.Clear();
        var noBinder = NewProject("NoBinder.scriv");
        File.WriteAllText(
            Path.Combine(noBinder, "NoBinder.scrivx"),
            "<?xml version=\"1.0\"?><ScrivenerProject/>");
        Assert.True(ScrivenerReader.Read(noBinder, null, diagnostics.Add).IsEmpty);
        Assert.Equal(
            new ScrivenerReadDiagnostic("binder", "not-found"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void AMalformedScrivxReadsAsEmptyRatherThanThrowing()
    {
        // An import that cannot start should say so in the dialog, not crash.
        var root = NewProject();
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<not xml at all");

        Assert.True(ScrivenerReader.Read(root).IsEmpty);
    }

    [Fact]
    public void MalformedXmlHasADiagnosticReasonWithoutLeakingItsContents()
    {
        var root = NewProject();
        File.WriteAllText(
            Path.Combine(root, "Book.scrivx"),
            "<PrivateChapterTitle></PrivateSecret>");
        var diagnostics = new List<ScrivenerReadDiagnostic>();

        Assert.True(ScrivenerReader.Read(root, null, diagnostics.Add).IsEmpty);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("manifest", diagnostic.Stage);
        Assert.Equal("invalid-xml", diagnostic.Reason);
        Assert.Equal("XmlException", diagnostic.ExceptionType);
        Assert.Equal("An XML start tag does not match its end tag.", diagnostic.Detail);
        Assert.Equal(1, diagnostic.LineNumber);
        Assert.True(diagnostic.LinePosition > 0);
        Assert.NotEqual(0, diagnostic.ErrorCode);
        Assert.DoesNotContain("private", diagnostic.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Unexpected end of file. The following elements are not closed: PrivateTitle.", "The XML ended before one or more elements were closed.")]
    [InlineData("The 'Private' start tag does not match the end tag of 'Secret'.", "An XML start tag does not match its end tag.")]
    [InlineData("Reference to undeclared entity 'Private'.", "The XML references an entity that was not declared.")]
    [InlineData("The 'private' prefix is undeclared prefix data.", "The XML uses a namespace prefix that was not declared.")]
    [InlineData("Root element is missing.", "The XML root element is missing.")]
    [InlineData("There are multiple root elements.", "The XML contains more than one root element.")]
    [InlineData("Data at the root level is invalid.", "Data at the XML root is invalid.")]
    [InlineData("Name cannot begin with the ' ' character.", "An XML name begins with a character XML does not allow.")]
    [InlineData("A name was started with an invalid character.", "An XML name begins with a character XML does not allow.")]
    [InlineData("Invalid character in the given encoding.", "The XML contains a character XML does not allow.")]
    [InlineData("The '=' character cannot be included in a name.", "The XML contains a character XML does not allow.")]
    [InlineData("For security reasons DTD is prohibited in this XML document.", "The XML contains a DTD, which this parser does not allow.")]
    [InlineData("There is an unclosed literal string.", "The XML contains an unterminated quoted value.")]
    [InlineData("Unexpected token PrivateTitle.", "The XML contains an unexpected token.")]
    [InlineData("Private unclassified parser wording.", "The XML parser rejected the document; source-specific details were redacted.")]
    public void XmlDiagnosticDetailsUseAContentFreeVocabulary(string runtimeMessage, string expected)
    {
        var detail = ScrivenerReader.SafeExceptionDetail(new System.Xml.XmlException(runtimeMessage));

        Assert.Equal(expected, detail);
        Assert.DoesNotContain("Private", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonXmlDiagnosticDetailsNeverRepeatRuntimeMessages()
    {
        Assert.Equal(
            "The operating system denied access to a project file.",
            ScrivenerReader.SafeExceptionDetail(
                new UnauthorizedAccessException("Private path and title")));
        Assert.Equal(
            "The operating system could not read a project file; it may be locked or unavailable.",
            ScrivenerReader.SafeExceptionDetail(new IOException("Private path and title")));
        Assert.Equal(
            "The project could not be read.",
            ScrivenerReader.SafeExceptionDetail(new InvalidOperationException("Private title")));
    }

    [Fact]
    public void AnUnreadableManifestHasAContentFreePackageDiagnostic()
    {
        var root = NewProject();
        WriteScrivx(root, "");
        var diagnostics = new List<ScrivenerReadDiagnostic>();
        using var locked = File.Open(
            Path.Combine(root, "Book.scrivx"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.True(ScrivenerReader.Read(root, null, diagnostics.Add).IsEmpty);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("package", diagnostic.Stage);
        Assert.Equal("access-failed", diagnostic.Reason);
        Assert.Equal("IOException", diagnostic.ExceptionType);
        Assert.Contains("could not read", diagnostic.Detail);
        Assert.NotEqual(0, diagnostic.ErrorCode);
        Assert.DoesNotContain(root, diagnostic.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidSupportingXmlDoesNotMakeTheOutlineThrow()
    {
        var root = NewProject();
        WriteScrivx(root, "<BinderItem UUID=\"D\" Type=\"DraftFolder\"><Title>Draft</Title></BinderItem>");
        var files = Path.Combine(root, "Files");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "styles.xml"), "<broken");
        var diagnostics = new List<ScrivenerReadDiagnostic>();

        Assert.Empty(ScrivenerReader.Outline(root, diagnostics.Add));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("supporting-files", diagnostic.Stage);
        Assert.Equal("invalid-xml", diagnostic.Reason);
        Assert.Equal("XmlException", diagnostic.ExceptionType);
        Assert.NotEmpty(diagnostic.Detail);
        Assert.Equal(1, diagnostic.LineNumber);
        Assert.True(diagnostic.LinePosition > 0);
        Assert.NotEqual(0, diagnostic.ErrorCode);
    }

    [Fact]
    public void AContentReadFailureIsReportedWithoutAPathOrProse()
    {
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="S1" Type="Text"><Title>Scene</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "S1", "Private prose.");
        var diagnostics = new List<ScrivenerReadDiagnostic>();
        using var locked = File.Open(
            Path.Combine(root, "Files", "Data", "S1", "content.rtf"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.True(ScrivenerReader.Read(root, null, diagnostics.Add).IsEmpty);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("content", diagnostic.Stage);
        Assert.Equal("read-failed", diagnostic.Reason);
        Assert.Equal("IOException", diagnostic.ExceptionType);
        Assert.Contains("could not read", diagnostic.Detail);
        Assert.NotEqual(0, diagnostic.ErrorCode);
        Assert.DoesNotContain(root, diagnostic.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptyBinderHasAnOutlineDiagnostic()
    {
        var root = NewProject();
        WriteScrivx(root, "");
        var diagnostics = new List<ScrivenerReadDiagnostic>();

        Assert.Empty(ScrivenerReader.Outline(root, diagnostics.Add));

        Assert.Equal(
            new ScrivenerReadDiagnostic("binder", "empty"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void AScrivxWithNoBinderReadsAsEmpty()
    {
        var root = NewProject();
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<?xml version=\"1.0\"?><Project/>");

        Assert.True(ScrivenerReader.Read(root).IsEmpty);
    }

    [Fact]
    public void ADocumentWhoseFileIsMissingStillLandsAsAnOutlineScene()
    {
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
                <BinderItem UUID="GONE" Type="Text"><Title>Planned</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Planned", scene.Title);
        Assert.Empty(scene.Text);
    }

    [Fact]
    public void ADocumentWithNoIdContributesNoProse()
    {
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem Type="Folder"><Title>One</Title><Children>
                <BinderItem Type="Text"><Title>No id</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Scenes.Single().Text);
    }

    [Fact]
    public void TwoUntitledChaptersDoNotMergeIntoOne()
    {
        // Nothing identifies them, and merging silently loses a chapter.
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem Type="Folder"><Children>
                <BinderItem Type="Text"><Title>A</Title></BinderItem>
              </Children></BinderItem>
              <BinderItem Type="Folder"><Children>
                <BinderItem Type="Text"><Title>B</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Equal(2, ScrivenerReader.Read(root).Scenes.Select(s => s.ChapterKey).Distinct().Count());
    }

    [Fact]
    public void AnImportedFileThatIsNoLongerThereContributesNothing()
    {
        // The binder still lists it; the bytes are gone. A research item
        // pointing at nothing is worse than no research item.
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title></BinderItem>
            <BinderItem UUID="R" Type="ResearchFolder"><Title>Research</Title><Children>
              <BinderItem UUID="P1" Type="PDF"><Title>Missing survey</Title></BinderItem>
              <BinderItem UUID="I1" Type="Image"><Title>Missing map</Title></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Research);
    }

    [Fact]
    public void APictureIsFoundWhateverImageFormatItWasSavedIn()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title></BinderItem>
            <BinderItem UUID="R" Type="ResearchFolder"><Title>Research</Title><Children>
              <BinderItem UUID="I1" Type="Image"><Title>A photograph</Title></BinderItem>
            </Children></BinderItem>
            """);
        var folder = Path.Combine(root, "Files", "Data", "I1");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "content.jpg"), [0xFF, 0xD8, 0xFF]);

        var image = ScrivenerReader.Read(root).Research.Single();

        Assert.Equal(ScrivenerResearchKind.Image, image.Kind);
        Assert.EndsWith("content.jpg", image.SourcePath);
    }

    [Fact]
    public void ANonexistentPathReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(Path.Combine(_dir.Path, "nope.scriv")).IsEmpty);
    }

    [Fact]
    public void CustomMetadataWrittenWithFieldIdAndValueChildrenIsReadToo()
    {
        // Scrivener has written the item both ways across its 3.x line.
        var root = NewProject();
        WriteScrivx(
            root,
            """
            <BinderItem UUID="D" Type="DraftFolder"><Title>Draft</Title><Children>
              <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
                <BinderItem UUID="S1" Type="Text"><Title>Scene</Title><MetaData>
                  <CustomMetaData><MetaDataItem>
                    <FieldID>tension</FieldID><Value>High</Value>
                  </MetaDataItem></CustomMetaData>
                </MetaData></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """,
            "<CustomMetaDataSettings><MetaDataField ID=\"tension\"><Title>Tension</Title>"
            + "</MetaDataField></CustomMetaDataSettings>");
        WriteDoc3(root, "S1", "Prose.");

        Assert.Equal("High", ScrivenerReader.Read(root).Scenes.Single().CustomFields["tension"]);
    }

    [Fact]
    public void ACustomFieldWithNoIdIsIgnored()
    {
        var root = NewProject();
        WriteScrivx(
            root,
            "<BinderItem UUID=\"D\" Type=\"DraftFolder\"><Title>Draft</Title></BinderItem>",
            "<CustomMetaDataSettings><MetaDataField><Title>Nameless</Title>"
            + "</MetaDataField></CustomMetaDataSettings>");

        Assert.Empty(ScrivenerReader.Read(root).CustomFields);
    }

    [Fact]
    public void ACustomFieldWithNoTitleIsCalledByItsId()
    {
        var root = NewProject();
        WriteScrivx(
            root,
            "<BinderItem UUID=\"D\" Type=\"DraftFolder\"><Title>Draft</Title></BinderItem>",
            "<CustomMetaDataSettings><MetaDataField ID=\"tension\"/></CustomMetaDataSettings>");

        Assert.Equal("tension", ScrivenerReader.Read(root).CustomFields.Single().Title);
    }

    // ── Real Scrivener RTF ──

    [Fact]
    public void RealScrivenerRtfDecodesPunctuationAndNeverLeaksInternalMarkers()
    {
        var root = ScrivenerProjectBuilder.CopyRealFormattingFixture(_dir.Path);

        var scene = Assert.Single(ScrivenerReader.Read(root).Scenes);

        Assert.Equal("Scene 1", scene.Title);
        Assert.Contains("“Lorem ipsum!”", scene.Text);
        Assert.Contains("Volupta’s aliqua—dolores esse…", scene.Text);
        Assert.DoesNotContain("'93", scene.Text);
        Assert.DoesNotContain("$Scr_", scene.Text);
        Assert.False(scene.Text.StartsWith("**", StringComparison.Ordinal));
        Assert.DoesNotContain('�', scene.Text);
    }

    [Fact]
    public void RealScrivenerRtfPreservesSemanticEditorFormatting()
    {
        var root = ScrivenerProjectBuilder.CopyRealFormattingFixture(_dir.Path);

        var scene = Assert.Single(ScrivenerReader.Read(root).Scenes);

        Assert.Contains("<p class=\"nv-style-heading\">Prologue</p>", scene.Html);
        Assert.Contains("<ul><li>A bullet from the real project.</li></ul>", scene.Html);
        Assert.Contains("<ol><li>A numbered item from the real project.</li></ol>", scene.Html);
        Assert.Contains("font-weight:bold", scene.Html);
        Assert.Contains("font-style:italic", scene.Html);
        Assert.DoesNotContain("$Scr_", scene.Html);
    }

    [Fact]
    public void RealScrivenerNamedStylesBecomeSafeMarkdownForResearch()
    {
        var root = ScrivenerProjectBuilder.CopyRealFormattingFixture(_dir.Path);

        var research = Assert.Single(ScrivenerReader.Read(root).Research);

        Assert.Equal("Style Guide", research.Title);
        Assert.StartsWith("# **Dignissimos in blanditiis**", research.MarkdownText);
        Assert.Contains("## **\\[Dignissimos in blanditiis\\]**", research.MarkdownText);
        Assert.Contains("**Nisi cupiditate:**", research.MarkdownText);
        Assert.Contains("*cupidatat vitae lorem sequi do corrupti ipsam.*", research.MarkdownText);
        Assert.DoesNotContain("$Scr_", research.Text);
        Assert.DoesNotContain("$Scr_", research.MarkdownText);
    }

    // ── Named styles resolved through styles.xml ──

    /// <summary>
    /// Writes a project whose single scene carries raw RTF, a per-document
    /// style list, and a styles.xml naming the two paragraph styles Novalist
    /// maps by name rather than by formatting.
    /// </summary>
    private string NewStyledProject(string sceneRtf)
    {
        var root = NewProject();
        WriteScrivx(root,
            """
            <BinderItem UUID="DRAFT" ID="1" Type="DraftFolder"><Title>Manuscript</Title><Children>
              <BinderItem UUID="S1" ID="2" Type="Text"><Title>Styled</Title></BinderItem>
            </Children></BinderItem>
            """);

        var files = Path.Combine(root, "Files");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "styles.xml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Styles>
              <Style ID="QUOTE-UUID" Name="Block Quote" Type="Paragraph"><Format>{\rtf1 sample\par}</Format></Style>
              <Style ID="VERSE-UUID" Name="Verse" Type="Paragraph"><Format>{\rtf1 sample\par}</Format></Style>
              <Style ID="EMPH-UUID" Name="Author Emphasis" Type="Character"><Format>{\rtf1\b sample\par}</Format></Style>
            </Styles>
            """,
            Encoding.UTF8);

        var folder = Path.Combine(files, "Data", "S1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.rtf"), sceneRtf);
        File.WriteAllText(
            Path.Combine(folder, "content.styles"), "QUOTE-UUID, VERSE-UUID, EMPH-UUID");
        return root;
    }

    [Fact]
    public void StyleNamesScrivenerShipsWithBecomeNovalistParagraphStyles()
    {
        // Index 0 and 1 address content.styles, whose UUIDs resolve through
        // styles.xml - the indirection that makes a bare <$Scr_Ps::N> meaningful.
        var root = NewStyledProject(
            @"{\rtf1\ansi <$Scr_Ps::0>Quoted line<!$Scr_Ps::0>\par" +
            @"<$Scr_Ps::1>Versed line<!$Scr_Ps::1>\par}");

        var scene = Assert.Single(ScrivenerReader.Read(root).Scenes);

        Assert.Equal(
            "<p class=\"nv-style-blockquote\">Quoted line</p>"
            + "<p class=\"nv-style-poetry\">Versed line</p>",
            scene.Html);
        Assert.DoesNotContain("$Scr_", scene.Html);
    }

    [Fact]
    public void AMarkerLeavesNoEmptyRunAndNoSplitInTheProseAroundIt()
    {
        // The markers sit inside styled runs, so stripping them leaves a
        // whitespace-only run at each end of the first paragraph, cuts the prose
        // either side of a heading marker into two runs of identical style, and
        // applies a character style to the middle of the third.
        var root = NewStyledProject(
            @"{\rtf1\ansi {\b <$Scr_Ps::0> }Real text{\i  <!$Scr_Ps::0>}\par " +
            @"One <$Scr_H::1>two<!$Scr_H::1>\par " +
            @"Plain <$Scr_Cs::2>emphasised<!$Scr_Cs::2> plain.\par}");

        var document = ScrivenerReader.Read(root);
        var scene = Assert.Single(document.Scenes);

        Assert.Contains("<p class=\"nv-style-blockquote\">Real text</p>", scene.Html);
        Assert.Contains("<p class=\"nv-style-heading\">One two</p>", scene.Html);
        Assert.Contains(
            "<p>Plain <span style=\"font-weight:bold\">emphasised</span> plain.</p>", scene.Html);
        // Merged rather than left as two adjacent spans of the same style.
        Assert.DoesNotContain("</span><span", scene.Html);
    }

    [Fact]
    public void ASceneBreakSurvivesTheNamedStylePassIntact()
    {
        var root = NewStyledProject(
            @"{\rtf1\ansi Before the break.\par * * *\par After the break.\par}");

        var scene = Assert.Single(ScrivenerReader.Read(root).Scenes);

        Assert.Equal(
            "<p>Before the break.</p><p>***</p><p>After the break.</p>", scene.Html);
    }

    // ── The binder outline, and sending rows where the writer wants ──

    private string OldDrafts() => ScrivenerProjectBuilder.CopyOldDraftsFixture(_dir.Path);

    private static ScrivenerBinderRow Row(
        IReadOnlyList<ScrivenerBinderRow> rows, string key)
        => Assert.Single(rows, r => r.Key == key);

    [Fact]
    public void TheOutlineOffersTheTopLevelAndTheLevelBelowItAndNothingDeeper()
    {
        var rows = ScrivenerReader.Outline(OldDrafts());

        Assert.Equal("Novel Format", rows[0].Title);
        Assert.Equal(0, rows[0].Depth);
        Assert.All(rows, r => Assert.InRange(r.Depth, 0, 1));

        // "Old" is one row and its nine drafts, its notes and its deleted
        // scenes are eleven more - the whole point of going one level down.
        Assert.Equal(0, Row(rows, "OLD").Depth);
        Assert.Equal(1, Row(rows, "D6").Depth);
        // A chapter inside a draft is the binder's business, not the writer's.
        Assert.DoesNotContain(rows, r => r.Key == "D6-C1");
    }

    [Fact]
    public void TheOutlineStartsFromWhatTheRulesWouldHaveDone()
    {
        var rows = ScrivenerReader.Outline(OldDrafts());

        Assert.Equal(ScrivenerDestination.Manuscript, Row(rows, "DRAFT").Destination);
        Assert.Equal(ScrivenerDestination.Characters, Row(rows, "CHARACTERS").Destination);
        Assert.Equal(ScrivenerDestination.Skip, Row(rows, "TRASH").Destination);
        Assert.Equal(ScrivenerDestination.Skip, Row(rows, "TEMPLATES").Destination);
        Assert.Equal(ScrivenerDestination.Research, Row(rows, "RESEARCH").Destination);

        // This is the bug, stated as a fact: nine drafts start out as research
        // because nothing in the binder says they are anything else.
        Assert.Equal(ScrivenerDestination.Research, Row(rows, "OLD").Destination);
        Assert.Equal(ScrivenerDestination.Research, Row(rows, "D6").Destination);
    }

    [Fact]
    public void AChildInheritsItsParentsDestinationUnlessItsOwnIconSaysOtherwise()
    {
        var rows = ScrivenerReader.Outline(OldDrafts());

        Assert.Equal(ScrivenerDestination.Characters, Row(rows, "CH-PRAX").Destination);
        Assert.Equal(ScrivenerDestination.Research, Row(rows, "RES1").Destination);
    }

    [Fact]
    public void TheOutlineCountsWhatEachRowIsWorth()
    {
        var rows = ScrivenerReader.Outline(OldDrafts());

        Assert.Equal(12, Row(rows, "D6").Documents);
        Assert.True(Row(rows, "D6").HasChildren);
        Assert.False(Row(rows, "NOVELFORMAT").HasChildren);
        Assert.Equal(1, Row(rows, "NOVELFORMAT").Documents);

        // The draft folder nobody has started yet is worth nothing, which is
        // the whole reason this screen exists.
        Assert.Equal(0, Row(rows, "DRAFT").Documents);
        Assert.False(Row(rows, "DRAFT").HasChildren);
    }

    [Fact]
    public void TheOutlineIsEmptyForSomethingThatIsNotAProject()
    {
        Assert.Empty(ScrivenerReader.Outline(Path.Combine(_dir.Path, "nope.scriv")));
        Assert.Empty(ScrivenerReader.Outline(NewProject()));

        // A binder file with no binder in it.
        var bare = NewProject("Bare.scriv");
        File.WriteAllText(
            Path.Combine(bare, "Book.scrivx"), "<?xml version=\"1.0\"?><ScrivenerProject/>");
        Assert.Empty(ScrivenerReader.Outline(bare));
    }

    [Fact]
    public void AnUnreadableProjectOffersNoRowsRatherThanFailingTheDialog()
    {
        var root = NewProject("Broken.scriv");
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<ScrivenerProject><Binder>");

        Assert.Empty(ScrivenerReader.Outline(root));
    }

    [Fact]
    public void ADocumentInsideTheDraftIsOfferedAsManuscriptRatherThanAsResearch()
    {
        // The empty-draft fixture cannot show this, and it is the ordinary case:
        // a row below the draft folder inherits the draft, not the top level.
        var rows = ScrivenerReader.Outline(ScrivenerProjectBuilder.BuildV3(_dir.Path));

        var part = Assert.Single(rows, r => r.Key == "PART1");
        Assert.Equal(1, part.Depth);
        Assert.Equal(ScrivenerDestination.Manuscript, part.Destination);

        // Even a loose document with no folder around it.
        Assert.Equal(
            ScrivenerDestination.Manuscript, Assert.Single(rows, r => r.Key == "S5").Destination);
    }

    [Fact]
    public void ADraftFolderLeftEmptyImportsTheWholeBinderAsResearchUntilItIsMapped()
    {
        // The bug as the writer met it: nothing is manuscript, and nine drafts
        // and every chapter in them arrive as research notes.
        var project = ScrivenerReader.Read(OldDrafts());

        Assert.Empty(project.Scenes);
        Assert.Contains(project.Research, r => r.Title == "Solarian High Council Meeting");
    }

    [Fact]
    public void AFolderMappedToADraftBecomesADraftOfItsOwnWithItsChaptersIntact()
    {
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["D6"] = ScrivenerDestination.Draft
        });

        var scenes = project.Scenes;
        Assert.Equal(12, scenes.Count);
        Assert.All(scenes, s => Assert.Equal(ScrivenerTargetKind.Draft, s.TargetKind));
        Assert.All(scenes, s => Assert.Equal("Old Draft 6- Started 10/30/2025?", s.TargetTitle));

        // The folder names the draft, so its children are the chapters rather
        // than the whole thing collapsing into one.
        Assert.Equal(6, scenes.Select(s => s.ChapterKey).Distinct().Count());
        Assert.Equal("In The Beginning...", scenes[0].ChapterTitle);
        Assert.Equal("Ziusudra sends distress signal", scenes[0].Title);
        Assert.Equal(4, scenes.Count(s => s.ChapterTitle == "Chapter 5: Contact"));
    }

    [Fact]
    public void EveryOldDraftCanGoToADraftOfItsOwnAtOnce()
    {
        var mapping = new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9" }
            .ToDictionary(k => k, _ => ScrivenerDestination.Draft);

        var project = ScrivenerReader.Read(OldDrafts(), mapping);

        Assert.Equal(9, project.Scenes.Select(s => s.TargetKey).Distinct().Count());
        // Binder order, which is the order the writer sees them in.
        Assert.Equal("Old Draft 9- Started 6/13/2026", project.Scenes[0].TargetTitle);

        // The rows beside them were not named, so they keep the destination the
        // rules gave them rather than being dragged along.
        Assert.Contains(project.Research, r => r.Title == "What I kept from draft 3");
        Assert.Contains(project.Research, r => r.Title == "The market that went nowhere");
    }

    [Fact]
    public void SettingAFolderAndThenOneRowInsideItLeavesTheRestWhereTheFolderPutThem()
    {
        // The shape the dialog sends once a folder has been set and one row put
        // back: the folder and every row inside it named, one of them
        // disagreeing. The disagreeing row must win without dragging the rest
        // back to where they were detected.
        var mapping = new Dictionary<string, ScrivenerDestination>
        {
            ["OLD"] = ScrivenerDestination.Draft,
            ["OLDNOTES"] = ScrivenerDestination.Research,
            ["DELETED"] = ScrivenerDestination.Draft
        };
        foreach (var key in new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9" })
            mapping[key] = ScrivenerDestination.Draft;

        var project = ScrivenerReader.Read(OldDrafts(), mapping);

        // Nine drafts and the deleted-scenes folder, and not the notes.
        Assert.Equal(10, project.Scenes.Select(s => s.TargetKey).Distinct().Count());
        Assert.DoesNotContain(project.Scenes, s => s.TargetKey == "OLDNOTES");
        Assert.Contains(project.Research, r => r.Title == "What I kept from draft 3");
        Assert.DoesNotContain(project.Research, r => r.Title == "The market that went nowhere");
    }

    [Fact]
    public void AnUnnamedSiblingKeepsTheFolderTagItWouldHaveHadAnyway()
    {
        var tagged = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["D6"] = ScrivenerDestination.Draft
        });

        // "Old Notes" was not named, so routing its siblings individually must
        // not cost it the tag it gets when the parent is walked whole.
        var note = Assert.Single(tagged.Research, r => r.Title == "What I kept from draft 3");
        Assert.Equal("Old Notes", note.FolderTag);
    }

    [Fact]
    public void AFolderMappedToABookIsMarkedAsOneRatherThanAsADraft()
    {
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["D1"] = ScrivenerDestination.Book
        });

        Assert.All(project.Scenes, s => Assert.Equal(ScrivenerTargetKind.Book, s.TargetKind));
        Assert.All(project.Scenes, s => Assert.Equal("Old Draft 1- Started 04/2021", s.TargetTitle));
    }

    [Fact]
    public void AFolderMappedToTheManuscriptKeepsItsOwnGroupingAsAnAct()
    {
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["D6"] = ScrivenerDestination.Manuscript
        });

        // Merged into a book that already has chapters, so the draft's own name
        // survives as the act rather than its chapters landing loose among them.
        Assert.All(project.Scenes, s => Assert.Equal(ScrivenerTargetKind.Manuscript, s.TargetKind));
        Assert.All(project.Scenes,
            s => Assert.Equal("Old Draft 6- Started 10/30/2025?", s.PartTitle));
        Assert.Equal(6, project.Scenes.Select(s => s.ChapterKey).Distinct().Count());
    }

    [Fact]
    public void AFolderCanBeSentToTheCodexOrLeftBehindEntirely()
    {
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["WORLDBUILDING"] = ScrivenerDestination.Places,
            ["NOTES"] = ScrivenerDestination.Skip,
            ["IDEAS"] = ScrivenerDestination.Characters
        });

        Assert.Contains(project.Entities,
            e => e.Name == "The Swarm" && e.Kind == ScrivenerEntityKind.Location);
        Assert.Contains(project.Entities,
            e => e.Name == "Second swarm" && e.Kind == ScrivenerEntityKind.Character);
        Assert.Contains("Notes", project.Losses);
        Assert.DoesNotContain(project.Research, r => r.Title == "Timeline questions");
    }

    [Fact]
    public void SomethingScrivenerMarkedItselfCanStillBeOverriddenByHand()
    {
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["TRASH"] = ScrivenerDestination.Research,
            ["CHARACTERS"] = ScrivenerDestination.Skip
        });

        Assert.Contains(project.Research, r => r.Title == "Abandoned opening");
        Assert.DoesNotContain("Trash", project.Losses);
        Assert.Empty(project.Entities);
        Assert.Contains("Characters", project.Losses);
    }

    [Fact]
    public void ADraftThatNeverGotChapterFoldersStillBringsItsDocuments()
    {
        // Four of the nine drafts are a flat run of documents - how a draft
        // looks before it has been organised. Every one of those documents went
        // missing: they all landed in the one shared "Imported" chapter, which
        // belonged to whichever draft reached it first, so three of the four
        // drafts were created empty.
        var loose = new[] { "D9", "D7", "D3", "D1" };
        var project = ScrivenerReader.Read(
            OldDrafts(), loose.ToDictionary(k => k, _ => ScrivenerDestination.Draft));

        foreach (var key in loose)
        {
            var scenes = project.Scenes.Where(s => s.TargetKey == key).ToList();
            Assert.Equal(3, scenes.Count);
            Assert.Equal(
                new[] { "Opening", "The signal arrives", "They argue about it" },
                scenes.Select(s => s.Title).ToArray());
            // One chapter, and it is this draft's own rather than one shared
            // with every other draft that had loose documents.
            Assert.Single(scenes.Select(s => s.ChapterKey).Distinct());
        }

        Assert.Equal(4, project.Scenes.Select(s => s.ChapterKey).Distinct().Count());
    }

    [Fact]
    public void SayingResearchOverAFolderOfSketchesMeansResearch()
    {
        // The icons say these are characters, and left alone that is what they
        // become. Saying otherwise has to actually win, or the choice is a
        // suggestion the importer is free to ignore.
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["CHARACTERS"] = ScrivenerDestination.Research
        });

        Assert.Empty(project.Entities);
        Assert.Contains(project.Research, r => r.Title == "Prax" && r.FolderTag == "Characters");

        // Untouched, they are still characters.
        Assert.Equal(3, ScrivenerReader.Read(OldDrafts()).Entities.Count);
    }

    [Fact]
    public void AMappingThatNamesNothingInTheBinderChangesNothing()
    {
        var mapped = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["not-a-binder-key"] = ScrivenerDestination.Manuscript
        });

        Assert.Empty(mapped.Scenes);
        Assert.Equal(ScrivenerReader.Read(OldDrafts()).Research.Count, mapped.Research.Count);
    }

    [Fact]
    public void MappingADraftDocumentDoesNotTurnProseIntoACodexEntry()
    {
        // A document inside something bound for the manuscript is a scene
        // whatever icon it carries, or a character sheet kept in the draft
        // would quietly stop being prose.
        var project = ScrivenerReader.Read(OldDrafts(), new Dictionary<string, ScrivenerDestination>
        {
            ["CHARACTERS"] = ScrivenerDestination.Draft
        });

        Assert.Empty(project.Entities);
        Assert.Equal(3, project.Scenes.Count);
        Assert.All(project.Scenes, s => Assert.Equal(ScrivenerTargetKind.Draft, s.TargetKind));
    }
}
