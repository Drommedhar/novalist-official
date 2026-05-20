using System;
using Avalonia.Controls;
using Novalist.Desktop.Localization;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

// Probe: confirm each non-WebView view's XAML loads under the headless app.
[Collection("Avalonia")]
public class ViewLoadProbeTests
{
    [AvaloniaTheory]
    [InlineData(typeof(Novalist.Desktop.Views.DashboardView))]
    [InlineData(typeof(Novalist.Desktop.Views.TimelineView))]
    [InlineData(typeof(Novalist.Desktop.Views.FootnotesPanelView))]
    [InlineData(typeof(Novalist.Desktop.Views.CodexHubView))]
    [InlineData(typeof(Novalist.Desktop.Views.ExportView))]
    [InlineData(typeof(Novalist.Desktop.Views.GitView))]
    [InlineData(typeof(Novalist.Desktop.Views.ResearchView))]
    [InlineData(typeof(Novalist.Desktop.Views.SceneNotesView))]
    [InlineData(typeof(Novalist.Desktop.Views.PlotGridView))]
    [InlineData(typeof(Novalist.Desktop.Views.WelcomeView))]
    [InlineData(typeof(Novalist.Desktop.Views.HotkeySettingsView))]
    [InlineData(typeof(Novalist.Desktop.Views.ImageGalleryView))]
    [InlineData(typeof(Novalist.Desktop.Views.FocusPeekCardView))]
    [InlineData(typeof(Novalist.Desktop.Views.SmartListsPanelView))]
    [InlineData(typeof(Novalist.Desktop.Views.SettingsView))]
    [InlineData(typeof(Novalist.Desktop.Views.CalendarView))]
    [InlineData(typeof(Novalist.Desktop.Views.ContextSidebarView))]
    [InlineData(typeof(Novalist.Desktop.Views.EntityEditorView))]
    [InlineData(typeof(Novalist.Desktop.Views.EntityPanelView))]
    [InlineData(typeof(Novalist.Desktop.Views.ExplorerView))]
    [InlineData(typeof(Novalist.Desktop.Views.RelationshipsGraphView))]
    [InlineData(typeof(Novalist.Desktop.Views.ExtensionsView))]
    public void View_Instantiates(Type viewType)
    {
        Loc.Instance.Initialize(
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");
        var view = (Control)Activator.CreateInstance(viewType)!;
        Assert.NotNull(view);
    }
}
