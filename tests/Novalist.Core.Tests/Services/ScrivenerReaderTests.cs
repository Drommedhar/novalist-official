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
    public void AProjectFolderIsRecognised()
    {
        Assert.True(ScrivenerReader.LooksLikeScrivener(NewProject()));
    }

    [Fact]
    public void TheScrivxInsideIsRecognisedToo()
    {
        // A file picker gives one or the other depending on the platform.
        var root = NewProject();
        WriteScrivx(root, "");

        Assert.True(ScrivenerReader.LooksLikeScrivener(Path.Combine(root, "Book.scrivx")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAProject(string path)
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener(path));
    }

    [Fact]
    public void AnOrdinaryFolderIsNotAProject()
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener(_dir.Path));
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
    public void AMalformedScrivxReadsAsEmptyRatherThanThrowing()
    {
        // An import that cannot start should say so in the dialog, not crash.
        var root = NewProject();
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<not xml at all");

        Assert.True(ScrivenerReader.Read(root).IsEmpty);
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
}
