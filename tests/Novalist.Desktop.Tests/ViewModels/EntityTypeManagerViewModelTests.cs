using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class EntityTypeManagerViewModelTests
{
    [Fact]
    public void AddField_RemoveField()
    {
        var vm = new EntityTypeManagerViewModel();
        vm.AddFieldCommand.Execute(null);
        vm.AddFieldCommand.Execute(null);
        Assert.Equal(2, vm.Fields.Count);
        vm.Fields[0].RemoveCommand.Execute(null);
        Assert.Single(vm.Fields);
    }

    [Fact]
    public void FieldRow_ShowEnumAndEntityRef_TrackTypeIndex()
    {
        var vm = new EntityTypeManagerViewModel();
        vm.AddFieldCommand.Execute(null);
        var row = vm.Fields[0];
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.TypeIndex = (int)CustomPropertyType.Enum;
        Assert.True(row.ShowEnumOptions);
        Assert.False(row.ShowEntityRefTarget);
        row.TypeIndex = (int)CustomPropertyType.EntityRef;
        Assert.True(row.ShowEntityRefTarget);
        Assert.Contains(nameof(EntityTypeFieldRowViewModel.ShowEnumOptions), raised);
        Assert.NotEmpty(row.PropertyTypes);
    }

    [Fact]
    public void SetCustomEntityTypes_ExtendsEntityRefTargets()
    {
        var vm = new EntityTypeManagerViewModel();
        vm.AddFieldCommand.Execute(null);
        vm.SetCustomEntityTypes(new[] { new CustomEntityTypeDefinition { DisplayName = "Faction" } });
        Assert.Contains("Faction", vm.Fields[0].EntityRefTargets);
    }

    [Fact]
    public void LoadDefinition_PopulatesFieldsAndFlags()
    {
        var def = new CustomEntityTypeDefinition
        {
            TypeKey = "faction", DisplayName = "Faction", DisplayNamePlural = "Factions", Icon = "X",
            Features = new CustomEntityFeatures { IncludeImages = false, IncludeRelationships = true, IncludeSections = false },
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "leader", DisplayName = "Leader", Type = CustomPropertyType.EntityRef, EnumOptions = new() { "Character" } },
                new CustomEntityFieldDefinition { Key = "kind", DisplayName = "Kind", Type = CustomPropertyType.Enum, EnumOptions = new() { "A", "B" } }
            }
        };
        var vm = new EntityTypeManagerViewModel();
        vm.LoadDefinition(def);

        Assert.True(vm.IsEditing);
        Assert.Equal("faction", vm.TypeKey);
        Assert.True(vm.IncludeRelationships);
        Assert.False(vm.IncludeImages);
        Assert.Equal(2, vm.Fields.Count);
        Assert.Equal("Character", vm.Fields[0].EntityRefTarget);
        Assert.Equal("A, B", vm.Fields[1].EnumOptionsText);
    }

    [Fact]
    public void BuildDefinition_NewType_GeneratesKeyAndDefaults()
    {
        var vm = new EntityTypeManagerViewModel { DisplayName = "My Faction!", Icon = "  " };
        vm.AddFieldCommand.Execute(null);
        var f = vm.Fields[0];
        f.DisplayName = "Member Count";
        f.TypeIndex = (int)CustomPropertyType.Enum;
        f.EnumOptionsText = "a, b, c";
        // A plain String field exercises the EnumOptions switch default arm (-> null).
        vm.AddFieldCommand.Execute(null);
        vm.Fields[1].Key = "motto";
        vm.Fields[1].TypeIndex = (int)CustomPropertyType.String;

        var def = vm.BuildDefinition();
        Assert.Null(def.DefaultFields[1].EnumOptions);
        Assert.Equal("my_faction", def.TypeKey);             // sanitized + trailing _ trimmed
        Assert.Equal("My Faction!s", def.DisplayNamePlural); // fallback + 's'
        Assert.Equal("📋", def.Icon);                         // blank -> default
        Assert.Equal("MemberCount", def.DefaultFields[0].Key); // key from display name (spaces removed)
        Assert.Equal(3, def.DefaultFields[0].EnumOptions!.Count);
    }

    [Fact]
    public void BuildDefinition_Editing_KeepsKey_EntityRefAndDefaults()
    {
        var vm = new EntityTypeManagerViewModel();
        vm.LoadDefinition(new CustomEntityTypeDefinition { TypeKey = "kept", DisplayName = "Kept", DisplayNamePlural = "Kepts" });
        vm.AddFieldCommand.Execute(null);
        var f = vm.Fields[0];
        f.Key = "ref"; f.DisplayName = "Ref"; f.TypeIndex = (int)CustomPropertyType.EntityRef; f.EntityRefTarget = "Location";

        var def = vm.BuildDefinition();
        Assert.Equal("kept", def.TypeKey);                       // editing -> existing key kept
        Assert.Equal(new[] { "Location" }, def.DefaultFields[0].EnumOptions);
    }

    [Fact]
    public void BuildDefinition_BlankDisplayName_GeneratesGuidKey()
    {
        var def = new EntityTypeManagerViewModel().BuildDefinition();
        Assert.StartsWith("custom_", def.TypeKey);
    }
}
