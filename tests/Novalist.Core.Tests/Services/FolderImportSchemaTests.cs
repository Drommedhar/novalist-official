using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class FolderImportSchemaTests
{
    [Fact]
    public void EveryExportedExample_ImportsUsingTheSameContract_IncludingBookCustomTypesAndTemplates()
    {
        var book = new BookData
        {
            CharacterTemplates = [new() { Id = "characters", Name = "People", CustomPropertyDefs = [new() { Key = "Occupation" }] }],
            LocationTemplates = [new() { Id = "locations", Name = "Places", CustomPropertyDefs = [new() { Key = "Population", Type = CustomPropertyType.Int }] }],
            ItemTemplates = [new() { Id = "items", Name = "Objects", CustomPropertyDefs = [new() { Key = "Magic", Type = CustomPropertyType.Bool }] }],
            LoreTemplates = [new() { Id = "lore", Name = "Lore", CustomPropertyDefs = [new() { Key = "Source" }] }],
            CustomEntityTemplates = [new() { Id = "factions", Name = "Groups", EntityTypeKey = "faction", CustomPropertyDefs = [new() { Key = "Founded", Type = CustomPropertyType.Date }] }],
            ActiveCustomEntityTemplateIds = new() { ["faction"] = "factions" },
            ManuscriptProperties = [new() { Key = "checked", Label = "Checked", Type = CustomPropertyType.Bool, Scope = ManuscriptPropertyScope.Research }]
        };
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction", DefaultFields = [new() { Key = "Rank", DisplayName = "Rank", Type = CustomPropertyType.Enum, EnumOptions = ["Major", "Minor"] }] };
        var schema = new FolderImportSchema(book, [type]);
        var export = JsonNode.Parse(schema.Export())!;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", export["$schema"]!.GetValue<string>());
        var branches = (JsonArray)export["oneOf"]!;
        Assert.Equal(7, branches.Count);
        var parser = new FolderImportParser(schema);
        foreach (var branch in branches)
        {
            var example = branch!["examples"]![0]!.DeepClone();
            var target = example["target"]!.GetValue<string>();
            var parsed = parser.Parse("entry.json", example.ToJsonString(), target);
            object entry = target switch
            {
                "character" => new CharacterData(), "location" => new LocationData(), "item" => new ItemData(),
                "lore" => new LoreData(), "scene" => new SceneData(), "research" => new ResearchItem(), _ => new CustomEntityData()
            };
            parsed.Apply(entry, "Imported content");
            Assert.Equal("Example " + target, parsed.Title);
            Assert.True(parsed.IsJson);
            Assert.Contains("**Markdown**", parsed.Content);
            Assert.True(FolderImportSchema.Accepts((JsonObject)branch, example));
        }
        Assert.DoesNotContain("id\"", schema.Export());
        var faction = branches.Single(branch => branch!["properties"]!["target"]!["const"]!.GetValue<string>() == "faction")!;
        Assert.Equal("Major", faction["properties"]!["data"]!["properties"]!["fields"]!["properties"]!["Rank"]!["enum"]![0]!.GetValue<string>());
        Assert.Equal("date", faction["properties"]!["data"]!["properties"]!["customProperties"]!["properties"]!["Founded"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public void CanonicalJson_PopulatesBuiltInFieldsPrivacySectionsAndRelationshipsWithoutGuessing()
    {
        var schema = new FolderImportSchema();
        var source = """
            {
              "novalistImport": 1,
              "target": "character",
              "data": {
                "name": "Ada", "surname": "Lovelace", "age": "32",
                "aliases": ["Enchantress"], "gender": "woman", "role": "Inventor",
                "ai": "Never", "readerHidden": true, "tags": ["lead"],
                "customProperties": { "Skill": "Mathematics" },
                "sections": [{ "title": "Biography", "content": "A long **biography**.", "aiHidden": true, "readerHidden": false }],
                "relationships": [{ "role": "collaborator", "target": "Charles Babbage", "category": "ally" }]
              },
              "content": "Additional writing."
            }
            """;
        var parsed = new FolderImportParser(schema).Parse("one.JSON", "\uFEFF" + source, "character");
        var character = new CharacterData();
        parsed.Apply(character, "Imported content");
        Assert.Equal("Ada", character.Name);
        Assert.Equal("Lovelace", character.Surname);
        Assert.Equal("32", character.Age);
        Assert.Equal("woman", character.Gender);
        Assert.Equal("Inventor", character.Role);
        Assert.Equal("Enchantress", Assert.Single(character.Aliases));
        Assert.Equal(AiInclusion.Never, character.Ai);
        Assert.True(character.ReaderHidden);
        Assert.Equal("lead", Assert.Single(parsed.Tags));
        Assert.Equal("Mathematics", character.CustomProperties["Skill"]);
        Assert.Equal("Charles Babbage", Assert.Single(character.Relationships).Target);
        Assert.True(character.Sections[0].AiHidden);
        Assert.Equal("A long **biography**.", character.Sections[0].Content);
        Assert.Equal("Additional writing.", character.Sections[1].Content);
        Assert.Empty(new FolderImportParser(schema).Parse("bare.json", "{\"novalistImport\":1,\"target\":\"character\",\"data\":{\"name\":\"Bare\"}}", "character").Content);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"novalistImport\":2,\"target\":\"character\",\"data\":{\"name\":\"Ada\"}}")]
    [InlineData("{\"novalistImport\":1,\"target\":\"location\",\"data\":{\"name\":\"Ada\"}}")]
    [InlineData("{\"novalistImport\":1,\"target\":\"character\",\"data\":{\"name\":\"Ada\"},\"unknown\":\"text\"}")]
    [InlineData("{\"novalistImport\":1,\"target\":\"character\",\"data\":{\"name\":\"Ada\",\"name\":\"Other\"}}")]
    [InlineData("{\"novalistImport\":1,\"target\":\"character\",\"data\":null}")]
    public void InvalidDocuments_RejectWithoutCreatingAnEntry(string source)
        => Assert.Throws<FormatException>(() => new FolderImportParser(new FolderImportSchema()).Parse("one.json", source, "character"));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\"   \"}")]
    [InlineData("{\"name\":32}")]
    [InlineData("{\"name\":\"Ada\",\"id\":\"intruder\"}")]
    [InlineData("{\"name\":\"Ada\",\"readerHidden\":\"yes\"}")]
    [InlineData("{\"name\":\"Ada\",\"aliases\":\"one,two\"}")]
    [InlineData("{\"name\":\"Ada\",\"aliases\":[\"one\",null]}")]
    [InlineData("{\"name\":\"Ada\",\"ai\":\"never\"}")]
    [InlineData("{\"name\":\"Ada\",\"customProperties\":{\"x\":true}}")]
    [InlineData("{\"name\":\"Ada\",\"customProperties\":{\"importedFrom\":\"false-path\"}}")]
    [InlineData("{\"name\":\"Ada\",\"sections\":[{\"title\":\"Incomplete\"}]}")]
    [InlineData("{\"name\":\"Ada\",\"sections\":[{\"title\":\"T\",\"content\":\"C\",\"unknown\":true}]}")]
    [InlineData("{\"name\":\"Ada\",\"ageMode\":\"guess\"}")]
    public void InvalidFieldShapes_DoNotGetSilentlyDropped(string data)
    {
        var source = "{\"novalistImport\":1,\"target\":\"character\",\"data\":" + data + "}";
        Assert.Throws<FormatException>(() => new FolderImportParser(new FolderImportSchema()).Parse("one.json", source, "character"));
    }

    [Theory]
    [InlineData("{\"title\":\"Note\",\"rating\":-1}")]
    [InlineData("{\"title\":\"Note\",\"rating\":6}")]
    [InlineData("{\"title\":\"Note\",\"rating\":\"3\"}")]
    [InlineData("{\"title\":\"Note\",\"rating\":2.5}")]
    [InlineData("{\"title\":\"Note\",\"rating\":2147483648}")]
    public void InvalidNumericFields_AreRejected(string data)
    {
        var source = "{\"novalistImport\":1,\"target\":\"research\",\"content\":\"Writing\",\"data\":" + data + "}";
        Assert.Throws<FormatException>(() => new FolderImportParser(new FolderImportSchema()).Parse("one.json", source, "research"));
    }

    [Fact]
    public void SceneJson_ImportsManuscriptMetadataAndAdditionalProperties()
    {
        var source = """
            {"novalistImport":1,"target":"scene","content":"Scene prose.","data":{
              "title":"Arrival","pov":"Ada","emotion":"hope","conflict":"storm","intensity":-5,
              "notes":"A note","synopsis":"They arrive","goal":"Reach land","outcome":"They do",
              "date":"1800-01-01","isFavorite":true,"inactive":false,"wordTarget":1000,
              "stage":"draft","label":"blue","beat":"opening","narrativeMode":"flashback","strand":"past",
              "excludeFromExport":true,"tags":["sea"],"properties":{"Research needed":"Harbour maps"}
            }}
            """;
        var scene = new SceneData { Properties = [] };
        var parsed = new FolderImportParser(new FolderImportSchema()).Parse("one.json", source, "scene");
        parsed.Apply(scene, "Source");
        Assert.Equal("Ada", scene.AnalysisOverrides!.Pov);
        Assert.Equal("hope", scene.AnalysisOverrides.Emotion);
        Assert.Equal("storm", scene.AnalysisOverrides.Conflict);
        Assert.Equal(-5, scene.AnalysisOverrides.Intensity);
        Assert.Equal("They arrive", scene.Synopsis);
        Assert.Equal("opening", scene.BeatKey);
        Assert.Equal("blue", scene.LabelKey);
        Assert.Equal(1000, scene.WordTarget);
        Assert.True(scene.IsFavorite);
        Assert.True(scene.ExcludeFromExport);
        Assert.Equal("Harbour maps", scene.Properties["Research needed"]);
        Assert.Equal("sea", Assert.Single(parsed.Tags));
    }

    [Fact]
    public void TypedCustomValuesAndCustomTypeFields_EnforceTheExportedDefinitions()
    {
        var book = new BookData
        {
            ActiveCustomEntityTemplateIds = new() { ["faction"] = "active" },
            CustomEntityTemplates = [new() { Id = "active", EntityTypeKey = "faction", Name = "Faction", CustomPropertyDefs =
            [new() { Key = "Founded", Type = CustomPropertyType.Date }, new() { Key = "Role" }, new() { Key = "Years", Type = CustomPropertyType.Int }] }]
        };
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DefaultFields = [new() { Key = "strength", Type = CustomPropertyType.Int }] };
        var parser = new FolderImportParser(new FolderImportSchema(book, [type]));
        var root = JsonNode.Parse("""
            {"novalistImport":1,"target":"faction","data":{"name":"Guild","fields":{"strength":"40"},"customProperties":{"Founded":"2024-02-29","Role":"ally","Years":"9999999999999999"}}}
            """)!;
        var faction = new CustomEntityData();
        parser.Parse("one.json", root.ToJsonString(), "faction").Apply(faction, "Source");
        Assert.Equal("active", faction.TemplateId);
        Assert.Equal("40", faction.Fields["strength"]);
        Assert.Equal("2024-02-29", faction.CustomProperties["Founded"]);
        Assert.Equal("9999999999999999", faction.CustomProperties["Years"]);
        foreach (var invalid in new[] { "40 people", "4.5", "lots" })
        {
            root["data"]!["fields"]!["strength"] = invalid;
            Assert.Throws<FormatException>(() => parser.Parse("one.json", root.ToJsonString(), "faction"));
        }
        root["data"]!["fields"]!["strength"] = "40";
        root["data"]!["customProperties"]!["Founded"] = "2023-02-29";
        Assert.Throws<FormatException>(() => parser.Parse("one.json", root.ToJsonString(), "faction"));
        root["data"]!["customProperties"]!["Founded"] = "2024-02-29";
        root["data"]!["templateId"] = "different-book";
        Assert.Throws<FormatException>(() => parser.Parse("one.json", root.ToJsonString(), "faction"));
        root["data"]!["templateId"] = "active";
        root["data"]!["fields"]!["unknown"] = "text";
        Assert.Throws<FormatException>(() => parser.Parse("one.json", root.ToJsonString(), "faction"));
    }

    [Fact]
    public void PublishedBaseSchema_MatchesTheContractInTheApp()
    {
        var path = Path.GetFullPath("../../../../../docs/import/novalist-import.schema.json", AppContext.BaseDirectory);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), JsonNode.Parse(new FolderImportSchema().Export())));
    }

    [Fact]
    public void PublishedExamples_AreReadyToImportWithoutManualEdits()
    {
        var folder = Path.GetFullPath("../../../../../docs/import/examples", AppContext.BaseDirectory);
        var parser = new FolderImportParser(new FolderImportSchema());
        var paths = Directory.GetFiles(folder, "*.json");
        Assert.Equal(6, paths.Length);
        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            var target = JsonNode.Parse(text)!["target"]!.GetValue<string>();
            var entry = parser.Parse(path, text, target);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.True(entry.IsJson);
        }
    }

    [Fact]
    public void RequiredCustomFields_ArePresentInTheSchemaAndExamples_AndMustBeSupplied()
    {
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DefaultFields = [new() { Key = "members", Type = CustomPropertyType.Int, Required = true }] };
        var schema = new FolderImportSchema(types: [type]);
        var branch = ((JsonArray)JsonNode.Parse(schema.Export())!["oneOf"]!).Last()!;
        Assert.Equal("members", branch["properties"]!["data"]!["properties"]!["fields"]!["required"]![0]!.GetValue<string>());
        var example = branch["examples"]![0]!;
        var parser = new FolderImportParser(schema);
        var entity = new CustomEntityData();
        parser.Parse("one.json", example.ToJsonString(), "faction").Apply(entity, "Source");
        Assert.Equal("0", entity.Fields["members"]);
        ((JsonObject)example["data"]!).Remove("fields");
        Assert.Throws<FormatException>(() => parser.Parse("one.json", example.ToJsonString(), "faction"));
        example["data"]!["fields"] = new JsonObject();
        Assert.Throws<FormatException>(() => parser.Parse("one.json", example.ToJsonString(), "faction"));
    }

    [Fact]
    public void CustomFieldsWithBuiltInNames_KeepTheirOwnTypesAndCanonicalJsonDestinations()
    {
        var book = new BookData { CharacterTemplates = [new() { Id = "people", Name = "People", CustomPropertyDefs =
        [new() { Key = "sections" }, new() { Key = "rating", Type = CustomPropertyType.Enum, EnumOptions = ["High", "Low"] },
         new() { Key = "ageMode", Type = CustomPropertyType.Enum, EnumOptions = ["Adult", "Child"] },
         new() { Key = "Eye colour", Type = CustomPropertyType.Bool }] }] };
        var schema = new FolderImportSchema(book);
        Assert.Contains("Adult", schema.Export());
        var parser = new FolderImportParser(schema);
        var source = """
            {"novalistImport":1,"target":"character","data":{"name":"Ada","ageMode":"number","sections":[{"title":"Biography","content":"Writing"}],
              "customProperties":{"sections":"A custom fact","rating":"High","ageMode":"Adult","Eye colour":"true"}}}
            """;
        var character = new CharacterData();
        parser.Parse("one.json", source, "character").Apply(character, "Source");
        Assert.Equal("number", character.AgeMode);
        Assert.Equal("Writing", Assert.Single(character.Sections).Content);
        Assert.Equal("A custom fact", character.CustomProperties["sections"]);
        Assert.Equal("High", character.CustomProperties["rating"]);
        Assert.Equal("Adult", character.CustomProperties["ageMode"]);
        Assert.Equal("true", character.CustomProperties["Eye colour"]);
        character = new CharacterData();
        parser.Parse("two.md", "customProperties.Eye colour: yes", "character").Apply(character, "Source");
        Assert.Equal("true", character.CustomProperties["Eye colour"]);
        Assert.Empty(character.EyeColor);
    }
}
