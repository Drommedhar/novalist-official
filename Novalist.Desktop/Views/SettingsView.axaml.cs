using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class SettingsView : UserControl
{
    private Dictionary<string, Control>? _sectionMap;

    public SettingsView()
    {
        InitializeComponent();
        CloseButton.Click += OnCloseClick;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            vm.ScrollToCategoryRequested -= OnScrollToCategoryRequested;
            vm.ScrollToCategoryRequested += OnScrollToCategoryRequested;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.CloseCommand.Execute(null);
    }

    private void OnScrollToCategoryRequested(string key)
    {
        _sectionMap ??= new Dictionary<string, Control>
        {
            ["language"] = SectionLanguage,
            ["editor"] = SectionEditor,
            ["writingGoals"] = SectionWritingGoals,
            ["autoReplacement"] = SectionAutoReplacement,
            ["templates"] = SectionTemplates,
            ["hotkeys"] = SectionHotkeys,
            ["general"] = SectionGeneral,
        };

        if (key.StartsWith("ext_", StringComparison.Ordinal))
        {
            SectionExtensions.BringIntoView();
            return;
        }

        if (_sectionMap.TryGetValue(key, out var section) && section.IsVisible)
        {
            section.BringIntoView();
        }
    }
}
