using Novalist.Sdk.Hooks;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Sdk.Tests.Models;

public class ContentPrivacyTests
{
    [Fact]
    public void ContentDefaultsAreEmptyAndVisible()
    {
        var content = new EntityContentInfo();
        Assert.Empty(content.Id);
        Assert.Empty(content.Name);
        Assert.Empty(content.Description);
        Assert.Empty(content.Sections);
        Assert.Empty(content.ImagePaths);
        Assert.Empty(content.Aliases);
        Assert.False(content.ReaderHidden);
        Assert.False(new CustomEntitySectionInfo().ReaderHidden);
        Assert.False(new SceneInfo().Inactive);
        Assert.False(new SceneInfo().ExcludeFromExport);
        Assert.Empty(new MapInfo().ImagePaths);
    }

    [Fact]
    public void NewFieldsRetainAssignedValuesAndInformationIsAnAdditiveDisposition()
    {
        var content = new EntityContentInfo { Id = "id", Name = "name", Description = "description", ReaderHidden = true,
            Sections = [new() { Title = "section", Content = "body", ReaderHidden = true }], ImagePaths = ["Images/a.png"], Aliases = ["alias"] };
        Assert.Equal("id", content.Id);
        Assert.Equal("name", content.Name);
        Assert.Equal("description", content.Description);
        Assert.True(content.ReaderHidden);
        Assert.True(Assert.Single(content.Sections).ReaderHidden);
        Assert.Equal("Images/a.png", Assert.Single(content.ImagePaths));
        Assert.Equal("alias", Assert.Single(content.Aliases));
        var scene = new SceneInfo { Inactive = true, ExcludeFromExport = true };
        Assert.True(scene.Inactive);
        Assert.True(scene.ExcludeFromExport);
        Assert.Equal(3, (int)InlineActionDisposition.ShowInformation);
        Assert.Equal("Images/map.png", Assert.Single(new MapInfo { ImagePaths = ["Images/map.png"] }.ImagePaths));
    }
}
