using NSubstitute;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Services;

public partial class EntityServiceTests
{
    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    [InlineData("guild")]
    public async Task TargetedReadsReturnCurrentRecordsForEveryEntityType(string type)
    {
        _meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "guild", FolderName = "Guilds" });
        var folder = type switch
        {
            "character" => _book.CharacterFolder, "location" => _book.LocationFolder,
            "item" => _book.ItemFolder, "lore" => _book.LoreFolder, _ => "Guilds"
        };
        var path = Path.Combine(_bookRoot, folder, "selected.json");
        WriteJson(path, new { Id = "selected", Name = "Before", EntityTypeKey = type });
        Assert.Equal("Before", (await ReadSelectedAsync(type))!.DisplayName);

        WriteJson(path, new { Id = "selected", Name = "After", EntityTypeKey = type });
        var changed = await ReadSelectedAsync(type);

        Assert.Equal("After", changed!.DisplayName);
        Assert.False(changed.IsWorldBible);
    }

    private async Task<IEntityData?> ReadSelectedAsync(string type) => type switch
    {
        "character" => await _sut.LoadCharacterAsync("selected"),
        "location" => await _sut.LoadLocationAsync("selected"),
        "item" => await _sut.LoadItemAsync("selected"),
        "lore" => await _sut.LoadLoreEntryAsync("selected"),
        _ => await _sut.LoadCustomEntityAsync(type, "selected")
    };

    [Fact]
    public async Task RenamedBookRecordOverridesWorldBibleDespiteInvalidCanonicalFiles()
    {
        WriteBookChar("selected", "Wrong record");
        WriteJson(Path.Combine(_bookRoot, _book.CharacterFolder, "selected.json"), new CharacterData { Id = "other" });
        File.WriteAllText(Path.Combine(_bookRoot, _book.CharacterFolder, "broken.json"), "{ broken");
        File.WriteAllText(Path.Combine(_bookRoot, _book.CharacterFolder, "null.json"), "null");
        WriteJson(Path.Combine(_bookRoot, _book.CharacterFolder, "z-renamed.json"), new CharacterData { Id = "selected", Name = "Book" });
        WriteWbChar("selected", "World");

        var selected = await _sut.LoadCharacterAsync("selected");

        Assert.Equal("Book", selected!.Name);
        Assert.False(selected.IsWorldBible);
        File.Delete(Path.Combine(_bookRoot, _book.CharacterFolder, "z-renamed.json"));
        Assert.True((await _sut.LoadCharacterAsync("selected"))!.IsWorldBible);
    }

    [Fact]
    public async Task MissingRecordsReturnNullWithAndWithoutWorldBible()
    {
        Assert.Null(await _sut.LoadCharacterAsync("missing"));
        WriteWbChar("other", "Other");
        Assert.Null(await _sut.LoadCharacterAsync("missing"));
        _project.WorldBibleRoot.Returns((string?)null);
        Assert.Null(await _sut.LoadCharacterAsync("missing"));
    }
}
