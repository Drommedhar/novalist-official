using System.Text.Json;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class PluginImportIntegrationTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _vault;
    private readonly string _out;

    public PluginImportIntegrationTests()
    {
        _vault = Path.Combine(_dir.Path, "vault");
        _out = Path.Combine(_dir.Path, "out");
        Directory.CreateDirectory(_vault);
        Directory.CreateDirectory(_out);
    }

    public void Dispose() => _dir.Dispose();

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_vault, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void WriteBytes(string relative, byte[] bytes)
    {
        var full = Path.Combine(_vault, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private const string DataJson = """
    {
      "worldBiblePath": "WorldBible",
      "novalistRoot": "",
      "language": "de",
      "wordCountGoals": { "dailyGoal": 500, "projectGoal": 80000 },
      "relationshipPairs": { "parent": ["child", "kid"] },
      "autoReplacements": [ { "start": "--", "replacement": "—" } ],
      "characterTemplates": [
        { "id": "tmpl1", "ageMode": "date", "ageIntervalUnit": "Years" },
        { "id": "tmpl2", "ageMode": "manual" }
      ],
      "activeCharacterTemplateId": "tmpl1"
    }
    """;

    private const string AliceSheet = """
    # Alice Smith

    ## CharacterSheet
    Name: Alice
    Surname: Smith
    Gender: Female
    Age: 1990-05-20
    Role: Hero
    Group: Guild
    EyeColor: Blue
    HairColor: Brown
    HairLength: Long
    Height: 170
    Build: Slim
    SkinTone: Fair
    DistinguishingFeatures: Scar
    TemplateId: tmpl1
    Relationships:
    - friend: [[Bob]]
    Images:
    - portrait: ![[Images/pic.png]]
    CustomProperties:
    - mood: happy
    Sections:
    Backstory
    Born in a village.
    ChapterOverrides:
    Chapter: 01 - Intro
    - act: II
    - age: 35
    - name: Ally
    - surname: S
    - gender: F
    - role: Lead
    - eyecolor: Green
    - haircolor: Black
    - hairlength: Short
    - height: 171
    - build: Athletic
    - skintone: Tan
    - distinguishingfeatures: Tattoo
    - scene: 2
    """;

    private void BuildRichVault()
    {
        Write(".obsidian/plugins/novalist/data.json", DataJson);

        Write("Chapters/01 - Intro.md", """
        ---
        guid: ch-1
        order: 1
        status: first-draft
        act: "I"
        date: 2024-01-01
        ---
        # Intro

        ## Opening
        It was a **dark** night.

        ## Arrival
        She arrived at the [[City]].
        """);

        // Chapter with no frontmatter and no H2 -> single scene from body
        Write("Chapters/02 - Quiet.md", """
        # Quiet Chapter

        Just a single block of prose with no scene headings.
        """);

        Write("Characters/Alice.md", AliceSheet);
        Write("Locations/City.md", """
        # City

        ## LocationSheet
        Name: City
        Type: Urban
        Parent: [[Region]]
        Description:
        A big city.
        Images:
        - view: ![[Images/pic.png]]
        CustomProperties:
        - size: large
        Sections:
        History
        Founded long ago.
        """);
        Write("Locations/Region.md", """
        # Region

        ## LocationSheet
        Name: Region
        Type: Area
        """);
        Write("Items/Sword.md", """
        # Sword

        ## ItemSheet
        Name: Sword
        Type: Weapon
        Origin: Forge
        Description: A sharp blade.
        """);
        Write("Lore/Magic.md", """
        # Magic

        ## LoreSheet
        Name: Magic
        Category: System
        Description: How magic works.
        """);
        // Entity file with no sheet section -> name only from H1
        Write("Lore/Empty.md", "# Empty\n\nNo sheet here.");

        // Character with a non-matching template id (ApplyTemplateDateMode skip)
        // and a bare-filename image (image-remap vault-fallback path).
        Write("Characters/Cara.md", """
        # Cara

        ## CharacterSheet
        Name: Cara
        TemplateId: tmpl2
        Images:
        - loose: ![[loose.png]]
        - bundled: ![[pic.png]]
        """);

        WriteBytes("Images/pic.png", new byte[] { 1, 2, 3 });
        WriteBytes("Images/sub/nested.png", new byte[] { 4, 5 }); // CopyImageFiles recursion
        WriteBytes("loose.png", new byte[] { 6 });                // bare filename resolved from vault root

        // World bible — entities of every type + images
        Write("WorldBible/Characters/Bob.md", "# Bob\n\n## CharacterSheet\nName: Bob");
        Write("WorldBible/Locations/Realm.md", "# Realm\n\n## LocationSheet\nName: Realm");
        Write("WorldBible/Items/Relic.md", "# Relic\n\n## ItemSheet\nName: Relic");
        Write("WorldBible/Lore/Saga.md", "# Saga\n\n## LoreSheet\nName: Saga");
        WriteBytes("WorldBible/Images/wb.png", new byte[] { 7 });
    }

    [Fact]
    public async Task ImportAsync_RichVault_BuildsStandaloneProject()
    {
        BuildRichVault();
        var sut = new PluginImportService();
        var steps = new List<string>();
        sut.ProgressChanged = (s, _, _) => steps.Add(s);

        var result = await sut.ImportAsync(_vault, "", _out, "My Project", "Book One");

        var projectDir = result.ProjectPath;
        Assert.True(File.Exists(Path.Combine(projectDir, ".novalist", "project.json")));
        Assert.True(File.Exists(Path.Combine(projectDir, ".novalist", "settings.json")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Book One", ".book", "scenes.json")));

        // Characters written (Alice + Cara); Alice's birth-date moved from Age (template ageMode=date)
        var charFiles = Directory.GetFiles(Path.Combine(projectDir, "Book One", "Characters"), "*.json");
        Assert.Equal(2, charFiles.Length);
        var aliceJson = charFiles.Select(File.ReadAllText).First(j => j.Contains("\"Alice\""));
        Assert.Contains("1990-05-20", aliceJson);   // moved into BirthDate
        Assert.Contains("\"ageMode\"", aliceJson);

        // World-bible character written separately
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(projectDir, "World Bible", "Characters"), "*.json"));

        // Image copied into the book image folder
        Assert.True(File.Exists(Path.Combine(projectDir, "Book One", "Images", "pic.png")));

        // Settings collected from plugin data.json
        Assert.Equal("de", result.AutoReplacementLanguage);
        Assert.Single(result.AutoReplacements);
        Assert.True(result.RelationshipPairs.ContainsKey("parent"));

        // Progress was reported
        Assert.Contains("Import complete!", steps);
        // Diagnostic log captured
        Assert.NotEmpty(sut.Log);
    }

    [Fact]
    public async Task ImportAsync_ProjectInSubfolder_NoWorldBible_NoData()
    {
        // Minimal vault: a project subfolder with only Chapters, no data.json/world bible.
        Write("Novel/Chapters/01.md", "# Chapter\n\n## Scene\nText.");
        // Entity image path prefixed with the project subfolder -> prefix is stripped.
        Write("Novel/Characters/A.md", """
        # A

        ## CharacterSheet
        Name: A
        Images:
        - p: ![[Novel/Images/x.png]]
        """);
        WriteBytes("Novel/Images/x.png", new byte[] { 1 });

        var sut = new PluginImportService();
        var result = await sut.ImportAsync(_vault, "Novel", _out, "P", "B");

        Assert.True(Directory.Exists(Path.Combine(result.ProjectPath, "B", "Chapters")));
        Assert.True(File.Exists(Path.Combine(result.ProjectPath, "B", "Images", "x.png")));
        // No plugin data -> no language override
        Assert.Null(result.AutoReplacementLanguage);
    }

    [Fact]
    public async Task ImportAsync_ChapterStatusVariants_Mapped()
    {
        Write("Chapters/a.md", "---\norder: 1\nstatus: final\n---\n# A\n\n## S\nx");
        var sut = new PluginImportService();
        var result = await sut.ImportAsync(_vault, "", _out, "P", "B");
        var project = await File.ReadAllTextAsync(Path.Combine(result.ProjectPath, ".novalist", "project.json"));
        Assert.Contains("\"status\"", project);
    }

    [Fact]
    public async Task ImportAsync_InvalidSettingsArrays_Swallowed()
    {
        // autoReplacements + characterTemplates are arrays of the wrong shape -> parse catches.
        Write(".obsidian/plugins/novalist/data.json", """
        { "autoReplacements": [ 1, 2, 3 ], "characterTemplates": [ "notatemplate" ] }
        """);
        Write("Chapters/01.md", "# C\n\n## S\nx");
        Write("Characters/A.md", "# A\n\n## CharacterSheet\nName: A");
        var sut = new PluginImportService();
        var result = await sut.ImportAsync(_vault, "", _out, "P", "B");
        Assert.Empty(result.AutoReplacements); // parse failed -> stays empty, no throw
    }

    [Fact]
    public async Task ImportAsync_CustomImageFolderName_RemapsToImages()
    {
        Write(".obsidian/plugins/novalist/data.json", """{ "imageFolder": "Bilder" }""");
        Write("Chapters/01.md", "# C\n\n## S\nx");
        Write("Characters/A.md", """
        # A

        ## CharacterSheet
        Name: A
        Images:
        - p: ![[Bilder/art.png]]
        """);
        WriteBytes("Bilder/art.png", new byte[] { 1 });

        var sut = new PluginImportService();
        var result = await sut.ImportAsync(_vault, "", _out, "P", "B");

        // Source folder "Bilder" remapped to standalone "Images"
        Assert.True(File.Exists(Path.Combine(result.ProjectPath, "B", "Images", "art.png")));
        var json = await File.ReadAllTextAsync(
            Directory.GetFiles(Path.Combine(result.ProjectPath, "B", "Characters"), "*.json")[0]);
        Assert.Contains("Images/art.png", json);
    }

    [Fact]
    public void ResolveWikilinkReferences_StripsOverrideRels_AndResolvesParentId()
    {
        var region = new Novalist.Core.Models.LocationData { Id = "loc-region", Name = "Region" };
        var city = new Novalist.Core.Models.LocationData { Id = "loc-city", Name = "City", Parent = "[[Region]]" };
        var alice = new Novalist.Core.Models.CharacterData { Id = "c1", Name = "Alice" };
        alice.ChapterOverrides.Add(new Novalist.Core.Models.CharacterOverride
        {
            Chapter = "1",
            Relationships = new() { new Novalist.Core.Models.EntityRelationship { Role = "ally", Target = "[[Bob]]" } }
        });

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Region"] = "loc-region"
        };
        PluginImportService.ResolveWikilinkReferences(new() { alice }, new() { region, city }, lookup);

        Assert.Equal("loc-region", city.Parent);                 // parent name -> id
        Assert.Equal("Bob", alice.ChapterOverrides[0].Relationships![0].Target); // wikilink stripped
    }
}
