using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
public class EntityPanelViewTests
{
    static EntityPanelViewTests()
        => Loc.Instance.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");

    private static EntityPanelViewModel BuildVm()
    {
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns(new List<CharacterData>());
        ent.LoadLocationsAsync().Returns(new List<LocationData>());
        ent.LoadItemsAsync().Returns(new List<ItemData>());
        ent.LoadLoreAsync().Returns(new List<LoreData>());
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        ent.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());
        var proj = Substitute.For<IProjectService>();
        return new EntityPanelViewModel(ent, proj);
    }

    private static RoutedEventArgs R() => new();

    [AvaloniaFact]
    public void OnCreateClick_EachType_AndNoDataContext()
    {
        var vm = BuildVm();
        var view = new EntityPanelView { DataContext = vm };
        foreach (var t in new[] { EntityType.Character, EntityType.Location, EntityType.Item, EntityType.Lore, EntityType.Custom })
        {
            vm.ActiveEntityType = t;
            DialogHost.Invoke(view, "OnCreateClick", null, R());
        }
        // default switch arm: an out-of-range type falls through to Task.CompletedTask
        vm.ActiveEntityType = (EntityType)999;
        DialogHost.Invoke(view, "OnCreateClick", null, R());
        // no-DataContext guard
        DialogHost.Invoke(new EntityPanelView(), "OnCreateClick", null, R());
    }

    [AvaloniaFact]
    public void OnItemClick_Branches()
    {
        var vm = BuildVm();
        var view = new EntityPanelView { DataContext = vm };

        // non-button source -> return
        DialogHost.Invoke(view, "OnItemClick", null, new RoutedEventArgs { Source = new TextBox() });

        // button with no tag (and no ancestor tag) -> return
        DialogHost.Invoke(view, "OnItemClick", null, new RoutedEventArgs { Source = new Button() });

        // button tagged with a CharacterListItemViewModel -> early return (handled elsewhere)
        var charBtn = new Button { Tag = new CharacterListItemViewModel(new CharacterData { Name = "C" }) };
        DialogHost.Invoke(view, "OnItemClick", null, new RoutedEventArgs { Source = charBtn });

        // button tagged with a plain entity -> OpenEntityCommand
        var locBtn = new Button { Tag = new LocationData { Name = "Keep" } };
        DialogHost.Invoke(view, "OnItemClick", null, new RoutedEventArgs { Source = locBtn });

        // no DataContext
        DialogHost.Invoke(new EntityPanelView(), "OnItemClick", null, new RoutedEventArgs { Source = locBtn });
    }

    [AvaloniaFact]
    public void MenuHandlers_AllTags_AndGuards()
    {
        var vm = BuildVm();
        var view = new EntityPanelView { DataContext = vm };

        void Mi(string handler, object tag) => DialogHost.Invoke(view, handler, new MenuItem { Tag = tag }, R());
        void Wrong(string handler) => DialogHost.Invoke(view, handler, new MenuItem { Tag = "x" }, R());

        var ch = new CharacterData { Name = "C" };
        var loc = new LocationData { Name = "L" };
        var it = new ItemData { Name = "I" };
        var lo = new LoreData { Name = "Lo" };
        var ce = new CustomEntityData { Name = "CE", EntityTypeKey = "k" };
        var td = new CustomEntityTypeDefinition { TypeKey = "k" };

        Mi("OnDeleteCharacterClick", ch);
        Mi("OnDeleteLocationClick", loc);
        Mi("OnDeleteItemClick", it);
        Mi("OnDeleteLoreClick", lo);
        Mi("OnDeleteCustomEntityClick", ce);
        Mi("OnMoveCharacterToWBClick", ch);
        Mi("OnMoveLocationToWBClick", loc);
        Mi("OnMoveItemToWBClick", it);
        Mi("OnMoveLoreToWBClick", lo);
        Mi("OnMoveCustomEntityToWBClick", ce);
        Mi("OnEditEntityTypeClick", td);
        Mi("OnDeleteEntityTypeClick", td);
        Mi("OnRemoveLocationParentClick", new LocationTreeItemViewModel(loc));

        // wrong-tag guards
        foreach (var h in new[] { "OnDeleteCharacterClick", "OnDeleteLocationClick", "OnDeleteItemClick",
            "OnDeleteLoreClick", "OnDeleteCustomEntityClick", "OnMoveCharacterToWBClick", "OnMoveLocationToWBClick",
            "OnMoveItemToWBClick", "OnMoveLoreToWBClick", "OnMoveCustomEntityToWBClick", "OnEditEntityTypeClick",
            "OnDeleteEntityTypeClick", "OnRemoveLocationParentClick" })
            Wrong(h);
    }

    [AvaloniaFact]
    public void OnLocationTreeSelectionChanged_AddedItem_Empty_NoVm()
    {
        var vm = BuildVm();
        var view = new EntityPanelView { DataContext = vm };
        var node = new LocationTreeItemViewModel(new LocationData { Name = "Keep" });

        var added = new SelectionChangedEventArgs(SelectingItemsControl.SelectionChangedEvent,
            new List<object>(), new List<object> { node });
        DialogHost.Invoke(view, "OnLocationTreeSelectionChanged", null, added);

        var none = new SelectionChangedEventArgs(SelectingItemsControl.SelectionChangedEvent,
            new List<object>(), new List<object>());
        DialogHost.Invoke(view, "OnLocationTreeSelectionChanged", null, none);

        DialogHost.Invoke(new EntityPanelView(), "OnLocationTreeSelectionChanged", null, added); // no vm
    }

    [AvaloniaFact]
    public void OnDragLeave_RemovesClass_And_DragThreshold()
    {
        var view = new EntityPanelView();
        var border = new Border();
        border.Classes.Add("dropTarget");
        DialogHost.Invoke(view, "OnDragLeave", border, (object?)null);
        Assert.DoesNotContain("dropTarget", border.Classes);
        DialogHost.Invoke(view, "OnDragLeave", new TextBox(), (object?)null); // non-border -> no-op

        var threshold = typeof(EntityPanelView).GetMethod("HasExceededDragThreshold",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)threshold.Invoke(view, new object[] { new Point(10, 10) })!);
        Assert.False((bool)threshold.Invoke(view, new object[] { new Point(1, 1) })!);
    }

    [AvaloniaFact]
    public void DataContextChanged_CustomEntityBinding()
    {
        var vm = BuildVm();
        vm.ActiveCustomTypeKey = "k"; // non-null -> UpdateCustomEntityListBinding sets ItemsSource
        var view = new EntityPanelView { DataContext = vm };
        Assert.NotNull(view.GetVisualNamed<ItemsControl>("CustomEntityList")!.ItemsSource);

        // property change re-binds
        vm.ActiveCustomTypeKey = "k2";

        // null-key path (no binding update)
        var vm2 = BuildVm();
        _ = new EntityPanelView { DataContext = vm2 };
    }
}
