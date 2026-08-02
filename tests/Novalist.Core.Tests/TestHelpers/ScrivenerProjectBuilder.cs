using System.Text;

namespace Novalist.Core.Tests.TestHelpers;

/// <summary>
/// Builds a Scrivener project on disk that exercises every part of the binder
/// the importer reads.
///
/// Modelled on a real project created by Scrivener 3 from its own novel
/// template, then filled in: the same <c>Type</c> attributes, the same icon
/// names, the same label and status vocabularies, the same
/// <c>TemplateFolderUUID</c>, and the same trap that the template sets - every
/// part is titled "Part" and every chapter "Chapter", so anything grouping by
/// title collapses the book into one chapter.
///
/// <see cref="BuildV3"/> writes the Scrivener 3 layout (a UUID folder per
/// document); <see cref="BuildV2"/> writes the same binder in the Scrivener 2
/// one (numbered files with the suffix in the name), which is the layout that
/// carried document notes and synopses nothing used to read.
/// </summary>
public static class ScrivenerProjectBuilder
{
    /// <summary>The UUID of the Template Sheets folder, which the project
    /// declares and the importer must skip.</summary>
    public const string TemplateFolderUuid = "TPL-FOLDER";

    /// <summary>Minimal RTF carrying one paragraph, which is what Scrivener
    /// writes for a document.</summary>
    public static string Rtf(string text)
        => "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Times;}}\\f0\\fs24 " + text + "\\par}";

    /// <summary>
    /// The binder every fixture shares. Ids are the same in both layouts; only
    /// where the bytes live differs.
    /// </summary>
    private const string BinderXml = """
        <BinderItem UUID="INTRO" ID="1" Type="Text">
          <Title>Novel Format</Title>
          <MetaData><IconFileName>Information</IconFileName></MetaData>
        </BinderItem>
        <BinderItem UUID="DRAFT" ID="2" Type="DraftFolder">
          <Title>Manuscript</Title>
          <Children>
            <BinderItem UUID="PART1" ID="3" Type="Folder"><Title>Part</Title><Children>
              <BinderItem UUID="CH1" ID="4" Type="Folder"><Title>Chapter</Title><Children>
                <BinderItem UUID="S1" ID="5" Type="Text">
                  <Title>Arrival</Title>
                  <MetaData>
                    <IncludeInCompile>Yes</IncludeInCompile>
                    <LabelID>7</LabelID>
                    <StatusID>3</StatusID>
                    <CustomMetaData>
                      <MetaDataItem ID="tension">High</MetaDataItem>
                      <MetaDataItem ID="pov">Mira</MetaDataItem>
                      <MetaDataItem ID="unknown-field">ignored</MetaDataItem>
                    </CustomMetaData>
                  </MetaData>
                </BinderItem>
                <BinderItem UUID="S2" ID="6" Type="Text"><Title>The Inn</Title></BinderItem>
              </Children></BinderItem>
              <BinderItem UUID="CH2" ID="7" Type="Folder"><Title>Chapter</Title><Children>
                <BinderItem UUID="S3" ID="8" Type="Text">
                  <Title>Departure</Title>
                  <MetaData><IncludeInCompile>No</IncludeInCompile></MetaData>
                </BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            <BinderItem UUID="PART2" ID="9" Type="Folder"><Title>Part</Title><Children>
              <BinderItem UUID="CH3" ID="10" Type="Folder"><Title>Chapter</Title><Children>
                <BinderItem UUID="S4" ID="11" Type="Text"><Title>Return</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            <BinderItem UUID="S5" ID="12" Type="Text"><Title>Loose scene</Title></BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="CHARS" ID="13" Type="Folder">
          <Title>Characters</Title>
          <MetaData><IconFileName>Characters (Photo)</IconFileName></MetaData>
          <Children>
            <BinderItem UUID="E1" ID="14" Type="Text">
              <Title>Mira Vance</Title>
              <MetaData><IconFileName>Characters (Character Sheet)</IconFileName></MetaData>
            </BinderItem>
            <BinderItem UUID="GRP" ID="15" Type="Folder"><Title>The Vances</Title><Children>
              <BinderItem UUID="E2" ID="16" Type="Text"><Title>Tomas Vance</Title></BinderItem>
            </Children></BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="PLACES" ID="17" Type="Folder">
          <Title>Places</Title>
          <MetaData><IconFileName>Locations (Map)</IconFileName></MetaData>
          <Children>
            <BinderItem UUID="E3" ID="18" Type="Text"><Title>Hillsford</Title></BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="FRONT" ID="19" Type="Folder">
          <Title>Front Matter</Title>
          <MetaData><IconFileName>Front Matter</IconFileName></MetaData>
          <Children>
            <BinderItem UUID="FM1" ID="20" Type="Text"><Title>Dedication</Title></BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="RESEARCH" ID="21" Type="ResearchFolder">
          <Title>Research</Title>
          <Children>
            <BinderItem UUID="SRC" ID="22" Type="Folder"><Title>Sources</Title><Children>
              <BinderItem UUID="R1" ID="23" Type="PDF"><Title>Harbour survey</Title></BinderItem>
              <BinderItem UUID="R2" ID="24" Type="Image"><Title>Map scan</Title></BinderItem>
              <BinderItem UUID="R3" ID="25" Type="Text"><Title>Tide tables</Title></BinderItem>
              <BinderItem UUID="R4" ID="30" Type="Other">
                <Title>Harbourmaster interview</Title>
                <MetaData><FileExtension>m4a</FileExtension></MetaData>
              </BinderItem>
              <BinderItem UUID="R5" ID="31" Type="Other">
                <Title>Tonnage returns</Title>
                <MetaData><FileExtension>csv</FileExtension></MetaData>
              </BinderItem>
              <BinderItem UUID="R6" ID="32" Type="Other">
                <Title>Harbour walk-through</Title>
                <MetaData><FileExtension>mp4</FileExtension></MetaData>
              </BinderItem>
            </Children></BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="TPL-FOLDER" ID="26" Type="Folder">
          <Title>Template Sheets</Title>
          <Children>
            <BinderItem UUID="T1" ID="27" Type="Text">
              <Title>Character Sketch</Title>
              <MetaData><IconFileName>Characters (Character Sheet)</IconFileName></MetaData>
            </BinderItem>
          </Children>
        </BinderItem>
        <BinderItem UUID="TRASH" ID="28" Type="TrashFolder">
          <Title>Trash</Title>
          <Children>
            <BinderItem UUID="X1" ID="29" Type="Text"><Title>Cut scene</Title></BinderItem>
          </Children>
        </BinderItem>
        """;

    private const string SettingsXml = """
        <LabelSettings><Labels>
          <Label ID="-1">No Label</Label>
          <Label ID="7">Red</Label>
          <Label ID="8">Orange</Label>
        </Labels></LabelSettings>
        <StatusSettings><StatusItems>
          <Status ID="-1">No Status</Status>
          <Status ID="2">First Draft</Status>
          <Status ID="3">Revised Draft</Status>
        </StatusItems></StatusSettings>
        <CustomMetaDataSettings>
          <MetaDataField ID="tension" Type="List">
            <Title>Tension</Title>
            <ListItems><ListItem>Low</ListItem><ListItem>High</ListItem></ListItems>
          </MetaDataField>
          <MetaDataField ID="pov" Type="Text"><Title>POV</Title></MetaDataField>
        </CustomMetaDataSettings>
        """;

    /// <summary>The prose each document carries. Documents absent from this map
    /// are outline placeholders with no file at all, which is how a Scrivener
    /// project spends most of its life.</summary>
    private static readonly Dictionary<string, string> Prose = new()
    {
        ["INTRO"] = "How this template works.",
        ["S1"] = "She arrived at dusk.",
        ["S2"] = "The inn was full.",
        ["S3"] = "They left before dawn.",
        // S4 "Return" and S5 "Loose scene" are deliberately empty.
        ["E1"] = "Mira Vance, harbourmaster.",
        ["E2"] = "Tomas Vance, her brother.",
        ["E3"] = "Hillsford, a town of two piers.",
        ["FM1"] = "For everyone who waited.",
        ["R3"] = "Spring tides run to four metres.",
        ["T1"] = "Name:\nRole:\nAppearance:",
        ["X1"] = "A scene that was cut.",
    };

    private static readonly Dictionary<string, string> Synopses = new()
    {
        ["S1"] = "She arrives and everything changes.",
    };

    private static readonly Dictionary<string, string> Notes = new()
    {
        ["S1"] = "Check the tide table against chapter four.",
        ["E1"] = "Do not reveal the brother until part two.",
    };

    /// <summary>Numeric ids, for the Scrivener 2 layout.</summary>
    private static readonly Dictionary<string, string> Ids = new()
    {
        ["INTRO"] = "1", ["S1"] = "5", ["S2"] = "6", ["S3"] = "8", ["S4"] = "11", ["S5"] = "12",
        ["E1"] = "14", ["E2"] = "16", ["E3"] = "18", ["FM1"] = "20",
        ["R1"] = "23", ["R2"] = "24", ["R3"] = "25", ["T1"] = "27", ["X1"] = "29",
        ["R4"] = "30", ["R5"] = "31", ["R6"] = "32",
    };

    /// <summary>The Scrivener 3 layout: a UUID folder per document.</summary>
    public static string BuildV3(string parent, string name = "Exhaustive.scriv")
    {
        var root = NewRoot(parent, name);
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));

        foreach (var (uuid, text) in Prose) WriteV3(root, uuid, "content.rtf", Rtf(text));
        foreach (var (uuid, text) in Synopses) WriteV3(root, uuid, "synopsis.txt", text);
        foreach (var (uuid, text) in Notes) WriteV3(root, uuid, "notes.rtf", Rtf(text));

        // The two file-backed research items, whose bytes the import copies.
        WriteV3Bytes(root, "R1", "content.pdf", "%PDF-1.4 fake"u8.ToArray());
        WriteV3Bytes(root, "R2", "content.png", PngBytes);
        WriteV3Bytes(root, "R4", "content.m4a", "fake audio"u8.ToArray());
        WriteV3Bytes(root, "R5", "content.csv", "port,tonnage"u8.ToArray());
        WriteV3Bytes(root, "R6", "content.mp4", "fake video"u8.ToArray());
        return root;
    }

    /// <summary>The Scrivener 2 layout: numbered files under Files/Docs, with
    /// the suffix baked into the filename.</summary>
    public static string BuildV2(string parent, string name = "Exhaustive2.scriv")
    {
        var root = NewRoot(parent, name);
        var docs = Path.Combine(root, "Files", "Docs");
        Directory.CreateDirectory(docs);

        foreach (var (uuid, text) in Prose)
            File.WriteAllText(Path.Combine(docs, Ids[uuid] + ".rtf"), Rtf(text));
        foreach (var (uuid, text) in Synopses)
            File.WriteAllText(Path.Combine(docs, Ids[uuid] + "_synopsis.txt"), text);
        foreach (var (uuid, text) in Notes)
            File.WriteAllText(Path.Combine(docs, Ids[uuid] + "_notes.rtf"), Rtf(text));

        File.WriteAllBytes(Path.Combine(docs, Ids["R1"] + ".pdf"), "%PDF-1.4 fake"u8.ToArray());
        File.WriteAllBytes(Path.Combine(docs, Ids["R2"] + ".png"), PngBytes);
        File.WriteAllBytes(Path.Combine(docs, Ids["R4"] + ".m4a"), "fake audio"u8.ToArray());
        File.WriteAllBytes(Path.Combine(docs, Ids["R5"] + ".csv"), "port,tonnage"u8.ToArray());
        File.WriteAllBytes(Path.Combine(docs, Ids["R6"] + ".mp4"), "fake video"u8.ToArray());
        return root;
    }

    /// <summary>A PNG signature and nothing else. The import copies the bytes
    /// without decoding them, so this is as much file as the test needs.</summary>
    private static byte[] PngBytes => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string NewRoot(string parent, string name)
    {
        var root = Path.Combine(parent, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, Path.GetFileNameWithoutExtension(name) + ".scrivx"),
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <ScrivenerProject Version="2.0">
               <Binder>{BinderXml}</Binder>
               <TemplateFolderUUID>{TemplateFolderUuid}</TemplateFolderUUID>
               {SettingsXml}
             </ScrivenerProject>
             """,
            Encoding.UTF8);
        return root;
    }

    private static void WriteV3(string root, string uuid, string file, string text)
    {
        var folder = Path.Combine(root, "Files", "Data", uuid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), text);
    }

    private static void WriteV3Bytes(string root, string uuid, string file, byte[] bytes)
    {
        var folder = Path.Combine(root, "Files", "Data", uuid);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), bytes);
    }
}
