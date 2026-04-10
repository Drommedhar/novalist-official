using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class EntityEditorView : UserControl
{
    public EntityEditorView()
    {
        InitializeComponent();

        SaveButton.Click += (_, _) => Vm?.SaveCommand.Execute(null);
        DeleteButton.Click += (_, _) => Vm?.DeleteCommand.Execute(null);
        CloseButton.Click += (_, _) => Vm?.CloseCommand.Execute(null);
        StopOverrideButton.Click += (_, _) => Vm?.StopOverrideModeCommand.Execute(null);
        AddRelBtn.Click += (_, _) => Vm?.AddRelationshipCommand.Execute(null);
        AddImgBtn.Click += (_, _) => Vm?.AddImageCommand.Execute(null);
        AddPropBtn.Click += (_, _) => Vm?.AddCustomPropertyCommand.Execute(null);
        AddSecBtn.Click += (_, _) => Vm?.AddSectionCommand.Execute(null);

        // Handle remove buttons inside ItemsControl templates via bubbling
        AddHandler(Button.ClickEvent, OnBubbledClick);
    }

    private EntityEditorViewModel? Vm => DataContext as EntityEditorViewModel;

    private void OnBubbledClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button btn || btn.Tag == null) return;

        var vm = Vm;
        if (vm == null) return;

        switch (btn.Name)
        {
            case "RemRelBtn" when btn.Tag is ObservableRelationship rel:
                vm.RemoveRelationshipCommand.Execute(rel);
                break;
            case "RemImgBtn" when btn.Tag is EntityImage img:
                vm.RemoveImageCommand.Execute(img);
                break;
            case "RemPropBtn" when btn.Tag is ObservableKeyValue kv:
                vm.RemoveCustomPropertyCommand.Execute(kv);
                break;
            case "RemSecBtn" when btn.Tag is ObservableSection sec:
                vm.RemoveSectionCommand.Execute(sec);
                break;
            case "EditOverrideBtn" when btn.Tag is OverrideListItemViewModel editOv:
                vm.EditExistingOverrideCommand.Execute(editOv);
                break;
            case "RemoveOverrideBtn" when btn.Tag is OverrideListItemViewModel removeOv:
                vm.RemoveOverrideCommand.Execute(removeOv);
                break;
        }
    }

    private async void OnSelectProjectImageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EntityImage image } && Vm != null)
        {
            await Vm.SelectProjectImageAsync(image);
            e.Handled = true;
        }
    }

    private async void OnAddRelationshipTargetClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ObservableRelationship relationship } && Vm != null)
        {
            await Vm.AddRelationshipTargetAsync(relationship);
            e.Handled = true;
        }
    }

    private void OnRemoveRelationshipTargetClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ObservableRelationshipTarget target } && Vm != null)
        {
            Vm.RemoveRelationshipTargetCommand.Execute(target);
            e.Handled = true;
        }
    }

    private void OnRelationshipRoleGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ObservableRelationship relationship })
            relationship.HideRoleSuggestions();
    }

    private void OnRelationshipRoleTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is ObservableRelationship relationship && Vm != null)
            UpdateRoleSuggestions(relationship, textBox.Text);
    }

    private void OnRelationshipRoleKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is ObservableRelationship relationship)
        {
            if (e.Key == Key.Escape)
            {
                relationship.HideRoleSuggestions();
                e.Handled = true;
                return;
            }

            if (Vm != null)
                UpdateRoleSuggestions(relationship, textBox.Text);
        }
    }

    private void OnRelationshipTargetGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ObservableRelationship relationship })
            relationship.HideTargetSuggestions();
    }

    private void OnRelationshipTargetTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is ObservableRelationship relationship && Vm != null)
            UpdateTargetSuggestions(relationship, textBox.Text);
    }

    private async void OnRelationshipTargetKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ObservableRelationship relationship || Vm == null)
            return;

        if (e.Key == Key.Enter)
        {
            relationship.HideTargetSuggestions();
            await Vm.AddRelationshipTargetAsync(relationship);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            relationship.HideTargetSuggestions();
            e.Handled = true;
            return;
        }

        UpdateTargetSuggestions(relationship, textBox.Text);
    }

    private void OnRelationshipRoleSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { Tag: ObservableRelationship relationship, SelectedItem: string selectedRole })
            return;

        relationship.Role = selectedRole;
        relationship.HideRoleSuggestions();

        if (sender is ListBox listBox)
            listBox.SelectedItem = null;

        FocusSiblingTextBox(sender, relationship, isTarget: false);
    }

    private async void OnRelationshipTargetSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { Tag: ObservableRelationship relationship, SelectedItem: string selectedTarget } listBox || Vm == null)
            return;

        relationship.PendingTarget = selectedTarget;
        relationship.HideTargetSuggestions();
        listBox.SelectedItem = null;
        await Vm.AddRelationshipTargetAsync(relationship);
        FocusSiblingTextBox(sender, relationship, isTarget: true);
    }

    private void UpdateRoleSuggestions(ObservableRelationship relationship, string? query)
    {
        if (Vm == null)
            return;

        var filtered = FilterSuggestions(Vm.RelationshipRoleSuggestions, query);
        relationship.SetRoleSuggestions(filtered);
    }

    private void UpdateTargetSuggestions(ObservableRelationship relationship, string? query)
    {
        if (Vm == null)
            return;

        var existingTargets = new HashSet<string>(relationship.Targets.Select(target => target.Name), StringComparer.OrdinalIgnoreCase);
        var filtered = FilterSuggestions(Vm.CharacterRelationshipSuggestions, query)
            .Where(candidate => !existingTargets.Contains(candidate))
            .ToList();
        relationship.SetTargetSuggestions(filtered);
    }

    // ── Parent location suggestion handlers ─────────────────────────

    private void OnParentLocationGotFocus(object? sender, FocusChangedEventArgs e)
    {
        Vm?.HideParentLocationSuggestions();
    }

    private void OnParentLocationTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && Vm != null)
            UpdateParentLocationSuggestions(textBox.Text);
    }

    private void OnParentLocationKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || Vm == null) return;

        if (e.Key == Key.Escape)
        {
            Vm.HideParentLocationSuggestions();
            e.Handled = true;
            return;
        }

        UpdateParentLocationSuggestions(textBox.Text);
    }

    private void OnParentLocationSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string selected } listBox || Vm == null) return;

        Vm.ParentLocation = selected;
        Vm.HideParentLocationSuggestions();
        Vm.ScheduleAutoSave();
        listBox.SelectedItem = null;
        ParentLocationInput?.Focus();
    }

    private void UpdateParentLocationSuggestions(string? query)
    {
        if (Vm == null) return;
        var filtered = FilterSuggestions(Vm.AllLocationNames, query);
        Vm.SetParentLocationSuggestions(filtered);
    }

    // ── EntityRef field suggestion handlers ─────────────────────────

    private void OnEntityRefGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ObservableKeyValue kv })
            kv.HideEntityRefSuggestions();
    }

    private void OnEntityRefTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ObservableKeyValue kv)
            UpdateEntityRefSuggestions(kv, tb.Text);
    }

    private void OnEntityRefKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not ObservableKeyValue kv) return;

        if (e.Key == Key.Escape)
        {
            kv.HideEntityRefSuggestions();
            e.Handled = true;
            return;
        }

        UpdateEntityRefSuggestions(kv, tb.Text);
    }

    private void OnEntityRefSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string selected, Tag: ObservableKeyValue kv } listBox) return;

        kv.Value = selected;
        kv.HideEntityRefSuggestions();
        Vm?.ScheduleAutoSave();
        listBox.SelectedItem = null;
    }

    private static void UpdateEntityRefSuggestions(ObservableKeyValue kv, string? query)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            kv.HideEntityRefSuggestions();
            return;
        }

        var filtered = kv.AllEntityRefNames
            .Where(n => n.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();
        kv.SetEntityRefSuggestions(filtered);
    }

    private static IReadOnlyList<string> FilterSuggestions(IEnumerable<string> source, string? query)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return [];

        return source
            .Where(item => item.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static void FocusSiblingTextBox(object? sender, ObservableRelationship relationship, bool isTarget)
    {
        var source = sender as Control;
        var relationshipContainer = source?.FindAncestorOfType<Grid>();
        if (relationshipContainer == null)
            return;

        var textBoxes = relationshipContainer.GetVisualDescendants().OfType<TextBox>().ToList();
        if (textBoxes.Count == 0)
            return;

        var targetTextBox = isTarget ? textBoxes.LastOrDefault() : textBoxes.FirstOrDefault();
        targetTextBox?.Focus();
    }
}
