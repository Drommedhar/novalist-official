using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class FocusPeekViewModelTests
{
    private static FocusPeekDisplayData FullData() => new()
    {
        EntityType = EntityType.Character,
        Entity = new object(),
        Title = "Bob",
        TypeLabel = "Character",
        TypeBadgeBackground = "#fff",
        Description = "desc",
        ChapterInfo = "Ch 1",
        Pills = [new FocusPeekPillItem { Text = "p" }],
        Relationships = [new FocusPeekRelationshipItem("Ally", [])],
        AppearanceProperties = [new FocusPeekPropertyItem { Key = "Hair", Value = "Brown" }],
        CustomProperties = [new FocusPeekPropertyItem { Key = "X", Value = "Y" }],
        Sections = [new FocusPeekSectionItem { Title = "Bio", Content = "story" },
                    new FocusPeekSectionItem { Title = "B2", Content = "" }],
        Images = [new FocusPeekImageItem { Name = "a", Path = "a.png" },
                  new FocusPeekImageItem { Name = "b", Path = "b.png" }],
        AiFindings = [new FocusPeekAiFindingItem { Type = "reference", Title = "t", Description = "d", Excerpt = "e" }],
        MapPins = [new FocusPeekMapPinItem("m1", "Map", "p1", "Pin", (_, _) => Task.CompletedTask)],
    };

    [Fact]
    public void Show_PopulatesEverything_SetsOpen_SelectsFirsts()
    {
        var vm = new FocusPeekViewModel();
        vm.Show(FullData(), 100, 200);

        Assert.True(vm.IsOpen);
        Assert.Equal("Bob", vm.Title);
        Assert.Equal("Character", vm.TypeLabel);
        Assert.Equal(EntityType.Character, vm.CurrentEntityType);
        Assert.NotNull(vm.CurrentEntity);
        Assert.Equal(100, vm.Left);
        Assert.Equal(200, vm.Top);

        Assert.True(vm.HasPills);
        Assert.True(vm.HasRelationships);
        Assert.True(vm.HasAppearanceProperties);
        Assert.True(vm.HasCustomProperties);
        Assert.True(vm.HasDescription);
        Assert.True(vm.HasChapterInfo);
        Assert.True(vm.HasSections);
        Assert.True(vm.HasAiFindings);
        Assert.False(vm.ShowAiStub); // findings present
        Assert.True(vm.HasMapPins);
        Assert.True(vm.HasImage);
        Assert.True(vm.HasMultipleImages);
        Assert.Equal("a.png", vm.SelectedImagePath);
        Assert.Equal("story", vm.SelectedSectionContent);
        Assert.True(vm.HasSelectedSectionContent);
    }

    [Fact]
    public void Hide_ClearsEverything()
    {
        var vm = new FocusPeekViewModel();
        vm.Show(FullData(), 1, 1);
        vm.Hide();

        Assert.False(vm.IsOpen);
        Assert.Null(vm.CurrentEntity);
        Assert.False(vm.HasPills);
        Assert.False(vm.HasRelationships);
        Assert.False(vm.HasSections);
        Assert.False(vm.HasImage);
        Assert.True(vm.ShowAiStub); // no findings -> stub shown
        Assert.Equal(string.Empty, vm.Title);
        Assert.Equal(string.Empty, vm.Description);
    }

    [Fact]
    public void SetPinned_TogglesLabelAndGlyph()
    {
        var vm = new FocusPeekViewModel();
        vm.SetPinned(true);
        Assert.True(vm.IsPinned);
        Assert.Equal("●", vm.PinButtonGlyph);
        var pinnedLabel = vm.PinButtonLabel;
        vm.SetPinned(false);
        Assert.Equal("○", vm.PinButtonGlyph);
        Assert.NotEqual(pinnedLabel, vm.PinButtonLabel);
    }

    [Fact]
    public void SetPointerOverCard_And_UpdatePosition()
    {
        var vm = new FocusPeekViewModel();
        vm.SetPointerOverCard(true);
        Assert.True(vm.IsPointerOverCard);
        vm.UpdatePosition(7, 9);
        Assert.Equal(7, vm.Left);
        Assert.Equal(9, vm.Top);
    }

    [Fact]
    public void Commands_InvokeCallbacks()
    {
        var vm = new FocusPeekViewModel();
        bool close = false, pin = false, open = false;
        vm.CloseRequested = () => close = true;
        vm.TogglePinRequested = () => pin = true;
        vm.OpenRequested = () => open = true;
        vm.CloseCommand.Execute(null);
        vm.TogglePinCommand.Execute(null);
        vm.OpenEntityCommand.Execute(null);
        Assert.True(close && pin && open);
    }

    [Fact]
    public void Commands_NullCallbacks_NoThrow()
    {
        var vm = new FocusPeekViewModel();
        vm.CloseCommand.Execute(null);
        vm.TogglePinCommand.Execute(null);
        vm.OpenEntityCommand.Execute(null);
    }

    [Fact]
    public void SelectedImageAndSection_Setters_NotifyAndCompute()
    {
        var vm = new FocusPeekViewModel();
        vm.SelectedImage = new FocusPeekImageItem { Name = "x", Path = "x.png" };
        Assert.True(vm.HasImage);
        Assert.Equal("x.png", vm.SelectedImagePath);
        vm.SelectedImage = null;
        Assert.False(vm.HasImage);
        Assert.Equal(string.Empty, vm.SelectedImagePath);

        vm.SelectedSection = new FocusPeekSectionItem { Title = "T", Content = "C" };
        Assert.Equal("C", vm.SelectedSectionContent);
        vm.SelectedSection = null;
        Assert.Equal(string.Empty, vm.SelectedSectionContent);
    }

    [Fact]
    public async Task MapPinItem_DisplayText_AndNavigate()
    {
        string? navMap = null;
        var pin = new FocusPeekMapPinItem("m1", "Map", "p1", "Pin", (m, _) => { navMap = m; return Task.CompletedTask; });
        Assert.Equal("Pin · Map", pin.DisplayText);
        await pin.NavigateCommand.ExecuteAsync(null);
        Assert.Equal("m1", navMap);

        Assert.Equal("Map", new FocusPeekMapPinItem("m", "Map", "p", "", (_, _) => Task.CompletedTask).DisplayText);
        Assert.Equal("Pin", new FocusPeekMapPinItem("m", "", "p", "Pin", (_, _) => Task.CompletedTask).DisplayText);
    }

    [Fact]
    public void PillItem_IconAndOpacity()
    {
        var withIcon = new FocusPeekPillItem { Text = "t", IconPath = "M0", Dim = true };
        Assert.True(withIcon.HasIcon);
        Assert.Equal(0.75, withIcon.DisplayOpacity);
        var plain = new FocusPeekPillItem { Text = "t" };
        Assert.False(plain.HasIcon);
        Assert.Equal(1.0, plain.DisplayOpacity);
    }

    [Fact]
    public void SectionAndImage_ToString()
    {
        Assert.Equal("Bio", new FocusPeekSectionItem { Title = "Bio", Content = "c" }.ToString());
        Assert.Equal("a", new FocusPeekImageItem { Name = "a", Path = "p" }.ToString());
    }

    [Fact]
    public async Task RelationshipTarget_NavigateGatedByCanNavigate()
    {
        bool ran = false;
        var canNav = new FocusPeekRelationshipTarget("Bob", _ => { ran = true; return Task.CompletedTask; }, true, true);
        Assert.True(canNav.CanNavigate);
        Assert.True(canNav.ShowSeparator);
        Assert.True(canNav.NavigateCommand.CanExecute(null));
        await canNav.NavigateCommand.ExecuteAsync(null);
        Assert.True(ran);

        var noNav = new FocusPeekRelationshipTarget("X", _ => Task.CompletedTask, false, false);
        Assert.False(noNav.NavigateCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("reference", "arrow", "#3498db")]
    [InlineData("inconsistency", "warn", "#e74c3c")]
    [InlineData("suggestion", "bullet", "#f39c12")]
    [InlineData("other", "bullet", "#95a5a6")]
    public void AiFinding_IconAndColorByType(string type, string iconKey, string color)
    {
        // iconKey is an ascii token mapped to the production glyph via escapes, so
        // this source file stays free of emoji-range glyphs.
        var expectedIcon = iconKey switch
        {
            "arrow" => ((char)0x2192).ToString(),
            "warn" => ((char)0x26A0).ToString(),
            _ => ((char)0x2022).ToString(),
        };
        var f = new FocusPeekAiFindingItem { Type = type, Title = "t", Description = "d" };
        Assert.Equal(expectedIcon, f.TypeIcon);
        Assert.Equal(color, f.TypeColor);
    }

    [Fact]
    public void AiFinding_HasExcerpt()
    {
        Assert.True(new FocusPeekAiFindingItem { Type = "x", Title = "t", Description = "d", Excerpt = "e" }.HasExcerpt);
        Assert.False(new FocusPeekAiFindingItem { Type = "x", Title = "t", Description = "d" }.HasExcerpt);
    }

    [Fact]
    public void RelationshipItem_ExposesRoleAndTargets()
    {
        var rel = new FocusPeekRelationshipItem("Mentor", [new FocusPeekRelationshipTarget("Y", _ => Task.CompletedTask, true, false)]);
        Assert.Equal("Mentor", rel.Role);
        Assert.Single(rel.Targets);
    }
}
