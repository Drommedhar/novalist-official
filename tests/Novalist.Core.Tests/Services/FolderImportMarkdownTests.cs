using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class FolderImportMarkdownTests
{
    [Fact]
    public void CharacterSheet_SeparatesBaseFieldsChapterFieldsImagesAndExtraFacts()
    {
        var source = """
            # Sam North
            ## General Information
            - **Age:** 10
            - **Relationship:** Protagonist; part of a trio
            ## Further Information
            - Geburtstag: 8. Juli
            - Erscheinung: Kurzes braunes Haar, blaue Augen, etwas übergewichtig
            - Eltern: Alex North & Robin North
            - Schuljahr: 4. Klasse
            - Persönlichkeitsmerkmale: Sanftmütig, empathisch, kreativ
            - Rolle: Analytiker; besitzt ein Smartphone
            ## Images
            - Action Shot: ![[Images/Characters/Full Body/portrait.png]]
            ## Chapter Relevant Information
            - **The beginning** (Order: 1):
              - age: 6
              - relationship:
              - further_info:
              - images: - Action Shot:
              - info:
            - **The return** (Order: 3):
              - age: 10
            """;
        var book = new BookData
        {
            ActiveCharacterTemplateId = "people",
            CharacterTemplates = [new() { Id = "people", AgeMode = "date", AgeIntervalUnit = IntervalUnit.Years }],
            Chapters = [new() { Guid = "first", Title = "Prologue — The beginning", FolderName = "01 - The beginning", Order = 1 },
                new() { Guid = "return", Title = "The return", Order = 2 }]
        };
        var character = new CharacterData();
        new FolderImportParser(new FolderImportSchema(book)).Parse("Sam North.md", source, "character").Apply(character, "Source");
        Assert.Equal("Sam", character.Name);
        Assert.Equal("North", character.Surname);
        Assert.Equal("10", character.Age);
        Assert.Equal("number", character.AgeMode);
        Assert.Null(character.BirthDate);
        Assert.Equal("Protagonist; part of a trio", character.Role);
        Assert.Equal("Kurz", character.HairLength);
        Assert.Equal("braun", character.HairColor);
        Assert.Equal("blau", character.EyeColor);
        Assert.Equal("etwas übergewichtig", character.Build);
        Assert.Equal("8. Juli", character.CustomProperties["Geburtstag"]);
        Assert.Equal("4. Klasse", character.CustomProperties["Schuljahr"]);
        Assert.Equal("Analytiker; besitzt ein Smartphone", character.CustomProperties["Rolle"]);
        Assert.Equal("Sanftmütig, empathisch, kreativ", character.CustomProperties["Persönlichkeitsmerkmale"]);
        Assert.Equal(["Alex North", "Robin North"], character.Relationships.Select(relationship => relationship.Target));
        Assert.All(character.Relationships, relationship => Assert.Equal("family", relationship.Category));
        Assert.Equal("Action Shot", Assert.Single(character.Images).Name);
        Assert.Equal("Images/Characters/Full Body/portrait.png", character.Images[0].Path);
        Assert.Equal(["first", "return"], character.ChapterOverrides.Select(scope => scope.Chapter));
        Assert.Equal(["6", "10"], character.ChapterOverrides.Select(scope => scope.Age));
        Assert.Equal(source.ReplaceLineEndings("\n"), Assert.Single(character.Sections).Content);
    }

    [Fact]
    public void ChapterValues_NeverPopulateBaseFields_AndChapterOrderIsNotUsedAsIdentity()
    {
        var book = new BookData
        {
            Chapters = [new() { Guid = "first", Title = "Different chapter", Order = 1 },
                new() { Guid = "duplicate-1", Title = "Duplicate" }, new() { Guid = "duplicate-2", Title = "Duplicate" }]
        };
        var character = new CharacterData();
        var source = """
            # Sam
            ## Chapter Relevant Information
            - **Missing chapter** (Order: 1):
              - age: 6
              - relationship: explorer
              - customProperties.Skill: sailing
              - Appearance: short black hair
              - birthDate: 2014-07-08
              - tags: early
            - **Duplicate**:
              - hairColor: grey
            - **Empty**:
              - age:
              - images: - Portrait:
            - **Unsupported**:
              - birthDate: 2014-07-08
              - tags: early
            ## Biography
            Childhood memories.
            """;
        new FolderImportParser(new FolderImportSchema(book)).Parse("one.md", source, "character").Apply(character, "Source");
        Assert.Empty(character.Age);
        Assert.Empty(character.Role);
        Assert.Null(character.BirthDate);
        Assert.Empty(character.CustomProperties);
        Assert.Equal("number", character.AgeMode);
        Assert.Equal(2, character.ChapterOverrides.Count);
        Assert.Equal("Missing chapter", character.ChapterOverrides[0].Chapter);
        Assert.Equal("explorer", character.ChapterOverrides[0].Role);
        Assert.Equal("sailing", character.ChapterOverrides[0].CustomProperties!["Skill"]);
        Assert.Equal("black", character.ChapterOverrides[0].HairColor);
        Assert.Equal("Duplicate", character.ChapterOverrides[1].Chapter);
        Assert.Equal("grey", character.ChapterOverrides[1].HairColor);
        Assert.Contains(character.Sections, section => section.Title == "Biography");
    }

    [Theory]
    [InlineData("# Mononym\n## General Information\nAge: 10", "Mononym", "")]
    [InlineData("# Sam North\n## General Information\nSurname: South", "Sam North", "South")]
    [InlineData("# Sam North\n## General Information\nName: Samuel\nSurname: South", "Samuel", "South")]
    [InlineData("# Sam North\n## Biography\nAn ordinary sketch.", "Sam North", "")]
    [InlineData("## General Information\nAge: 10", "one", "")]
    public void NameSplitting_IsLimitedToCharacterSheets_AndExplicitNameFieldsWin(string source, string name, string surname)
    {
        var parsed = new FolderImportParser(new FolderImportSchema()).Parse("one.md", source, "character");
        var character = new CharacterData { Name = parsed.Title };
        parsed.Apply(character, "Source");
        Assert.Equal(name, character.Name);
        Assert.Equal(surname, character.Surname);
    }

    [Fact]
    public void ExtraFacts_AreEditableProperties_ButObjectsInternalKeysAndTableHeadersAreNot()
    {
        var source = """
            ---
            population: 40
            unknown: {nested: kept in source}
            id: original-id
            importedFrom: bogus
            ---
            # Port
            | Field | Value |
            | --- | --- |
            | Customs | Strict |
            ## History
            No facts are invented from prose.
            """;
        var location = new LocationData();
        new FolderImportParser(new FolderImportSchema()).Parse("one.md", source, "location").Apply(location, "Source");
        Assert.Equal(new Dictionary<string, string> { ["population"] = "40", ["Customs"] = "Strict" }, location.CustomProperties);
        Assert.Contains(location.Sections, section => section.Title == "History");
        var research = new ResearchItem { Properties = [] };
        new FolderImportParser(new FolderImportSchema()).Parse("one.txt", "Source: Archive\nEdition: 2", "research").Apply(research, "Source");
        Assert.Equal("Archive", research.Properties["Source"]);
        Assert.Equal("2", research.Properties["Edition"]);
    }

    [Fact]
    public void RelationshipAndImageBlocks_KeepTheirOwnScopeAndSupportMarkdownAndWikilinks()
    {
        var source = """
            # Sam
            Mother: Robin
            ## Relationships
            - Mentor: [[Alex]]
            ## Images
            - Portrait: ![A portrait](<Images/a portrait.png>)
            ![[Images/other.png|100]]
            ![](Images/third.png)
            ```
            ![[Images/not-imported.png]]
            ```
            """;
        var character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("one.md", source, "character").Apply(character, "Source");
        Assert.Equal(["Robin", "Alex"], character.Relationships.Select(relationship => relationship.Target));
        Assert.Equal("Mentor", character.Relationships[1].Role);
        Assert.Empty(character.Role);
        Assert.Equal(["Portrait", "other", "third"], character.Images.Select(image => image.Name));
        Assert.Equal("A portrait", character.Images[0].Alt);
        Assert.Equal("Images/a portrait.png", character.Images[0].Path);
        Assert.Equal(source.ReplaceLineEndings("\n"), Assert.Single(character.Sections).Content);
    }

    [Fact]
    public void AgeMode_UsesImportedNumbers_UnlessTheSourceExplicitlyChoosesDateMode()
    {
        var book = new BookData { CharacterTemplates = [new() { Id = "default", AgeMode = "date" }], ActiveCharacterTemplateId = "default" };
        var parser = new FolderImportParser(new FolderImportSchema(book));
        var character = new CharacterData();
        parser.Parse("one.md", "Age: 10", "character").Apply(character, "Source");
        Assert.Equal("number", character.AgeMode);
        character = new CharacterData();
        parser.Parse("one.md", "Age: 10\nBirth date: ''", "character").Apply(character, "Source");
        Assert.Equal("number", character.AgeMode);
        character = new CharacterData();
        parser.Parse("one.md", "Age: 10\nAge mode: date", "character").Apply(character, "Source");
        Assert.Equal("date", character.AgeMode);
    }

    [Fact]
    public void Appearance_OnlyReadsClearLabelledClauses_AndExplicitFieldsWin()
    {
        var character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("one.md", """
            # Sam
            Hair color: red
            Appearance: short brown hair, blue eyes, slightly overweight
            ## Biography
            A friend has long black hair and green eyes.
            """, "character").Apply(character, "Source");
        Assert.Equal("red", character.HairColor);
        Assert.Equal("short", character.HairLength);
        Assert.Equal("blue", character.EyeColor);
        Assert.Equal("slightly overweight", character.Build);
        Assert.Equal("short brown hair, blue eyes, slightly overweight", character.CustomProperties["Appearance"]);
        character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("one.md", """
            Appearance: blue eyes, green eyes, not brown hair, maybe short hair, hair
            Aussehen: {description: long black hair}
            """, "character").Apply(character, "Source");
        Assert.Empty(character.EyeColor);
        Assert.Empty(character.HairColor);
        Assert.Empty(character.HairLength);
        Assert.Empty(character.Build);
        character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("one.md", "---\nappearance: {description: long black hair}\n---\n## Appearance\nShort hair", "character").Apply(character, "Source");
        Assert.Equal("Short", character.HairLength);
        Assert.Empty(character.HairColor);
    }

    [Fact]
    public void FieldHeadings_DoNotTurnExamplesInCodeBlocksIntoFactsOrEntityNames()
    {
        var character = new CharacterData();
        var parser = new FolderImportParser(new FolderImportSchema());
        var parsed = parser.Parse("Sam.md", """
            ## Appearance
            short brown hair
            ```
            blue eyes
            ```
            ## Age
            ~~~
            99
            ~~~
            """, "character");
        parsed.Apply(character, "Source");
        Assert.Equal("Sam", parsed.Title);
        Assert.Equal("brown", character.HairColor);
        Assert.Empty(character.EyeColor);
        Assert.Empty(character.Age);
    }
}
