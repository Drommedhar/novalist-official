using System.Collections.ObjectModel;
using Avalonia.Media;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class SettingsViewModelTests
{
    static SettingsViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public ISettingsService Settings = null!;
        public IProjectService Proj = null!;
        public AppSettings App = null!;
        public ProjectSettings ProjSettings = null!;
        public BookData? Book;
        public ProjectMetadata? Meta;
        public SettingsViewModel Vm = null!;
    }

    private static H Build(bool loaded = false, bool withBook = false)
    {
        var h = new H();
        var settings = Substitute.For<ISettingsService>();
        h.App = new AppSettings();
        settings.Settings.Returns(h.App);
        settings.Effective.Returns(h.App);
        settings.SaveAsync().Returns(Task.CompletedTask);
        h.Settings = settings;

        var proj = Substitute.For<IProjectService>();
        h.ProjSettings = new ProjectSettings { Overrides = new SettingsOverrides() };
        proj.ProjectSettings.Returns(h.ProjSettings);
        proj.IsProjectLoaded.Returns(loaded);
        proj.SaveProjectSettingsAsync().Returns(Task.CompletedTask);
        proj.SaveProjectAsync().Returns(Task.CompletedTask);
        if (loaded)
        {
            h.Meta = new ProjectMetadata { Name = "Proj" };
            proj.CurrentProject.Returns(h.Meta);
        }
        else proj.CurrentProject.Returns((ProjectMetadata?)null);
        if (withBook)
        {
            h.Book = new BookData();
            proj.ActiveBook.Returns(h.Book);
        }
        h.Proj = proj;
        h.Vm = new SettingsViewModel(settings, proj);
        return h;
    }

    [AvaloniaFact]
    public void Constructs_AndExposesLists()
    {
        var h = Build();
        Assert.NotEmpty(h.Vm.Categories);
        Assert.NotEmpty(h.Vm.AvailableThemes);
        Assert.NotEmpty(h.Vm.AvailablePageFormats);
        Assert.NotEmpty(h.Vm.AvailableLanguages);
        Assert.NotEmpty(h.Vm.AvailableUiLanguages);
    }

    [AvaloniaFact]
    public void WatchFilesystem_DefaultsTrue_AndPersistsToProjectSettings()
    {
        var h = Build(loaded: true);
        Assert.True(h.Vm.WatchFilesystem);

        h.Vm.WatchFilesystem = false;

        Assert.False(h.ProjSettings.WatchFilesystem);
        h.Proj.Received().SaveProjectSettingsAsync();
    }

    [AvaloniaFact]
    public void SearchFilter_TogglesSectionVisibility()
    {
        var h = Build();
        h.Vm.SearchQuery = "theme";
        Assert.True(h.Vm.IsAppearanceSectionVisible);
        Assert.False(h.Vm.IsDiagnosticsSectionVisible);

        h.Vm.SearchQuery = "log";
        Assert.True(h.Vm.IsDiagnosticsSectionVisible);
        Assert.False(h.Vm.IsAppearanceSectionVisible);

        h.Vm.SearchQuery = string.Empty; // restores all
        Assert.True(h.Vm.IsAppearanceSectionVisible);
        Assert.True(h.Vm.IsDiagnosticsSectionVisible);
    }

    [AvaloniaFact]
    public void SelectedCategory_ScrollsAndClearsSearch()
    {
        var h = Build();
        string? scrolled = null;
        h.Vm.ScrollToCategoryRequested += k => scrolled = k;
        h.Vm.SearchQuery = "theme";
        h.Vm.SelectedCategory = h.Vm.Categories[1];
        Assert.Equal(h.Vm.Categories[1].Key, scrolled);
        Assert.Equal(string.Empty, h.Vm.SearchQuery); // cleared
    }

    [AvaloniaFact]
    public void LoadExtensionSettingsPages_AddsCategoriesAndPages()
    {
        var h = Build();
        var before = h.Vm.Categories.Count;
        h.Vm.LoadExtensionSettingsPages(
        [
            new SettingsPage { Category = "MyExt", CreateView = () => new Avalonia.Controls.Border() },
        ]);
        Assert.Equal(before + 1, h.Vm.Categories.Count);
        Assert.Single(h.Vm.ExtensionSettingsPages);
        Assert.Equal("MyExt", h.Vm.ExtensionSettingsPages[0].Category);
        Assert.NotNull(h.Vm.ExtensionSettingsPages[0].View);
    }

    // ── Editor settings (global scope) ──────────────────────────────
    [AvaloniaFact]
    public void EditorSettings_WriteGlobalAndClamp()
    {
        var h = Build();
        h.Vm.EditorFontFamily = "Georgia";
        Assert.Equal("Georgia", h.App.EditorFontFamily);

        h.Vm.EditorFontSize = 999; // clamped to 36
        Assert.Equal(36, h.App.EditorFontSize);
        h.Vm.EditorFontSize = 1; // clamped to 8
        Assert.Equal(8, h.App.EditorFontSize);

        h.Vm.BookFontSize = 999; // clamped to 24
        Assert.Equal(24, h.App.BookFontSize);

        h.Vm.EnableBookParagraphSpacing = true;
        Assert.True(h.App.EnableBookParagraphSpacing);
        h.Vm.EnableBookWidth = true;
        Assert.True(h.App.EnableBookWidth);
        h.Vm.BookTextBlockWidth = 600;
        Assert.Equal(600, h.App.BookTextBlockWidth);
        h.Vm.BookFontFamily = "Times";
        Assert.Equal("Times", h.App.BookFontFamily);
        h.Vm.PageViewEnabled = true;
        Assert.True(h.App.PageViewEnabled);
        h.Vm.TypewriterScrollEnabled = true;
        Assert.True(h.App.TypewriterScrollEnabled);

        Assert.False(string.IsNullOrEmpty(h.Vm.BookWidthCharsPerLine));
    }

    [AvaloniaFact]
    public void PageFormat_IsCustom_AndUpdatesPreview()
    {
        var h = Build();
        var custom = h.Vm.AvailablePageFormats.FirstOrDefault(f => f.Code == "Custom");
        if (custom != null)
        {
            h.Vm.SelectedPageFormat = custom;
            Assert.True(h.Vm.IsCustomPageFormat);
        }
        var other = h.Vm.AvailablePageFormats.First(f => f.Code != "Custom");
        h.Vm.SelectedPageFormat = other;
        Assert.False(h.Vm.IsCustomPageFormat);
        Assert.Equal(other.Code, h.App.BookPageFormat);
    }

    [AvaloniaFact]
    public void TypewriterAnchors_MutuallyApply()
    {
        var h = Build();
        h.Vm.TypewriterAnchorMiddle = false; // ctor defaults middle=true; clear so middle->true fires later
        h.Vm.TypewriterAnchorTop = true;
        Assert.Equal("top", h.App.TypewriterScrollAnchor);
        h.Vm.TypewriterAnchorTop = false;
        h.Vm.TypewriterAnchorBottom = true;
        Assert.Equal("bottom", h.App.TypewriterScrollAnchor);
        h.Vm.TypewriterAnchorBottom = false;
        h.Vm.TypewriterAnchorMiddle = true;
        Assert.Equal("middle", h.App.TypewriterScrollAnchor);
    }

    // ── Writing assistance ──────────────────────────────────────────
    [AvaloniaFact]
    public void WritingSettings_LanguagePresetAndToggles()
    {
        var h = Build();
        h.Vm.SelectedLanguage = "de-low";
        Assert.Equal("de-low", h.App.AutoReplacementLanguage);
        Assert.NotEmpty(h.App.AutoReplacements);
        Assert.False(string.IsNullOrEmpty(h.Vm.AutoReplacementPreview));

        h.Vm.DialogueCorrectionEnabled = true;
        Assert.True(h.App.DialogueCorrectionEnabled);
        h.Vm.GrammarCheckEnabled = true;
        Assert.True(h.App.GrammarCheckEnabled);

        h.Vm.GrammarCheckApiUrl = "http://localhost:8081/v2/check";
        Assert.Equal("http://localhost:8081/v2/check", h.App.GrammarCheckApiUrl);
        h.Vm.GrammarCheckApiUrl = "   ";
        Assert.Null(h.App.GrammarCheckApiUrl);

        h.Vm.GrammarCheckApiKey = "test-key";
        Assert.Equal("test-key", h.App.GrammarCheckApiKey);
        h.Vm.GrammarCheckApiKey = "   ";
        Assert.Null(h.App.GrammarCheckApiKey);

        h.Vm.GrammarCheckUsername = "test-user";
        Assert.Equal("test-user", h.App.GrammarCheckUsername);
        h.Vm.GrammarCheckUsername = "   ";
        Assert.Null(h.App.GrammarCheckUsername);
    }

    // ── Appearance: language / theme / accent ───────────────────────
    [AvaloniaFact]
    public void Appearance_UiLanguage_Theme_Accent()
    {
        var h = Build();
        var lang = h.Vm.AvailableUiLanguages.First();
        h.Vm.SelectedUiLanguage = lang;
        Assert.Equal(lang.Code, h.App.Language);

        var theme = h.Vm.AvailableThemes.FirstOrDefault(t => t.Name != h.Vm.SelectedTheme?.Name);
        if (theme != null)
        {
            h.Vm.SelectedTheme = theme;
            Assert.Equal(theme.Name, h.App.Theme);
        }

        h.Vm.AccentColor = Color.Parse("#112233");
        Assert.Equal("#112233", h.App.AccentColor);

        h.Vm.ResetAccentColorCommand.Execute(null);
        Assert.Null(h.App.AccentColor); // reset to theme default
    }

    // ── Updates / diagnostics ───────────────────────────────────────
    [AvaloniaFact]
    public void UpdatesAndDiagnostics_Toggles()
    {
        var h = Build();
        h.Vm.CheckForUpdates = true;
        Assert.True(h.App.CheckForUpdates);
        h.Vm.CheckForExtensionUpdates = true;
        Assert.True(h.App.CheckForExtensionUpdates);
        h.Vm.GithubToken = "tok";
        Assert.Equal("tok", h.App.GitHubToken);
        h.Vm.GithubToken = "  "; // whitespace -> null
        Assert.Null(h.App.GitHubToken);
        h.Vm.DiagnosticLoggingEnabled = true;
        Assert.True(h.App.DiagnosticLoggingEnabled);
        h.Vm.ClearLogsCommand.Execute(null); // safe (deletes log files)
    }

    // ── Word goals ──────────────────────────────────────────────────
    [AvaloniaFact]
    public void WordGoals_NoProject_NoOp()
    {
        var h = Build(loaded: false);
        h.Vm.DailyWordGoal = 500; // ActiveProjectGoals null -> no-op (no throw)
        h.Vm.ProjectWordGoal = 9000;
    }

    [AvaloniaFact]
    public void WordGoals_WithProject_Persist()
    {
        var h = Build(loaded: true);
        h.ProjSettings.WordCountGoals = new ProjectWordCountGoals();
        h.Vm.DailyWordGoal = 1234;
        Assert.Equal(1234, h.ProjSettings.WordCountGoals.DailyGoal);
        h.Vm.ProjectWordGoal = 60000;
        Assert.Equal(60000, h.ProjSettings.WordCountGoals.ProjectGoal);
        h.Vm.ProjectDeadline = "2026-12-31";
        Assert.Equal("2026-12-31", h.ProjSettings.WordCountGoals.Deadline);
        h.Vm.ProjectDeadline = "bad-date"; // regex guard -> not applied
        Assert.Equal("2026-12-31", h.ProjSettings.WordCountGoals.Deadline);
        h.Vm.ProjectAuthor = "Me";
        Assert.Equal("Me", h.ProjSettings.Author);
        Assert.Equal("Proj", h.Vm.CurrentProjectName);
    }

    // ── Scoped (global vs project) ──────────────────────────────────
    [AvaloniaFact]
    public void ScopeToggles_CopyToOverridesAndClear()
    {
        var h = Build(loaded: true);
        h.App.EditorFontFamily = "Verdana";

        h.Vm.EditorScopeProject = true; // copies effective editor settings into overrides
        Assert.Equal("Verdana", h.ProjSettings.Overrides!.EditorFontFamily);

        // Now editor writes go to the override
        h.Vm.EditorFontFamily = "Courier";
        Assert.Equal("Courier", h.ProjSettings.Overrides.EditorFontFamily);

        h.Vm.EditorScopeProject = false; // clears editor overrides
        Assert.Null(h.ProjSettings.Overrides.EditorFontFamily);

        h.Vm.AppearanceScopeProject = true;
        Assert.True(h.Vm.CanUseProjectScope);
        h.Vm.AppearanceScopeProject = false;

        h.Vm.WritingScopeProject = true;
        h.Vm.WritingScopeProject = false;
    }

    // ── Templates ───────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task CharacterTemplate_AddEditDelete()
    {
        var h = Build(loaded: true, withBook: true);
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(true);

        await h.Vm.AddCharacterTemplateCommand.ExecuteAsync(null);
        Assert.Single(h.Book!.CharacterTemplates);
        Assert.Single(h.Vm.CharacterTemplates); // LoadTemplates ran inside the command

        var item = h.Vm.CharacterTemplates[0];
        await h.Vm.EditCharacterTemplateCommand.ExecuteAsync(item);
        h.Book.ActiveCharacterTemplateId = item.Id; // delete clears active id
        h.Vm.DeleteCharacterTemplateCommand.Execute(item);
        Assert.Empty(h.Book.CharacterTemplates);
        Assert.Equal(string.Empty, h.Book.ActiveCharacterTemplateId);
    }

    [AvaloniaFact]
    public async Task Templates_NoBook_NoOp()
    {
        var h = Build(loaded: false, withBook: false);
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(true);
        await h.Vm.AddCharacterTemplateCommand.ExecuteAsync(null);
        await h.Vm.AddLocationTemplateCommand.ExecuteAsync(null);
        await h.Vm.AddItemTemplateCommand.ExecuteAsync(null);
        await h.Vm.AddLoreTemplateCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.CharacterTemplates); // no book -> nothing added, no throw
    }

    [AvaloniaFact]
    public async Task Location_Item_Lore_Templates_AddEditDelete()
    {
        var h = Build(loaded: true, withBook: true);
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(true);

        await h.Vm.AddLocationTemplateCommand.ExecuteAsync(null);
        await h.Vm.AddItemTemplateCommand.ExecuteAsync(null);
        await h.Vm.AddLoreTemplateCommand.ExecuteAsync(null);
        Assert.Single(h.Book!.LocationTemplates);
        Assert.Single(h.Book.ItemTemplates);
        Assert.Single(h.Book.LoreTemplates);

        await h.Vm.EditLocationTemplateCommand.ExecuteAsync(h.Vm.LocationTemplates[0]);
        await h.Vm.EditItemTemplateCommand.ExecuteAsync(h.Vm.ItemTemplates[0]);
        await h.Vm.EditLoreTemplateCommand.ExecuteAsync(h.Vm.LoreTemplates[0]);

        h.Book.ActiveLocationTemplateId = h.Vm.LocationTemplates[0].Id;
        h.Book.ActiveItemTemplateId = h.Vm.ItemTemplates[0].Id;
        h.Book.ActiveLoreTemplateId = h.Vm.LoreTemplates[0].Id;
        h.Vm.DeleteLocationTemplateCommand.Execute(h.Vm.LocationTemplates[0]);
        h.Vm.DeleteItemTemplateCommand.Execute(h.Vm.ItemTemplates[0]);
        h.Vm.DeleteLoreTemplateCommand.Execute(h.Vm.LoreTemplates[0]);
        Assert.Empty(h.Book.LocationTemplates);
        Assert.Equal(string.Empty, h.Book.ActiveLocationTemplateId);
    }

    [AvaloniaFact]
    public async Task EditTemplate_EditorCancelled_AndNullItem()
    {
        var h = Build(loaded: true, withBook: true);
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(true);
        await h.Vm.AddCharacterTemplateCommand.ExecuteAsync(null);
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(false); // cancel
        await h.Vm.EditCharacterTemplateCommand.ExecuteAsync(h.Vm.CharacterTemplates[0]);
        Assert.Single(h.Book!.CharacterTemplates);
        await h.Vm.EditCharacterTemplateCommand.ExecuteAsync(null); // null item -> no-op
    }

    [AvaloniaFact]
    public async Task CustomEntityTemplates_GroupedAndCrud()
    {
        var h = Build(loaded: true, withBook: true);
        h.Meta!.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayNamePlural = "Factions" });
        h.Vm.ShowTemplateEditor = _ => Task.FromResult(true);

        await h.Vm.AddCustomEntityTemplateCommand.ExecuteAsync("faction");
        Assert.Single(h.Book!.CustomEntityTemplates);
        Assert.Contains(h.Vm.CustomEntityTemplateGroups, g => g.TypeKey == "faction" && g.Templates.Count == 1);

        var req = new CustomEntityTemplateEditRequest("faction", h.Book.CustomEntityTemplates[0].Id);
        await h.Vm.EditCustomEntityTemplateCommand.ExecuteAsync(req);
        h.Vm.DeleteCustomEntityTemplateCommand.Execute(req);
        Assert.Empty(h.Book.CustomEntityTemplates);

        await h.Vm.AddCustomEntityTemplateCommand.ExecuteAsync(""); // empty key -> no-op
        await h.Vm.AddCustomEntityTemplateCommand.ExecuteAsync("ghost"); // unknown type -> no-op
    }

    [AvaloniaFact]
    public void Close_FiresEvent()
    {
        var h = Build();
        bool closed = false;
        h.Vm.CloseRequested += () => closed = true;
        h.Vm.CloseCommand.Execute(null);
        Assert.True(closed);
    }

    // ── Sub view models ─────────────────────────────────────────────
    [AvaloniaFact]
    public void SubViewModels_EqualityAndToString()
    {
        var ui = new UiLanguageItem("en", "English");
        Assert.Equal("English", ui.ToString());
        Assert.True(ui.Equals(new UiLanguageItem("en", "X")));
        Assert.Equal(ui.GetHashCode(), new UiLanguageItem("en", "Y").GetHashCode());

        var t = new TemplateListItem("id", "Name");
        Assert.Equal("Name", t.ToString());
        Assert.True(t.Equals(new TemplateListItem("id", "Z")));
        _ = t.GetHashCode();

        var pf = new PageFormatItem("Custom", "Custom");
        Assert.Equal("Custom", pf.ToString());
        Assert.True(pf.Equals(new PageFormatItem("Custom", "X")));
        _ = pf.GetHashCode();

        var cat = new SettingsCategoryItem("editor", "settings.editor");
        Assert.False(string.IsNullOrEmpty(cat.DisplayName));
        Assert.False(string.IsNullOrEmpty(cat.ToString()));
        Assert.True(cat.Equals(new SettingsCategoryItem("editor", "x")));
        _ = cat.GetHashCode();

        var grp = new CustomEntityTemplateGroup("k", "Disp", [new TemplateListItem("i", "n")]);
        Assert.Equal("k", grp.TypeKey);
        Assert.Single(grp.Templates);

        var req = new CustomEntityTemplateEditRequest("type", "tid");
        Assert.Equal("type", req.EntityTypeKey);
        Assert.Equal("tid", req.TemplateId);

        var ext = new ExtensionSettingsPageVM("cat", new Avalonia.Controls.Border());
        Assert.Equal("cat", ext.Category);
        Assert.NotNull(ext.View);
    }
}
