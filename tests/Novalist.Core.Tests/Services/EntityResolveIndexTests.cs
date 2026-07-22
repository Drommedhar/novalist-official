using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class EntityResolveIndexTests
{
    private static readonly IReadOnlyList<(string, IReadOnlyList<CustomEntityData>)> NoCustom =
        new List<(string, IReadOnlyList<CustomEntityData>)>();

    [Fact]
    public void Normalize_StripsWikiBracketsAndTrims()
        => Assert.Equal("Aldric", EntityResolveIndex.Normalize("  [[Aldric]] "));

    [Fact]
    public void Compose_JoinsSurnameWhenPresent()
    {
        Assert.Equal("Aldric Vane", EntityResolveIndex.Compose("Aldric", "Vane"));
        Assert.Equal("Aldric", EntityResolveIndex.Compose("Aldric", ""));
    }

    [Fact]
    public void Build_MapsComposedName_FirstName_AndAliases()
    {
        var c = new CharacterData { Id = "c1", Name = "Aldric", Surname = "Vane", Aliases = { "The Grey" } };
        var index = EntityResolveIndex.Build([c], [], [], [], NoCustom);

        Assert.Equal(("c1", "character"), index["Aldric Vane"]);
        Assert.Equal(("c1", "character"), index["Aldric"]);       // bare first name
        Assert.Equal(("c1", "character"), index["the grey"]);     // alias, case-insensitive
    }

    [Fact]
    public void Build_SurnamelessCharacter_NotTreatedAsAmbiguous()
    {
        var c = new CharacterData { Id = "c1", Name = "Mordre", Surname = "" };
        var index = EntityResolveIndex.Build([c], [], [], [], NoCustom);

        // Composed == first name; must be added once, so it still resolves.
        Assert.Equal(("c1", "character"), index["Mordre"]);
    }

    [Fact]
    public void Build_AmbiguousName_Dropped()
    {
        var a = new CharacterData { Id = "a", Name = "Robin", Surname = "" };
        var b = new CharacterData { Id = "b", Name = "Robin", Surname = "" };
        var index = EntityResolveIndex.Build([a, b], [], [], [], NoCustom);

        Assert.False(index.ContainsKey("Robin"));
    }

    [Fact]
    public void Build_IgnoresBlankNames()
    {
        var l = new LocationData { Id = "l1", Name = "   " };
        var index = EntityResolveIndex.Build([], [l], [], [], NoCustom);
        Assert.Empty(index);
    }

    [Fact]
    public void Build_MapsLocationsItemsLore()
    {
        var loc = new LocationData { Id = "l1", Name = "Harbour", Aliases = { "Docks" } };
        var item = new ItemData { Id = "i1", Name = "Blade" };
        var lore = new LoreData { Id = "o1", Name = "The Pact" };
        var index = EntityResolveIndex.Build([], [loc], [item], [lore], NoCustom);

        Assert.Equal(("l1", "location"), index["Harbour"]);
        Assert.Equal(("l1", "location"), index["Docks"]);
        Assert.Equal(("i1", "item"), index["Blade"]);
        Assert.Equal(("o1", "lore"), index["The Pact"]);
    }

    [Fact]
    public void Build_MapsCustomEntities()
    {
        var custom = new CustomEntityData { Id = "s1", Name = "Fireball", Aliases = { "Flame" } };
        var customTypes = new List<(string, IReadOnlyList<CustomEntityData>)> { ("spell", [custom]) };
        var index = EntityResolveIndex.Build([], [], [], [], customTypes);

        Assert.Equal(("s1", "spell"), index["Fireball"]);
        Assert.Equal(("s1", "spell"), index["Flame"]);
    }

    [Fact]
    public async Task BuildAsync_LoadsEverythingFromService()
    {
        var entities = Substitute.For<IEntityService>();
        entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "Aldric" }]);
        entities.LoadLocationsAsync().Returns([new LocationData { Id = "l1", Name = "Harbour" }]);
        entities.LoadItemsAsync().Returns([new ItemData { Id = "i1", Name = "Blade" }]);
        entities.LoadLoreAsync().Returns([new LoreData { Id = "o1", Name = "The Pact" }]);
        entities.GetCustomEntityTypes().Returns([new CustomEntityTypeDefinition { TypeKey = "spell" }]);
        entities.LoadCustomEntitiesAsync("spell").Returns([new CustomEntityData { Id = "s1", Name = "Fireball" }]);

        var index = await EntityResolveIndex.BuildAsync(entities);

        Assert.Equal(("c1", "character"), index["Aldric"]);
        Assert.Equal(("l1", "location"), index["Harbour"]);
        Assert.Equal(("i1", "item"), index["Blade"]);
        Assert.Equal(("o1", "lore"), index["The Pact"]);
        Assert.Equal(("s1", "spell"), index["Fireball"]);
    }
}
