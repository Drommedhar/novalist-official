using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// Covers non-trivial field initializers (e.g. generated GUID ids, default
/// collections) on the plain data models, and the static lookup arrays.
/// </summary>
public class CoreModelInstantiationTests
{
    [Fact]
    public void EntityDataModels_GenerateNonEmptyIds()
    {
        Assert.NotEmpty(new CustomEntityData().Id);
        Assert.NotEmpty(new ItemData().Id);
        Assert.NotEmpty(new LocationData().Id);
        Assert.NotEmpty(new LoreData().Id);
    }

    [Fact]
    public void OtherDataModels_Instantiate()
    {
        Assert.NotEmpty(new PlotlineData().Id);
        Assert.NotEmpty(new SceneSnapshot().Id);
        Assert.NotEmpty(new SmartList().Id);
        Assert.NotNull(new TimelineData().Categories);
        Assert.NotEmpty(new TimelineData().Categories);
        Assert.NotNull(new ResearchItem());
        Assert.NotNull(new SceneFootnote());
        Assert.NotNull(new SceneComment());
    }

    [Fact]
    public void TimelineData_DefaultCategories_AreSeeded()
    {
        var t = new TimelineData();
        Assert.Contains(t.Categories, c => c.Id == "plot");
        Assert.Contains(t.Categories, c => c.Id == "character");
        Assert.Contains(t.Categories, c => c.Id == "world");
        Assert.Equal("vertical", t.ViewMode);
    }

    [Fact]
    public void LoreData_Categories_Exposed()
        => Assert.Contains("Organization", LoreData.Categories);

    [Fact]
    public void WellKnownPropertyTypes_All_ListsBuiltins()
    {
        Assert.Contains(WellKnownPropertyTypes.String, WellKnownPropertyTypes.All);
        Assert.Contains(WellKnownPropertyTypes.EntityRef, WellKnownPropertyTypes.All);
    }

    [Fact]
    public void TemplateKnownFields_ExposeKnownFieldSets()
    {
        Assert.Contains("Role", TemplateKnownFields.Character);
        Assert.Contains("Type", TemplateKnownFields.Location);
        Assert.Contains("Origin", TemplateKnownFields.Item);
        Assert.Contains("Category", TemplateKnownFields.Lore);
    }
}

public class EntityImageBranchTests
{
    [Fact]
    public void Setter_NoSubscriber_DoesNotThrow()
    {
        // Covers the PropertyChanged?.Invoke null branch (no listener attached).
        var img = new EntityImage();
        img.Name = "x";
        img.Path = "y";
        Assert.Equal("x", img.Name);
        Assert.Equal("y", img.Path);
    }

    [Fact]
    public void Setter_PathUnchanged_NoEvent()
    {
        var img = new EntityImage { Path = "p" };
        var raised = false;
        img.PropertyChanged += (_, _) => raised = true;
        img.Path = "p";
        Assert.False(raised);
    }
}
