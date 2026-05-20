using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class RelationshipsGraphViewModelTests
{
    static RelationshipsGraphViewModelTests()
    {
        // The graph classifies roles via locale keyword lists; point Loc at the
        // locales copied into the test output so RelationshipRoles loads them.
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
        RelationshipRoles.Reload();
    }

    private static CharacterData Char(string name, IEnumerable<(string Role, string Target)>? rels = null,
        string role = "", string group = "", bool wb = false, string surname = "")
    {
        var c = new CharacterData { Name = name, Surname = surname, Role = role, Group = group, IsWorldBible = wb };
        foreach (var (r, t) in rels ?? [])
            c.Relationships.Add(new EntityRelationship { Role = r, Target = t });
        return c;
    }

    private static RelationshipsGraphViewModel Build(params CharacterData[] chars)
    {
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns(chars.ToList());
        return new RelationshipsGraphViewModel(ent);
    }

    private static async Task<RelationshipsGraphViewModel> Loaded(params CharacterData[] chars)
    {
        var vm = Build(chars);
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task Reload_NoCharacters_Empty()
    {
        var vm = await Loaded();
        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Edges);
        Assert.Empty(vm.GroupBoxes);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Reload_NoRelationships_NoNodes()
    {
        var vm = await Loaded(Char("Alone"), Char("Solo"));
        Assert.Empty(vm.Nodes); // only connected characters are shown
    }

    [Fact]
    public async Task NonFamily_Relationship_BuildsEdge()
    {
        var vm = await Loaded(
            Char("Bob", [("friend", "Alice")]),
            Char("Alice"));
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Contains(vm.Edges, e => e.Label.Contains("friend"));
    }

    [Fact]
    public async Task ReciprocalRoles_MergeIntoOneEdge()
    {
        var vm = await Loaded(
            Char("Bob", [("mentor", "Alice")]),
            Char("Alice", [("student", "Bob")]));
        // Unordered pair merged -> single labeled edge listing both roles.
        var edge = Assert.Single(vm.Edges);
        Assert.Contains("mentor", edge.Label);
        Assert.Contains("student", edge.Label);
    }

    [Fact]
    public async Task ParentChild_Family_BuildsBoxAndTEdge_SuppressesFamilyEdge()
    {
        // Bob's father is Alice -> Alice parent of Bob (same family).
        var vm = await Loaded(
            Char("Bob", [("father", "Alice")], surname: "Smith"),
            Char("Alice", surname: "Smith"));
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
        // Parent/child edge is suppressed (replaced by the T-line); only T-edges remain.
        Assert.DoesNotContain(vm.Edges, e => e.Label.Contains("father"));
        Assert.NotEmpty(vm.Edges); // T-edges drawn
    }

    [Fact]
    public async Task Partners_PairedInFamily()
    {
        var vm = await Loaded(
            Char("Bob", [("husband", "Alice")]),
            Char("Alice", [("wife", "Bob")]));
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
    }

    [Fact]
    public async Task CoParents_ShareChild_BecomeImplicitPartners()
    {
        // Carol's father is Bob, Carol's mother is Alice -> Bob & Alice co-parents.
        var vm = await Loaded(
            Char("Carol", [("father", "Bob"), ("mother", "Alice")]),
            Char("Bob"),
            Char("Alice"));
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
    }

    [Fact]
    public async Task PseudoAnchor_ExternalCousin_PlacedNearFamily()
    {
        // A family (Alice parent of Bob) + external Cousin linked via pseudo role.
        var vm = await Loaded(
            Char("Bob", [("father", "Alice")]),
            Char("Alice"),
            Char("Carol", [("cousin", "Bob")]));
        Assert.Equal(3, vm.Nodes.Count);
    }

    [Fact]
    public async Task RoleGroup_ThreeOrMore_DrawsBox()
    {
        // A "guild" ring of 3+ connected by a non-family role -> role bounding box.
        var vm = await Loaded(
            Char("A", [("guildmate", "B"), ("guildmate", "C")]),
            Char("B", [("guildmate", "A"), ("guildmate", "C")]),
            Char("C", [("guildmate", "A"), ("guildmate", "B")]));
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Equals("guildmate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Filters_Search_Group_Role_HideWorldBible()
    {
        var vm = await Loaded(
            Char("Bob", [("friend", "Alice")], role: "Hero", group: "Party"),
            Char("Alice", role: "Sidekick", group: "Party", wb: true));

        Assert.Contains("Party", vm.AvailableGroups);
        Assert.Contains("Hero", vm.AvailableRoles);
        Assert.False(vm.HasActiveFilter);

        vm.SearchQuery = "bob";
        Assert.True(vm.HasActiveFilter);
        vm.SearchQuery = string.Empty;

        vm.FilterGroup = "Party";
        Assert.True(vm.HasActiveFilter);
        vm.FilterGroup = null;

        vm.FilterRole = "Hero";
        Assert.True(vm.HasActiveFilter);
        vm.FilterRole = null;

        vm.HideWorldBibleCharacters = true; // hides Alice -> Bob's only link gone -> no nodes
        Assert.True(vm.HasActiveFilter);

        vm.ClearFiltersCommand.Execute(null);
        Assert.False(vm.HasActiveFilter);
        Assert.Null(vm.FilterGroup);
        Assert.Null(vm.FilterRole);
        Assert.False(vm.HideWorldBibleCharacters);
    }

    [Fact]
    public async Task Search_FiltersToMatchingCharacters()
    {
        var vm = await Loaded(
            Char("Bob", [("friend", "Alice")]),
            Char("Alice", [("friend", "Bob")]),
            Char("Zed", [("friend", "Yan")]),
            Char("Yan", [("friend", "Zed")]));
        vm.SearchQuery = "bo"; // only Bob matches by name -> Alice dropped too (not matched)
        // Search narrows the character set; graph rebuilds from the filtered subset.
        Assert.True(vm.Nodes.Count <= 2);
    }

    [Fact]
    public async Task ReCenterChildren_TwoParentsTwoKids()
    {
        // Dad & Mom are co-parents of two kids -> gen0 couple, gen1 two children
        // that get re-centered under the parents' midpoint.
        var vm = await Loaded(
            Char("Kid1", [("father", "Dad"), ("mother", "Mom")]),
            Char("Kid2", [("father", "Dad"), ("mother", "Mom")]),
            Char("Dad"),
            Char("Mom"));
        Assert.Equal(4, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
        Assert.NotEmpty(vm.Edges); // T-edges from couple to kids
    }

    [Fact]
    public async Task RoleBox_WithFamilySideEndpoint_ExpandsBox()
    {
        // Mom is in a family (parent of Kid) and also part of a 3-member "ally" role
        // group with two loose strangers -> the role box greedily expands to include
        // the family-side endpoint.
        var vm = await Loaded(
            Char("Kid", [("father", "Mom")]),
            Char("Mom", [("ally", "S1"), ("ally", "S2")]),
            Char("S1", [("ally", "Mom"), ("ally", "S2")]),
            Char("S2", [("ally", "Mom"), ("ally", "S1")]));
        Assert.Contains(vm.GroupBoxes, b => b.Label.Equals("ally", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
    }

    [Fact]
    public async Task MultiGenerationPseudoAnchor_AndReversePseudo()
    {
        // Three-generation family: Grandpa -> Dad -> Kid. An external "Ext" is linked
        // by pseudo (cousin) to BOTH Grandpa (gen 0) and Kid (gen 2) -> multi-generation
        // anchor branch. Kid also names Ext as cousin -> reverse-pseudo collection.
        var vm = await Loaded(
            Char("Dad", [("father", "Grandpa")]),
            Char("Kid", [("father", "Dad"), ("nephew", "Ext")]),
            Char("Grandpa"),
            Char("Ext", [("uncle", "Grandpa"), ("nephew", "Kid")]),
            Char("KidB", [("father", "DadB")]),
            Char("DadB"));
        Assert.True(vm.Nodes.Count >= 4);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Contains("Familie"));
    }

    [Fact]
    public async Task LooseRoleGroup_PrePlacesMembers()
    {
        // A 3+ member role group that is entirely loose (no family) exercises the
        // pre-placement of loose role members.
        var vm = await Loaded(
            Char("R1", [("ringmember", "R2"), ("ringmember", "R3")]),
            Char("R2", [("ringmember", "R1"), ("ringmember", "R3")]),
            Char("R3", [("ringmember", "R1"), ("ringmember", "R2")]));
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Contains(vm.GroupBoxes, b => b.Label.Equals("ringmember", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SubModels_NodeEdgeBox()
    {
        var node = new RelationshipNode { Id = "1", Name = "N", X = 10, Y = 20 };
        Assert.Equal("N", node.Name);

        var edge = new RelationshipEdge { X1 = 1, Y1 = 2, X2 = 3, Y2 = 4, Label = "L", LabelX = 2, LabelY = 3 };
        Assert.Equal(new Avalonia.Point(1, 2), edge.StartPoint);
        Assert.Equal(new Avalonia.Point(3, 4), edge.EndPoint);

        var box = new RelationshipGroupBox { X = 0, Y = 0, Width = 100, Height = 50, Label = "B" };
        Assert.Equal(100, box.Width);
        Assert.Empty(box.ExtraMembers);
    }

    // Dense multi-family layout: several sibling couples sharing generations,
    // each with multiple children, to exercise the child-block re-center
    // collision clamp and the role-box overlap-avoidance paths.
    [Fact]
    public async Task DenseFamilies_ExerciseLayoutCollisionPaths()
    {
        var chars = new List<CharacterData>();
        for (int f = 0; f < 5; f++)
        {
            var dad = $"Dad{f}"; var mom = $"Mom{f}";
            chars.Add(Char(dad, new[] { ("wife", mom), ("son", $"K{f}a"), ("son", $"K{f}b"), ("daughter", $"K{f}c") }));
            chars.Add(Char(mom, new[] { ("husband", dad) }));
            chars.Add(Char($"K{f}a", new[] { ("father", dad), ("mother", mom) }));
            chars.Add(Char($"K{f}b", new[] { ("father", dad), ("mother", mom) }));
            chars.Add(Char($"K{f}c", new[] { ("father", dad), ("mother", mom) }));
        }
        for (int f = 1; f < 5; f++)
            chars.First(c => c.Name == $"Dad{f}").Relationships.Add(new EntityRelationship { Role = "brother", Target = $"Dad{f - 1}" });

        var vm = await Loaded(chars.ToArray());
        Assert.NotEmpty(vm.Nodes);
    }
}
