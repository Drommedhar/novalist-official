using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class TemplateEditorViewModelTests
{
    [Fact]
    public void Ctor_DetectsCharacterTemplate()
    {
        Assert.True(new TemplateEditorViewModel("character").IsCharacterTemplate);
        Assert.False(new TemplateEditorViewModel("location").IsCharacterTemplate);
    }

    [Fact]
    public void Character_LoadThenBuild_RoundTrips()
    {
        var vm = new TemplateEditorViewModel("character");
        var src = new CharacterTemplate
        {
            Name = "Hero",
            BuiltIn = true,
            IncludeRelationships = false,
            IncludeImages = false,
            IncludeChapterOverrides = false,
            AgeMode = "date",
            AgeIntervalUnit = IntervalUnit.Months,
            Fields =
            [
                new TemplateField { Key = "Age", DefaultValue = "" },
                new TemplateField { Key = "Role", DefaultValue = "Knight" },
                new TemplateField { Key = "Custom1", DefaultValue = "v" },
            ],
            CustomPropertyDefs =
            [
                new CustomPropertyDefinition { Key = "rank", Type = CustomPropertyType.Enum, EnumOptions = ["A", "B"] },
                new CustomPropertyDefinition { Key = "lifespan", Type = CustomPropertyType.Timespan, IntervalUnit = IntervalUnit.Days },
            ],
            Sections = [new TemplateSection { Title = "Bio", DefaultContent = "x" }],
        };

        vm.LoadCharacterTemplate(src);

        Assert.Equal("Hero", vm.TemplateName);
        Assert.True(vm.IsBuiltIn);
        Assert.Equal(1, vm.AgeModeIndex);          // date
        Assert.Equal(1, vm.AgeIntervalUnitIndex);  // Months
        Assert.True(vm.ShowAgeMode);               // Age field active
        Assert.True(vm.ShowAgeOptions);            // date mode
        Assert.Single(vm.CustomFields);            // Custom1 (Age/Role are known)
        Assert.Equal(2, vm.PropertyDefs.Count);
        Assert.Single(vm.Sections);

        var built = vm.BuildCharacterTemplate("id1");
        Assert.Equal("id1", built.Id);
        Assert.Equal("date", built.AgeMode);
        Assert.Equal(IntervalUnit.Months, built.AgeIntervalUnit);
        Assert.Contains(built.Fields, f => f.Key == "Role" && f.DefaultValue == "Knight");
        Assert.Contains(built.Fields, f => f.Key == "Custom1");
        var rank = built.CustomPropertyDefs.Single(d => d.Key == "rank");
        Assert.Equal(CustomPropertyType.Enum, rank.Type);
        Assert.Equal(new[] { "A", "B" }, rank.EnumOptions);
        var life = built.CustomPropertyDefs.Single(d => d.Key == "lifespan");
        Assert.Equal(IntervalUnit.Days, life.IntervalUnit);
        Assert.Single(built.Sections);
    }

    [Fact]
    public void Character_NewTemplate_AllKnownFieldsActive_NoDateMode()
    {
        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(new CharacterTemplate { Name = "Blank" }); // no fields -> isNew
        Assert.All(vm.KnownFields, f => Assert.True(f.IsActive));
        Assert.Equal(0, vm.AgeModeIndex); // number
        Assert.True(vm.ShowAgeMode);      // age active (new)
        Assert.False(vm.ShowAgeOptions);  // not date mode
        var built = vm.BuildCharacterTemplate("x");
        Assert.Null(built.AgeMode);       // number mode -> null
        Assert.Null(built.AgeIntervalUnit);
    }

    [Fact]
    public void Location_Item_Lore_RoundTrip()
    {
        var loc = new TemplateEditorViewModel("location");
        loc.LoadLocationTemplate(new LocationTemplate { Name = "L", Fields = [new TemplateField { Key = "Type", DefaultValue = "City" }] });
        Assert.Equal("L", loc.TemplateName);
        var lb = loc.BuildLocationTemplate("l1");
        Assert.Equal("l1", lb.Id);
        Assert.Contains(lb.Fields, f => f.Key == "Type");

        var item = new TemplateEditorViewModel("item");
        item.LoadItemTemplate(new ItemTemplate { Name = "I" });
        Assert.Equal("i1", item.BuildItemTemplate("i1").Id);

        var lore = new TemplateEditorViewModel("lore");
        lore.LoadLoreTemplate(new LoreTemplate { Name = "Lo" });
        Assert.Equal("lo1", lore.BuildLoreTemplate("lo1").Id);
    }

    [Fact]
    public void CustomEntity_LoadBuild_RoundTripsTypeKey()
    {
        var vm = new TemplateEditorViewModel("faction");
        string[] known = ["Banner", "Motto"];
        vm.LoadCustomEntityTemplate(new CustomEntityTemplate
        {
            Name = "Reds", EntityTypeKey = "faction", IncludeRelationships = true,
            Fields = [new TemplateField { Key = "Banner", DefaultValue = "red" }, new TemplateField { Key = "Extra", DefaultValue = "e" }]
        }, known);

        Assert.Single(vm.CustomFields); // Extra (Banner is known)
        var built = vm.BuildCustomEntityTemplate("c1");
        Assert.Equal("faction", built.EntityTypeKey);
        Assert.True(built.IncludeRelationships);
    }

    [Fact]
    public void CustomEntity_NullTypeKey_BuildsEmpty()
    {
        var vm = new TemplateEditorViewModel("faction");
        var built = vm.BuildCustomEntityTemplate("c1"); // no Load -> _customEntityTypeKey null
        Assert.Equal(string.Empty, built.EntityTypeKey);
    }

    [Fact]
    public void AddAndRemove_CustomField_Property_Section()
    {
        var vm = new TemplateEditorViewModel("location");

        vm.AddCustomFieldCommand.Execute(null);
        Assert.Single(vm.CustomFields);
        vm.CustomFields[0].RemoveCommand.Execute(null);
        Assert.Empty(vm.CustomFields);

        vm.AddPropertyCommand.Execute(null);
        Assert.Single(vm.PropertyDefs);
        vm.PropertyDefs[0].RemoveCommand.Execute(null);
        Assert.Empty(vm.PropertyDefs);

        vm.AddSectionCommand.Execute(null);
        Assert.Single(vm.Sections);
        vm.Sections[0].RemoveCommand.Execute(null);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void AgeMode_Change_TogglesShowAgeOptions()
    {
        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(new CharacterTemplate { Name = "C" }); // age active, number
        Assert.False(vm.ShowAgeOptions);
        vm.AgeModeIndex = 1; // date -> OnAgeModeIndexChanged
        Assert.True(vm.ShowAgeOptions);
    }

    [Fact]
    public void KnownField_AgeDeactivated_HidesAgeMode()
    {
        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(new CharacterTemplate { Name = "C" });
        var age = vm.KnownFields.First(f => f.FieldKey == "Age");
        Assert.True(vm.ShowAgeMode);
        age.IsActive = false; // ActiveChanged -> UpdateShowAgeOptions
        Assert.False(vm.ShowAgeMode);
    }

    [Fact]
    public void KnownFieldRow_DisplayName_ShowDefaultValue()
    {
        var mapped = new KnownFieldRowViewModel { FieldKey = "Gender", IsActive = true, IsAgeField = false };
        Assert.False(string.IsNullOrEmpty(mapped.DisplayName)); // resolves via loc map
        Assert.True(mapped.ShowDefaultValue);

        var unmapped = new KnownFieldRowViewModel { FieldKey = "Zzz" };
        Assert.Equal("Zzz", unmapped.DisplayName);

        var age = new KnownFieldRowViewModel { FieldKey = "Age", IsActive = true, IsAgeField = true };
        Assert.False(age.ShowDefaultValue); // age field hides default
    }

    [Fact]
    public void PropertyDefRow_TypeIndex_TogglesFlags()
    {
        var row = new CustomPropertyDefRowViewModel(["s", "i", "b", "d", "e", "t"]);
        Assert.Equal(6, row.PropertyTypes.Length);
        Assert.Equal(3, row.IntervalUnits.Length);

        row.TypeIndex = (int)CustomPropertyType.Enum;
        Assert.True(row.ShowEnumOptions);
        row.TypeIndex = (int)CustomPropertyType.Timespan;
        Assert.True(row.ShowIntervalUnit);
        Assert.False(row.ShowEnumOptions);
        row.TypeIndex = (int)CustomPropertyType.Bool;
        Assert.True(row.IsBoolType);
    }

    [Fact]
    public void DaysIntervalUnit_LoadAndBuild()
    {
        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(new CharacterTemplate
        {
            Name = "D",
            AgeMode = "date",
            AgeIntervalUnit = IntervalUnit.Days, // load: AgeIntervalUnitIndex -> 2
            CustomPropertyDefs =
            [
                new CustomPropertyDefinition { Key = "span", Type = CustomPropertyType.Timespan, IntervalUnit = IntervalUnit.Days }
            ],
        });
        Assert.Equal(2, vm.AgeIntervalUnitIndex);
        Assert.Equal(2, vm.PropertyDefs[0].IntervalUnitIndex);

        // Build: AgeModeIndex stays 1 (date), Days index -> IntervalUnit.Days
        var built = vm.BuildCharacterTemplate("d1");
        Assert.Equal(IntervalUnit.Days, built.AgeIntervalUnit);
        Assert.Equal(IntervalUnit.Days, built.CustomPropertyDefs.Single(d => d.Key == "span").IntervalUnit);
    }

    [Fact]
    public void MonthsLoad_And_YearsDefaultBuild()
    {
        var vm = new TemplateEditorViewModel("location");
        // Months arm on load (LoadPropertyDefs interval switch).
        vm.LoadLocationTemplate(new LocationTemplate
        {
            Name = "M",
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "p", Type = CustomPropertyType.Timespan, IntervalUnit = IntervalUnit.Months }]
        });
        Assert.Equal(1, vm.PropertyDefs[0].IntervalUnitIndex);

        // Years default arm on build: IntervalUnitIndex 0 -> IndexToIntervalUnit(0).
        vm.PropertyDefs[0].TypeIndex = (int)CustomPropertyType.Timespan;
        vm.PropertyDefs[0].IntervalUnitIndex = 0;
        var built = vm.BuildLocationTemplate("m1");
        Assert.Equal(IntervalUnit.Years, built.CustomPropertyDefs.Single(d => d.Key == "p").IntervalUnit);
    }

    [Fact]
    public void BuildPropertyDefs_SkipsEmptyKeys()
    {
        var vm = new TemplateEditorViewModel("location");
        vm.AddPropertyCommand.Execute(null);
        vm.PropertyDefs[0].Key = "   "; // empty -> skipped
        var built = vm.BuildLocationTemplate("l");
        Assert.Empty(built.CustomPropertyDefs);
    }
}
