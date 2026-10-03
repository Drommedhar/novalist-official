using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class FolderImportParserTests
{
    [Theory]
    [InlineData("md", "\n", "\n")]
    [InlineData("md", "\r\n", "\r\n")]
    [InlineData("md", "\r\n", "\n")]
    [InlineData("txt", "\n", "\n")]
    [InlineData("txt", "\r\n", "\r\n")]
    [InlineData("txt", "\r\n", "\n")]
    public void Source_PreservesWritingWithLfLineEndings(string extension, string headerNewline, string bodyNewline)
    {
        var source = $"---{headerNewline}name: Ada{headerNewline}---{headerNewline}Age: 32{bodyNewline}Unmapped **writing**.";
        const string expectedSource = "---\nname: Ada\n---\nAge: 32\nUnmapped **writing**.";
        var parsed = new FolderImportParser(new FolderImportSchema()).Parse($"one.{extension}", source, "character");
        var character = new CharacterData();
        parsed.Apply(character, "Source");

        Assert.Equal("Ada", character.Name);
        Assert.Equal("32", character.Age);
        Assert.Equal(expectedSource, parsed.Source);
        Assert.Equal(expectedSource, Assert.Single(character.Sections).Content);
    }

    private static BookData Book() => new()
    {
        ActiveCharacterTemplateId = "people",
        CharacterTemplates = [new CharacterTemplate
        {
            Id = "people", Name = "People", CustomPropertyDefs =
            [
                new() { Key = "Skill", Type = CustomPropertyType.String, Prompt = "What can they do?" },
                new() { Key = "Score", Type = CustomPropertyType.Int },
                new() { Key = "Alive", Type = CustomPropertyType.Bool },
                new() { Key = "Anniversary", Type = CustomPropertyType.Date },
                new() { Key = "Rank", Type = CustomPropertyType.Enum, EnumOptions = ["Captain", "Crew"] },
                new() { Key = "Service", Type = CustomPropertyType.Timespan },
                new() { Key = "Home", Type = CustomPropertyType.EntityRef },
                new() { Key = "Extension", TypeKey = "ext.test.text" },
                new() { Key = "IntegerKey", TypeKey = "Int" }
            ]
        }]
    };

    [Fact]
    public void Markdown_MapsMetadataLabelsTablesHeadingsAndTypedCustomFields_AndPreservesSource()
    {
        var source = """
            ---
            first_name: Ada
            surname: Lovelace
            age: 32
            gender: woman
            birthDate: 1815-12-10
            ageMode: date
            ageIntervalUnit: years
            aliases: [Enchantress, Ada]
            tags: [lead, inventor]
            readerHidden: yes
            ai: never
            customProperties:
              Skill: Mathematics
              Score: 007
              Alive: yes
              Anniversary: 1843-09-01
              Rank: captain
              Service: 12
              Home: London
              Extension: detailed text
              IntegerKey: 9
              Extra fact: 123
              importedFrom: bogus
            relationships:
              - role: collaborator
                target: Charles Babbage
                category: ally
            sections:
              - title: Private
                content: Original private writing
                aiHidden: true
                readerHidden: yes
            unknown:
              nested: This must not disappear.
            ---
            # Ada
            **Eye colour:** brown
            - Hair color: dark
            Group: Researchers
            | Field | Value |
            | --- | --- |
            | Height | 165 cm |
            | Build | slim |
            | Skin tone | light |
            ## Hair length
            Long
            ## Distinguishing features
            A scar on her left hand.
            ## Biography
            Preserved **biography**.
            ```yaml
            Age: 99
            ```
            """;
        var parsed = new FolderImportParser(new FolderImportSchema(Book())).Parse("Ada.md", source, "character");
        var character = new CharacterData { Id = "generated", CustomProperties = new() { ["importedFrom"] = "real-source" } };
        parsed.Apply(character, "Imported content");
        Assert.Equal("Ada", parsed.Title);
        Assert.Equal("Ada", character.Name);
        Assert.Equal("Lovelace", character.Surname);
        Assert.Equal("32", character.Age);
        Assert.Equal("woman", character.Gender);
        Assert.Equal("1815-12-10", character.BirthDate);
        Assert.Equal("date", character.AgeMode);
        Assert.Equal(IntervalUnit.Years, character.AgeIntervalUnit);
        Assert.Equal(["Enchantress", "Ada"], character.Aliases);
        Assert.Equal(["lead", "inventor"], parsed.Tags);
        Assert.True(character.ReaderHidden);
        Assert.Equal(AiInclusion.Never, character.Ai);
        Assert.Equal("brown", character.EyeColor);
        Assert.Equal("dark", character.HairColor);
        Assert.Equal("Long", character.HairLength);
        Assert.Equal("165 cm", character.Height);
        Assert.Equal("slim", character.Build);
        Assert.Equal("light", character.SkinTone);
        Assert.Equal("Researchers", character.Group);
        Assert.Equal("A scar on her left hand.", character.DistinguishingFeatures);
        Assert.Equal("people", character.TemplateId);
        Assert.Equal("7", character.CustomProperties["Score"]);
        Assert.Equal("true", character.CustomProperties["Alive"]);
        Assert.Equal("Captain", character.CustomProperties["Rank"]);
        Assert.Equal("1843-09-01", character.CustomProperties["Anniversary"]);
        Assert.Equal("12", character.CustomProperties["Service"]);
        Assert.Equal("London", character.CustomProperties["Home"]);
        Assert.Equal("detailed text", character.CustomProperties["Extension"]);
        Assert.Equal("9", character.CustomProperties["IntegerKey"]);
        Assert.Equal("123", character.CustomProperties["Extra fact"]);
        Assert.Equal("real-source", character.CustomProperties["importedFrom"]);
        Assert.Equal("generated", character.Id);
        Assert.Equal("Charles Babbage", Assert.Single(character.Relationships).Target);
        Assert.True(character.Sections[0].AiHidden);
        Assert.True(character.Sections[0].ReaderHidden);
        Assert.Contains(character.Sections, section => section.Title == "Biography" && section.Content.Contains("**biography**"));
        Assert.Equal(source.ReplaceLineEndings("\n"), character.Sections.Last().Content);
        Assert.True(character.Sections.Last().AiHidden);
        Assert.True(character.Sections.Last().ReaderHidden);
    }

    [Theory]
    [InlineData("Vorname: Ada\nNachname: Lovelace\nAlter: 32\nAugenfarbe: braun\nHaarlänge: lang\nGröße: 165 cm", "braun")]
    [InlineData("名字：Ada\n姓氏：Lovelace\n年龄：32\n瞳色：棕色\n发长：lang\n身高：165 cm", "棕色")]
    public void LocalisedLabels_MapToTheSameFields(string content, string eyes)
    {
        var character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("input.txt", content, "character").Apply(character, "Source");
        Assert.Equal("Ada", character.Name);
        Assert.Equal("Lovelace", character.Surname);
        Assert.Equal("32", character.Age);
        Assert.Equal(eyes, character.EyeColor);
        Assert.Equal("lang", character.HairLength);
        Assert.Equal("165 cm", character.Height);
    }

    [Fact]
    public void InvalidOrAmbiguousFields_DoNotGuess_AndQualifiedCustomFieldsAreRecognised()
    {
        var book = Book();
        book.CharacterTemplates[0].CustomPropertyDefs.Add(new() { Key = "Role" });
        var source = """
            ---
            ageIntervalUnit: centuries
            readerHidden: unknown
            customProperties:
              Alive: maybe
              Score: lots
              Anniversary: 2025-02-30
              Rank: stranger
              Invalid object: {nested: text}
            ---
            Role: explorer
            customProperties.Role: scout
            ## Custom properties
            Skill: sailing
            ## Aliases
            - Red
            - Doctor
            ## Biography
            Age is never guessed from prose, even when they are thirty.
            ~~~text
            Gender: incorrect
            ~~~
            """;
        var character = new CharacterData();
        var parsed = new FolderImportParser(new FolderImportSchema(book)).Parse("unknown.md", source, "character");
        parsed.Apply(character, "Source");
        Assert.Empty(character.Role);
        Assert.Empty(character.Gender);
        Assert.Empty(character.Age);
        Assert.Null(character.AgeIntervalUnit);
        Assert.False(character.ReaderHidden);
        Assert.Equal(["Red", "Doctor"], character.Aliases);
        Assert.Equal("scout", character.CustomProperties["Role"]);
        Assert.Equal("sailing", character.CustomProperties["Skill"]);
        Assert.DoesNotContain("Score", character.CustomProperties.Keys);
        Assert.DoesNotContain("Alive", character.CustomProperties.Keys);
        Assert.DoesNotContain("Anniversary", character.CustomProperties.Keys);
        Assert.DoesNotContain("Rank", character.CustomProperties.Keys);
        Assert.DoesNotContain("Invalid object", character.CustomProperties.Keys);
        Assert.Equal(source.ReplaceLineEndings("\n"), character.Sections.Last().Content);
    }

    [Fact]
    public void CustomTypes_MatchKeysAndDisplayLabels_WithScopedPropertiesAndHeadingSections()
    {
        var type = new CustomEntityTypeDefinition
        {
            TypeKey = "faction", DisplayName = "Faction", FolderName = "Factions", DefaultFields =
            [new() { Key = "strength", DisplayName = "Military strength", Type = CustomPropertyType.Int },
             new() { Key = "public", DisplayName = "Public", Type = CustomPropertyType.Bool }]
        };
        var book = new BookData
        {
            CustomEntityTemplates = [new() { Id = "faction-template", EntityTypeKey = "faction", Name = "Faction", CustomPropertyDefs = [new() { Key = "Leader" }] }],
            ActiveCustomEntityTemplateIds = new() { ["faction"] = "faction-template" }
        };
        var parsed = new FolderImportParser(new FolderImportSchema(book, [type])).Parse("Guild.md", """
            # The Guild
            | Field | Value |
            | Military strength | 42 |
            | Public | no |
            ## Custom properties
            Leader: Ada
            ## History
            A long history.
            """, "faction");
        var entity = new CustomEntityData();
        parsed.Apply(entity, "Source");
        Assert.Equal("The Guild", parsed.Title);
        Assert.Equal("42", entity.Fields["strength"]);
        Assert.Equal("false", entity.Fields["public"]);
        Assert.Equal("Ada", entity.CustomProperties["Leader"]);
        Assert.Equal("faction-template", entity.TemplateId);
        Assert.Contains(entity.Sections, section => section.Title == "History" && section.Content == "A long history.");
    }

    [Fact]
    public void SceneAndResearch_CustomPropertyLabelsAndMetadataPopulateTheCorrectDestinations()
    {
        var book = new BookData { ManuscriptProperties =
        [new() { Key = "suspense", Label = "Suspense", Type = CustomPropertyType.Int, Scope = ManuscriptPropertyScope.Scene },
         new() { Key = "checked", Label = "Checked", Type = CustomPropertyType.Bool, Scope = ManuscriptPropertyScope.Research }] };
        var parser = new FolderImportParser(new FolderImportSchema(book));
        var scene = new SceneData { Properties = [], AnalysisOverrides = new() };
        parser.Parse("one.md", "---\ntitle: Arrival\nsynopsis: Docking\nproperties:\n  suspense: 08\n---\nPOV: Ada\nIntensity: -3\nGoal: land\nOutcome: captured\n## Content\nProse.", "scene").Apply(scene, "Source");
        Assert.Equal("Arrival", scene.Title);
        Assert.Equal("Docking", scene.Synopsis);
        Assert.Equal("8", scene.Properties["suspense"]);
        Assert.Equal("Ada", scene.AnalysisOverrides!.Pov);
        Assert.Equal(-3, scene.AnalysisOverrides.Intensity);
        Assert.Equal("land", scene.Goal);
        Assert.Equal("captured", scene.Outcome);
        var research = new ResearchItem { Properties = [] };
        parser.Parse("note.txt", "Title: Source\nStatus: resolved\nRating: 4\nChecked: yes\nproperties.extra: Extra fact", "research").Apply(research, "Source");
        Assert.Equal("Source", research.Title);
        Assert.Equal(ResearchStatus.Resolved, research.Status);
        Assert.Equal(4, research.Rating);
        Assert.Equal("true", research.Properties["checked"]);
        Assert.Equal("Extra fact", research.Properties["extra"]);
        parser.Parse("invalid.txt", "Rating: many\nStatus: nonsense\nIntensity: 30", "research").Apply(research, "Source");
        Assert.Equal(4, research.Rating);
        Assert.Equal(ResearchStatus.Resolved, research.Status);
    }

    [Fact]
    public void ExplicitTemplateNames_Work_AndUnknownOrAmbiguousTemplatesKeepSource()
    {
        var book = Book();
        book.CharacterTemplates.Add(new() { Id = "other", Name = "Alternate", CustomPropertyDefs = [new() { Key = "Job" }] });
        var parser = new FolderImportParser(new FolderImportSchema(book));
        var character = new CharacterData();
        parser.Parse("one.md", "---\ntemplate: Alternate\nJob: sailor\n---\n# Ada", "character").Apply(character, "Source");
        Assert.Equal("other", character.TemplateId);
        Assert.Equal("sailor", character.CustomProperties["Job"]);
        var unknown = new CharacterData();
        parser.Parse("two.md", "Template: Missing\nJob: sailor", "character").Apply(unknown, "Source");
        Assert.Null(unknown.TemplateId);
        book.CharacterTemplates.Add(new() { Id = "duplicate", Name = "Alternate" });
        unknown = new CharacterData();
        new FolderImportParser(new FolderImportSchema(book)).Parse("three.md", "Template: Alternate", "character").Apply(unknown, "Source");
        Assert.Null(unknown.TemplateId);
        Assert.Single(unknown.Sections);
    }

    [Theory]
    [InlineData("---\nname: [broken\n---\n# Kept\nAge: 23")]
    [InlineData("---\n\n---\n# Kept\nAge: 23")]
    [InlineData("---\n? [complex, key]\n: value\n---\n# Kept\nAge: 23")]
    [InlineData("---\nname: &self {again: *self}\n---\n# Kept\nAge: 23")]
    public void MalformedOrUnsupportedYaml_PreservesTheFileAndStillReadsBodyLabels(string source)
    {
        var character = new CharacterData();
        var parsed = new FolderImportParser(new FolderImportSchema()).Parse("one.md", source, "character");
        parsed.Apply(character, "Source");
        Assert.Equal("Kept", parsed.Title);
        Assert.Equal("23", character.Age);
        Assert.Equal(source, Assert.Single(character.Sections).Content);
    }

    [Fact]
    public void YamlDepthAndExpansion_AreBoundedWithoutLosingTheFile()
    {
        foreach (var yaml in new[] { "deep: " + new string('[', 40) + "x" + new string(']', 40), "many: [" + string.Join(',', Enumerable.Repeat("x", 10001)) + "]" })
        {
            var source = "---\n" + yaml + "\n---\n# Kept\nAge: 23";
            var character = new CharacterData();
            new FolderImportParser(new FolderImportSchema()).Parse("one.md", source, "character").Apply(character, "Source");
            Assert.Equal("23", character.Age);
            Assert.Equal(source, Assert.Single(character.Sections).Content);
        }
    }

    [Fact]
    public void EmptyPlainTextQuotedValuesAndNulls_DoNotInventData()
    {
        var parser = new FolderImportParser(new FolderImportSchema());
        var empty = parser.Parse("empty.txt", "", "character");
        var character = new CharacterData();
        empty.Apply(character, "Source");
        Assert.Equal("empty", empty.Title);
        Assert.Empty(character.Sections);
        var source = "---\nname: ~\ngender: null\nage: \"null\"\n---\n# Ada\nSurname: 'Lovelace'\nAliases: [broken\nEye colour: \"brown\n| Unknown | x\\|y |";
        var parsed = parser.Parse("one.md", source, "character");
        parsed.Apply(character, "Source");
        Assert.Equal("Ada", parsed.Title);
        Assert.Empty(character.Gender);
        Assert.Equal("null", character.Age);
        Assert.Equal("Lovelace", character.Surname);
        Assert.Equal("\"brown", character.EyeColor);
        Assert.Contains("broken", Assert.Single(character.Aliases));
        Assert.Equal(source, Assert.Single(character.Sections).Content);
    }

    [Fact]
    public void BirthdayFieldsAndTemplateAgeSettings_KeepDateBasedAgeUsable()
    {
        var book = Book();
        book.CharacterTemplates[0].AgeMode = "date";
        book.CharacterTemplates[0].AgeIntervalUnit = IntervalUnit.Months;
        var parser = new FolderImportParser(new FolderImportSchema(book));
        var character = new CharacterData();
        parser.Parse("birthday.md", "Birth date: 2001-05-13", "character").Apply(character, "Source");
        Assert.Equal("2001-05-13", character.BirthDate);
        Assert.Equal("date", character.AgeMode);
        Assert.Equal(IntervalUnit.Months, character.AgeIntervalUnit);
        character = new CharacterData();
        new FolderImportParser(new FolderImportSchema()).Parse("birthday.md", "Birth date: 2001-05-13", "character").Apply(character, "Source");
        Assert.Equal("date", character.AgeMode);
        Assert.Equal(IntervalUnit.Years, character.AgeIntervalUnit);
    }
}
