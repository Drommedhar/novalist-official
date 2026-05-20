using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

[Collection("Avalonia")]
public class EntityEditorViewTests : IDisposable
{
    static EntityEditorViewTests()
        => Loc.Instance.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");

    private readonly List<EntityEditorViewModel> _vms = new();

    // Cancel each VM's pending 1500ms autosave so its background SaveAsync never fires
    // after the test ends — otherwise it raises PropertyChanged on a still-bound view
    // from a threadpool thread and poisons sibling Avalonia-collection tests.
    public void Dispose()
    {
        var field = typeof(EntityEditorViewModel).GetField("_autoSaveCts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var vm in _vms)
            (field?.GetValue(vm) as System.Threading.CancellationTokenSource)?.Cancel();
    }

    private EntityEditorViewModel BuildVm(out IEntityService entity)
    {
        entity = Substitute.For<IEntityService>();
        entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        entity.LoadLocationsAsync().Returns(new List<LocationData>());
        entity.LoadItemsAsync().Returns(new List<ItemData>());
        entity.LoadLoreAsync().Returns(new List<LoreData>());
        entity.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());
        entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        entity.SaveCharacterAsync(Arg.Any<CharacterData>()).Returns(Task.CompletedTask);
        entity.MigrateRelationshipDuplicatesAsync().Returns(0);
        entity.GetImageFullPath(Arg.Any<string>()).Returns(ci => "C:/p/" + (string)ci[0]);

        var settings = Substitute.For<ISettingsService>();
        var app = new AppSettings();
        settings.Settings.Returns(app);
        settings.Effective.Returns(app);
        settings.SaveAsync().Returns(Task.CompletedTask);

        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        proj.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());

        var vm = new EntityEditorViewModel(entity, settings, proj);
        _vms.Add(vm);
        return vm;
    }

    private static KeyEventArgs KeyUp(Key k) => new() { Key = k, RoutedEvent = InputElement.KeyUpEvent };
    private static RoutedEventArgs R() => new();

    [AvaloniaFact]
    public void Ctor_ButtonClicks_RouteToCommands()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };

        foreach (var name in new[] { "SaveButton", "DeleteButton", "CloseButton", "StopOverrideButton",
                                     "AddRelBtn", "AddImgBtn", "AddPropBtn", "AddSecBtn" })
        {
            var btn = view.GetVisualNamed<Button>(name);
            btn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        // No DataContext -> command lambdas null-guard
        var bare = new EntityEditorView();
        bare.GetVisualNamed<Button>("SaveButton")?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void OnBubbledClick_AllCases_AndGuards()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };

        // guards: non-button source, button without tag, vm null
        DialogHost.Invoke(view, "OnBubbledClick", null, new RoutedEventArgs { Source = new TextBox() });
        DialogHost.Invoke(view, "OnBubbledClick", null, new RoutedEventArgs { Source = new Button() });
        var bare = new EntityEditorView();
        DialogHost.Invoke(bare, "OnBubbledClick", null, new RoutedEventArgs { Source = new Button { Name = "RemRelBtn", Tag = new ObservableRelationship("r", "") } });

        void Bubble(string name, object tag)
            => DialogHost.Invoke(view, "OnBubbledClick", null, new RoutedEventArgs { Source = new Button { Name = name, Tag = tag } });

        Bubble("RemRelBtn", new ObservableRelationship("r", ""));
        Bubble("RemImgBtn", new EntityImage { Name = "i", Path = "i.png" });
        Bubble("RemPropBtn", new ObservableKeyValue("k", "v"));
        Bubble("RemSecBtn", new ObservableSection("s", ""));
        Bubble("EditOverrideBtn", new OverrideListItemViewModel(new CharacterOverride(), "Chap"));
        Bubble("RemoveOverrideBtn", new OverrideListItemViewModel(new CharacterOverride(), "Chap"));
        Bubble("UnknownBtn", "x"); // no case matches
    }

    [AvaloniaFact]
    public void SelectProjectImage_AddTarget_RemoveTarget_Clicks()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };

        DialogHost.Invoke(view, "OnSelectProjectImageClicked", new Button { Tag = new EntityImage { Name = "p", Path = "p.png" } }, R());
        DialogHost.Invoke(view, "OnSelectProjectImageClicked", new Button(), R()); // no tag -> skip

        var rel = new ObservableRelationship("brother", "");
        DialogHost.Invoke(view, "OnAddRelationshipTargetClicked", new Button { Tag = rel }, R());
        DialogHost.Invoke(view, "OnAddRelationshipTargetClicked", new Button(), R());

        var target = new ObservableRelationshipTarget("Robb", rel);
        DialogHost.Invoke(view, "OnRemoveRelationshipTargetClicked", new Button { Tag = target }, R());
        DialogHost.Invoke(view, "OnRemoveRelationshipTargetClicked", new Button(), R());
    }

    [AvaloniaFact]
    public void Relationship_Role_FocusTextChangedKeyUp()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };
        var rel = new ObservableRelationship("bro", "");
        var tb = new TextBox { DataContext = rel, Text = "bro" };

        DialogHost.Invoke(view, "OnRelationshipRoleGotFocus", tb, (FocusChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnRelationshipRoleTextChanged", tb, (TextChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnRelationshipRoleKeyUp", tb, KeyUp(Key.B));
        DialogHost.Invoke(view, "OnRelationshipRoleKeyUp", tb, KeyUp(Key.Escape));
        DialogHost.Invoke(view, "OnRelationshipRoleKeyUp", new TextBox(), KeyUp(Key.B)); // wrong DC -> skip
        DialogHost.Invoke(view, "OnRelationshipRoleGotFocus", new TextBox(), (FocusChangedEventArgs?)null);
    }

    [AvaloniaFact]
    public void Relationship_Target_FocusTextChangedKeyUp()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };
        var rel = new ObservableRelationship("brother", "");
        var tb = new TextBox { DataContext = rel, Text = "Ro" };

        DialogHost.Invoke(view, "OnRelationshipTargetGotFocus", tb, (FocusChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnRelationshipTargetTextChanged", tb, (TextChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnRelationshipTargetKeyUp", tb, KeyUp(Key.R));      // update
        DialogHost.Invoke(view, "OnRelationshipTargetKeyUp", tb, KeyUp(Key.Enter));  // add target
        DialogHost.Invoke(view, "OnRelationshipTargetKeyUp", tb, KeyUp(Key.Escape)); // hide
        DialogHost.Invoke(view, "OnRelationshipTargetKeyUp", new Button(), KeyUp(Key.Enter)); // wrong sender
    }

    [AvaloniaFact]
    public void Relationship_SuggestionSelected_Role_And_Target()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };
        var rel = new ObservableRelationship("", "");

        // Grid with sibling text boxes so FocusSiblingTextBox resolves a sibling.
        // Not rooted in a window: avoids racing the VM's background autosave on the
        // dispatcher thread; Focus() on an unrooted control is a harmless no-op.
        var roleList = new ListBox { Tag = rel, ItemsSource = new[] { "brother" }, SelectedItem = "brother" };
        var grid = new Grid();
        grid.Children.Add(new TextBox());
        grid.Children.Add(roleList);
        grid.Children.Add(new TextBox());
        DialogHost.Invoke(view, "OnRelationshipRoleSuggestionSelected", roleList, (SelectionChangedEventArgs?)null);
        Assert.Equal("brother", rel.Role);

        var targetList = new ListBox { Tag = rel, ItemsSource = new[] { "Robb" }, SelectedItem = "Robb" };
        grid.Children.Add(targetList);
        DialogHost.Invoke(view, "OnRelationshipTargetSuggestionSelected", targetList, (SelectionChangedEventArgs?)null);

        // wrong sender types -> early return
        DialogHost.Invoke(view, "OnRelationshipRoleSuggestionSelected", new ListBox(), (SelectionChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnRelationshipTargetSuggestionSelected", new ListBox(), (SelectionChangedEventArgs?)null);
    }

    [AvaloniaFact]
    public void Helper_EdgeBranches()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        // Seed suggestions so Filter's Where/Take and the existing-target filter run.
        vm.CharacterRelationshipSuggestions.Add("Robb");
        vm.CharacterRelationshipSuggestions.Add("Bran");
        var view = new EntityEditorView { DataContext = vm };

        // FilterSuggestions: empty query -> []; matching query over source -> Where/Take
        var filter = typeof(EntityEditorView).GetMethod("FilterSuggestions",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var empty = (System.Collections.IEnumerable)filter.Invoke(null, new object?[] { new[] { "x" }, "   " })!;
        Assert.Empty(empty.Cast<object>());
        var some = (System.Collections.IEnumerable)filter.Invoke(null, new object?[] { new[] { "Robb", "Bran" }, "ro" })!;
        Assert.NotEmpty(some.Cast<object>());

        // UpdateTargetSuggestions with a rel that already lists one suggestion -> existing-target filter
        var rel = new ObservableRelationship("brother", "Robb");
        DialogHost.Invoke(view, "UpdateTargetSuggestions", rel, "r");

        // Vm-null guards in UpdateRoleSuggestions / UpdateTargetSuggestions
        var bare = new EntityEditorView();
        DialogHost.Invoke(bare, "UpdateRoleSuggestions", rel, "x");
        DialogHost.Invoke(bare, "UpdateTargetSuggestions", rel, "x");

        // FocusSiblingTextBox: no Grid ancestor -> early return
        var lbNoGrid = new ListBox { Tag = rel, ItemsSource = new[] { "x" }, SelectedItem = "x" };
        DialogHost.Invoke(view, "OnRelationshipRoleSuggestionSelected", lbNoGrid, (SelectionChangedEventArgs?)null);

        // Grid ancestor but no TextBoxes -> the no-textboxes return
        var emptyGrid = new Grid();
        var lbInEmptyGrid = new ListBox { Tag = rel, ItemsSource = new[] { "x" }, SelectedItem = "x" };
        emptyGrid.Children.Add(lbInEmptyGrid);
        DialogHost.Invoke(view, "OnRelationshipRoleSuggestionSelected", lbInEmptyGrid, (SelectionChangedEventArgs?)null);
    }

    [AvaloniaFact]
    public void ParentLocation_Handlers()
    {
        var vm = BuildVm(out _);
        vm.OpenLocation(new LocationData { Name = "Keep" });
        var view = new EntityEditorView { DataContext = vm };
        var tb = new TextBox { Text = "Wint" };

        DialogHost.Invoke(view, "OnParentLocationGotFocus", tb, (FocusChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnParentLocationTextChanged", tb, (TextChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnParentLocationKeyUp", tb, KeyUp(Key.W));
        DialogHost.Invoke(view, "OnParentLocationKeyUp", tb, KeyUp(Key.Escape));
        DialogHost.Invoke(view, "OnParentLocationKeyUp", new Button(), KeyUp(Key.W)); // not textbox

        var list = new ListBox { ItemsSource = new[] { "Winterfell" }, SelectedItem = "Winterfell" };
        DialogHost.Invoke(view, "OnParentLocationSuggestionSelected", list, (SelectionChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnParentLocationSuggestionSelected", new ListBox(), (SelectionChangedEventArgs?)null); // no selection
    }

    [AvaloniaFact]
    public void EntityRef_Handlers()
    {
        var vm = BuildVm(out _);
        vm.OpenCharacter(new CharacterData { Name = "Jon" });
        var view = new EntityEditorView { DataContext = vm };
        var kv = new ObservableKeyValue("Ally", "") { AllEntityRefNames = new List<string> { "Robb Stark", "Arya Stark" } };
        var tb = new TextBox { DataContext = kv, Text = "Sta" };

        DialogHost.Invoke(view, "OnEntityRefGotFocus", tb, (FocusChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnEntityRefTextChanged", tb, (TextChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnEntityRefKeyUp", tb, KeyUp(Key.A));
        tb.Text = "   "; // whitespace -> hide branch
        DialogHost.Invoke(view, "OnEntityRefKeyUp", tb, KeyUp(Key.A));
        DialogHost.Invoke(view, "OnEntityRefKeyUp", tb, KeyUp(Key.Escape));
        DialogHost.Invoke(view, "OnEntityRefKeyUp", new Button(), KeyUp(Key.A)); // not textbox

        var list = new ListBox { Tag = kv, ItemsSource = new[] { "Robb Stark" }, SelectedItem = "Robb Stark" };
        DialogHost.Invoke(view, "OnEntityRefSuggestionSelected", list, (SelectionChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnEntityRefSuggestionSelected", new ListBox(), (SelectionChangedEventArgs?)null);
    }
}
