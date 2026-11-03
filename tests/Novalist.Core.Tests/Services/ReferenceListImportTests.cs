using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class ReferenceListImportTests
{
    private static FolderImportSchema Schema() => new(null, [new CustomEntityTypeDefinition
    {
        TypeKey = "guild", DisplayName = "Guild", DefaultFields = [
            new() { Key = "Members", DisplayName = "Members", Type = CustomPropertyType.EntityRefList },
            new() { Key = "History", DisplayName = "History", Type = CustomPropertyType.Text }
        ]
    }]);

    [Fact]
    public void ExportedExampleUsesAnEmptyReferenceList()
    {
        var schema = Schema();
        var branches = JsonNode.Parse(schema.Export())!["oneOf"]!.AsArray();
        var example = branches.Single(branch => branch!["properties"]!["target"]!["const"]!.GetValue<string>() == "guild")!["examples"]![0]!;
        var parsed = new FolderImportParser(schema).Parse("guild.json", example.ToJsonString(), "guild");
        var entry = new CustomEntityData();
        parsed.Apply(entry, "Source");
        Assert.Equal("[]", entry.Fields["Members"]);
    }

    [Theory]
    [InlineData("['first', 'second', 'first']")]
    [InlineData("'[\"first\",\"second\",\"first\"]'")]
    public void YamlReferenceListsPreserveDistinctEntryIds(string value)
    {
        var source = "---\nname: Guild\nMembers: " + value + "\nHistory: |\n  First line.\n  Second line.\n---\nSource text.";
        var parsed = new FolderImportParser(Schema()).Parse("guild.md", source, "guild");
        var entry = new CustomEntityData();
        parsed.Apply(entry, "Source");
        Assert.Equal("[\"first\",\"second\"]", entry.Fields["Members"]);
        Assert.Equal("First line.\nSecond line.", entry.Fields["History"]);
    }

    [Fact]
    public void InvalidReferenceListsRemainInThePreservedSource()
    {
        const string source = "---\nMembers: broken\n---\nOriginal writing.";
        var parsed = new FolderImportParser(Schema()).Parse("guild.md", source, "guild");
        var entry = new CustomEntityData();
        parsed.Apply(entry, "Source");
        Assert.False(entry.Fields.ContainsKey("Members"));
        Assert.Equal(source, Assert.Single(entry.Sections).Content);
    }
}
