using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.Utilities;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IProjectService _projectService;
    private AppSettings Settings => _settingsService.Settings;
    private ProjectWordCountGoals? ActiveProjectGoals =>
        _projectService.CurrentProject != null
            ? _projectService.ProjectSettings.WordCountGoals
            : null;

    // ── Search & Navigation ─────────────────────────────────────────

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SettingsCategoryItem? _selectedCategory;

    public List<SettingsCategoryItem> Categories { get; } =
    [
        new("appearance", "settings.appearance"),
        new("editor", "settings.editor"),
        new("writingGoals", "settings.writingGoals"),
        new("writingAssistance", "settings.writingAssistance"),
        new("templates", "settings.templates"),
        new("hotkeys", "settings.hotkeys"),
        new("updatesIntegrations", "settings.updatesIntegrations"),
        new("diagnostics", "settings.diagnostics"),
    ];

    [ObservableProperty] private bool _isAppearanceSectionVisible = true;
    [ObservableProperty] private bool _isEditorSectionVisible = true;
    [ObservableProperty] private bool _isWritingGoalsSectionVisible = true;
    [ObservableProperty] private bool _isWritingAssistanceSectionVisible = true;
    [ObservableProperty] private bool _isTemplatesSectionVisible = true;
    [ObservableProperty] private bool _isHotkeysSectionVisible = true;
    [ObservableProperty] private bool _isUpdatesIntegrationsSectionVisible = true;
    [ObservableProperty] private bool _isDiagnosticsSectionVisible = true;

    public HotkeySettingsViewModel HotkeySettings { get; } = new(App.HotkeyService);

    public event Action<string>? ScrollToCategoryRequested;

    /// <summary>Extension-contributed settings sections.</summary>
    public ObservableCollection<ExtensionSettingsPageVM> ExtensionSettingsPages { get; } = [];

    /// <summary>Populates the extension settings pages from the extension manager.</summary>
    public void LoadExtensionSettingsPages(IReadOnlyList<SettingsPage> pages)
    {
        ExtensionSettingsPages.Clear();
        foreach (var page in pages)
        {
            Categories.Add(new SettingsCategoryItem($"ext_{page.Category}", page.Category));
            ExtensionSettingsPages.Add(new ExtensionSettingsPageVM(page.Category, page.CreateView()));
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplySearchFilter();
    }

    partial void OnSelectedCategoryChanged(SettingsCategoryItem? value)
    {
        if (value == null) return;

        // Clear search when user picks a category
        if (!string.IsNullOrEmpty(SearchQuery))
        {
            _searchQuery = string.Empty;
            OnPropertyChanged(nameof(SearchQuery));
            ApplySearchFilter();
        }

        ScrollToCategoryRequested?.Invoke(value.Key);
    }

    private void ApplySearchFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            IsAppearanceSectionVisible = true;
            IsEditorSectionVisible = true;
            IsWritingGoalsSectionVisible = true;
            IsWritingAssistanceSectionVisible = true;
            IsTemplatesSectionVisible = true;
            IsHotkeysSectionVisible = true;
            IsUpdatesIntegrationsSectionVisible = true;
            IsDiagnosticsSectionVisible = true;
            return;
        }

        var q = SearchQuery.Trim();
        IsAppearanceSectionVisible = MatchesSection(q, "language", "interface", "sprache", "theme", "farbschema", "accent", "akzentfarbe", "color", "appearance", "darstellung",
            Loc.T("settings.appearance"), Loc.T("settings.language"), Loc.T("settings.uiLanguage"), Loc.T("settings.uiLanguageDesc"),
            Loc.T("settings.theme"), Loc.T("settings.themeDescription"),
            Loc.T("settings.accentColor"), Loc.T("settings.accentColorDesc"));
        IsEditorSectionVisible = MatchesSection(q, "editor", "font", "book", "page", "paragraph", "spacing", "width",
            Loc.T("settings.editor"), Loc.T("settings.fontFamily"), Loc.T("settings.fontSize"),
            Loc.T("settings.bookSpacing"), Loc.T("settings.bookWidth"), Loc.T("settings.bookWidthPageFormat"));
        IsWritingGoalsSectionVisible = MatchesSection(q, "goal", "daily", "project", "deadline", "word", "writing",
            Loc.T("settings.writingGoals"), Loc.T("settings.dailyWordGoal"), Loc.T("settings.projectWordGoal"),
            Loc.T("settings.projectDeadline"));
        IsWritingAssistanceSectionVisible = MatchesSection(q, "auto", "replacement", "quote", "smart", "dialogue", "correction", "dialog", "grammar", "spelling", "writing assistance", "schreibhilfe",
            Loc.T("settings.writingAssistance"), Loc.T("settings.autoReplacement"), Loc.T("settings.quoteStyle"), Loc.T("settings.dialogueCorrection"),
            Loc.T("settings.grammarCheck"));
        IsTemplatesSectionVisible = MatchesSection(q, "template", "character", "location", "item", "lore",
            Loc.T("settings.templates"), Loc.T("settings.characterTemplates"), Loc.T("settings.locationTemplates"),
            Loc.T("settings.itemTemplates"), Loc.T("settings.loreTemplates"));
        IsHotkeysSectionVisible = MatchesSection(q, "hotkey", "keyboard", "shortcut", "key", "binding",
            Loc.T("settings.hotkeys"));
        IsUpdatesIntegrationsSectionVisible = MatchesSection(q, "general", "update", "aktualisierung",
            "extension", "github", "token", "pat", "integration",
            Loc.T("settings.updatesIntegrations"), Loc.T("update.checkForUpdates"), Loc.T("update.checkForUpdatesDesc"),
            Loc.T("settings.checkForExtensionUpdates"), Loc.T("settings.githubToken"));
        IsDiagnosticsSectionVisible = MatchesSection(q, "log", "logging", "diagnostic", "diagnostics", "support", "protokoll", "diagnose",
            Loc.T("settings.diagnostics"), Loc.T("settings.diagnosticLogging"), Loc.T("settings.diagnosticLoggingDesc"));
    }

    private static bool MatchesSection(string query, params string[] terms)
    {
        return terms.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    [ObservableProperty]
    private string _editorFontFamily;

    [ObservableProperty]
    private double _editorFontSize;

    [ObservableProperty]
    private bool _enableBookParagraphSpacing;

    [ObservableProperty]
    private bool _enableBookWidth;

    [ObservableProperty]
    private PageFormatItem _selectedPageFormat;

    [ObservableProperty]
    private double? _bookTextBlockWidth;

    [ObservableProperty]
    private string _bookFontFamily;

    [ObservableProperty]
    private double _bookFontSize;

    [ObservableProperty]
    private string _bookWidthCharsPerLine = string.Empty;

    [ObservableProperty]
    private string _selectedLanguage;

    [ObservableProperty]
    private UiLanguageItem _selectedUiLanguage;

    public List<ThemeInfo> AvailableThemes => App.ThemeService.AvailableThemes.ToList();

    [ObservableProperty]
    private ThemeInfo _selectedTheme;

    [ObservableProperty]
    private Color _accentColor;

    [ObservableProperty]
    private string _autoReplacementPreview = string.Empty;

    [ObservableProperty]
    private bool _dialogueCorrectionEnabled;

    [ObservableProperty]
    private bool _grammarCheckEnabled;

    [ObservableProperty]
    private string _grammarCheckApiUrl = string.Empty;

    [ObservableProperty]
    private bool _typewriterScrollEnabled;

    [ObservableProperty]
    private bool _typewriterAnchorTop;

    [ObservableProperty]
    private bool _typewriterAnchorMiddle;

    [ObservableProperty]
    private bool _typewriterAnchorBottom;

    [ObservableProperty]
    private bool _pageViewEnabled;

    [ObservableProperty]
    private int _dailyWordGoal;

    [ObservableProperty]
    private int _projectWordGoal;

    [ObservableProperty]
    private string _projectDeadline = string.Empty;

    [ObservableProperty]
    private string _projectAuthor = string.Empty;

    [ObservableProperty]
    private bool _watchFilesystem = true;

    public List<string> AvailableLanguages { get; } = AutoReplacementDefaults.AvailableLanguages;
    public List<UiLanguageItem> AvailableUiLanguages { get; }
    public string CurrentProjectName => _projectService.CurrentProject?.Name ?? string.Empty;

    public List<string> AvailableFonts { get; } = FontManager.Current.SystemFonts
        .Select(f => f.Name)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public List<PageFormatItem> AvailablePageFormats { get; } = BookWidthCalculator.PageFormats
        .Select(f => new PageFormatItem(f, BookWidthCalculator.GetPageFormatDisplayName(f)))
        .ToList();

    public bool IsCustomPageFormat => SelectedPageFormat?.Code == "Custom";

    // Template management
    [ObservableProperty] private ObservableCollection<TemplateListItem> _characterTemplates = [];
    [ObservableProperty] private ObservableCollection<TemplateListItem> _locationTemplates = [];
    [ObservableProperty] private ObservableCollection<TemplateListItem> _itemTemplates = [];
    [ObservableProperty] private ObservableCollection<TemplateListItem> _loreTemplates = [];
    [ObservableProperty] private ObservableCollection<CustomEntityTemplateGroup> _customEntityTemplateGroups = [];

    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _checkForExtensionUpdates;
    [ObservableProperty] private string _githubToken = string.Empty;
    [ObservableProperty] private bool _diagnosticLoggingEnabled;

    public Func<TemplateEditorViewModel, Task<bool>>? ShowTemplateEditor { get; set; }

    public event Action? CloseRequested;
    public event Action? SettingsChanged;

    // ── Setting scope (Global vs This project) ──────────────────────
    // One toggle per Settings category that contains overridable settings.
    // Hotkeys, updates, diagnostics, templates and word goals are not scoped.

    [ObservableProperty] private bool _appearanceScopeProject;
    [ObservableProperty] private bool _editorScopeProject;
    [ObservableProperty] private bool _writingScopeProject;

    /// <summary>True when a project is open, so per-project scope is available.</summary>
    public bool CanUseProjectScope => _projectService.IsProjectLoaded;

    /// <summary>The active project's overrides, or null when no project is open.</summary>
    private SettingsOverrides? ActiveOverrides =>
        _projectService.IsProjectLoaded ? _projectService.ProjectSettings.Overrides : null;

    /// <summary>Suppresses scoped persistence while reloading VM values from effective settings.</summary>
    private bool _suppressScopedWrite;

    /// <summary>
    /// Writes a setting to the active project's override when the section is
    /// project-scoped (and a project is open), otherwise to global. Persists to
    /// the matching file and raises <see cref="SettingsChanged"/>.
    /// </summary>
    private void WriteScoped(bool projectScope, Action<AppSettings> applyGlobal, Action<SettingsOverrides> applyOverride)
    {
        if (_suppressScopedWrite) return;

        var ov = ActiveOverrides;
        if (projectScope && ov != null)
        {
            applyOverride(ov);
            SaveProjectSettingsAndNotify();
        }
        else
        {
            applyGlobal(Settings);
            SaveAndNotify();
        }
    }

    private void WriteAppearance(Action<AppSettings> g, Action<SettingsOverrides> o) => WriteScoped(AppearanceScopeProject, g, o);
    private void WriteEditor(Action<AppSettings> g, Action<SettingsOverrides> o) => WriteScoped(EditorScopeProject, g, o);
    private void WriteWriting(Action<AppSettings> g, Action<SettingsOverrides> o) => WriteScoped(WritingScopeProject, g, o);

    partial void OnAppearanceScopeProjectChanged(bool value)
    {
        var ov = ActiveOverrides;
        if (ov == null) return;
        var eff = _settingsService.Effective;
        if (value)
        {
            ov.Language = eff.Language;
            ov.Theme = eff.Theme;
            ov.AccentColor = eff.AccentColor;
        }
        else
        {
            ov.ClearAppearance();
        }
        SaveProjectSettingsAndNotify();
        ReloadAppearanceFromEffective();
    }

    partial void OnEditorScopeProjectChanged(bool value)
    {
        var ov = ActiveOverrides;
        if (ov == null) return;
        var eff = _settingsService.Effective;
        if (value)
        {
            ov.EditorFontFamily = eff.EditorFontFamily;
            ov.EditorFontSize = eff.EditorFontSize;
            ov.TypewriterScrollEnabled = eff.TypewriterScrollEnabled;
            ov.TypewriterScrollAnchor = eff.TypewriterScrollAnchor;
            ov.PageViewEnabled = eff.PageViewEnabled;
            ov.EnableBookParagraphSpacing = eff.EnableBookParagraphSpacing;
            ov.EnableBookWidth = eff.EnableBookWidth;
            ov.BookPageFormat = eff.BookPageFormat;
            ov.BookTextBlockWidth = eff.BookTextBlockWidth;
            ov.BookFontFamily = eff.BookFontFamily;
            ov.BookFontSize = eff.BookFontSize;
        }
        else
        {
            ov.ClearEditor();
        }
        SaveProjectSettingsAndNotify();
        ReloadEditorFromEffective();
    }

    partial void OnWritingScopeProjectChanged(bool value)
    {
        var ov = ActiveOverrides;
        if (ov == null) return;
        var eff = _settingsService.Effective;
        if (value)
        {
            ov.AutoReplacementLanguage = eff.AutoReplacementLanguage;
            ov.AutoReplacements = eff.AutoReplacements;
            ov.DialogueCorrectionEnabled = eff.DialogueCorrectionEnabled;
            ov.GrammarCheckEnabled = eff.GrammarCheckEnabled;
            ov.GrammarCheckApiUrl = eff.GrammarCheckApiUrl;
        }
        else
        {
            ov.ClearWriting();
        }
        SaveProjectSettingsAndNotify();
        ReloadWritingFromEffective();
    }

    private void ReloadAppearanceFromEffective()
    {
        var eff = _settingsService.Effective;
        _suppressScopedWrite = true;

        SelectedUiLanguage = AvailableUiLanguages.FirstOrDefault(i => string.Equals(i.Code, eff.Language, StringComparison.Ordinal))
            ?? SelectedUiLanguage;
        Loc.Instance.CurrentLanguage = eff.Language;

        var theme = string.IsNullOrEmpty(eff.Theme) ? "Default" : eff.Theme;
        try { App.ThemeService.ApplyTheme(theme == "system" ? "Default" : theme); } catch { /* invalid theme name */ }
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == App.ThemeService.ActiveThemeName) ?? SelectedTheme;

        App.ThemeService.ApplyAccentColor(eff.AccentColor);
        var accentHex = eff.AccentColor ?? App.ThemeService.GetActiveThemeDefaultAccentColor() ?? "#007ACC";
        AccentColor = Color.TryParse(accentHex, out var c) ? c : Color.Parse("#007ACC");

        _suppressScopedWrite = false;
    }

    private void ReloadEditorFromEffective()
    {
        var eff = _settingsService.Effective;
        _suppressScopedWrite = true;

        EditorFontFamily = eff.EditorFontFamily;
        EditorFontSize = eff.EditorFontSize;
        TypewriterScrollEnabled = eff.TypewriterScrollEnabled;
        var anchor = eff.TypewriterScrollAnchor ?? "middle";
        TypewriterAnchorTop = anchor == "top";
        TypewriterAnchorMiddle = anchor == "middle";
        TypewriterAnchorBottom = anchor == "bottom";
        PageViewEnabled = eff.PageViewEnabled;
        EnableBookParagraphSpacing = eff.EnableBookParagraphSpacing;
        EnableBookWidth = eff.EnableBookWidth;
        SelectedPageFormat = AvailablePageFormats.FirstOrDefault(f => string.Equals(f.Code, eff.BookPageFormat, StringComparison.Ordinal))
            ?? SelectedPageFormat;
        BookTextBlockWidth = eff.BookTextBlockWidth;
        BookFontFamily = eff.BookFontFamily;
        BookFontSize = eff.BookFontSize;

        _suppressScopedWrite = false;
        UpdateBookWidthPreview();
    }

    private void ReloadWritingFromEffective()
    {
        var eff = _settingsService.Effective;
        _suppressScopedWrite = true;

        SelectedLanguage = eff.AutoReplacementLanguage;
        DialogueCorrectionEnabled = eff.DialogueCorrectionEnabled;
        GrammarCheckEnabled = eff.GrammarCheckEnabled;
        GrammarCheckApiUrl = eff.GrammarCheckApiUrl ?? string.Empty;

        _suppressScopedWrite = false;
        UpdatePreview();
    }

    public SettingsViewModel(ISettingsService settingsService, IProjectService projectService)
    {
        _settingsService = settingsService;
        _projectService = projectService;

        var eff = _settingsService.Effective;

        // Initial scope toggles reflect whether the active project already
        // overrides each section.
        var ov = ActiveOverrides;
        _appearanceScopeProject = ov?.HasAppearanceOverride == true;
        _editorScopeProject = ov?.HasEditorOverride == true;
        _writingScopeProject = ov?.HasWritingOverride == true;

        _editorFontFamily = eff.EditorFontFamily;
        _editorFontSize = eff.EditorFontSize;
        _enableBookParagraphSpacing = eff.EnableBookParagraphSpacing;
        _enableBookWidth = eff.EnableBookWidth;
        _bookTextBlockWidth = eff.BookTextBlockWidth;
        _bookFontFamily = eff.BookFontFamily;
        _bookFontSize = eff.BookFontSize;
        _selectedLanguage = eff.AutoReplacementLanguage;
        _dialogueCorrectionEnabled = eff.DialogueCorrectionEnabled;
        _grammarCheckEnabled = eff.GrammarCheckEnabled;
        _grammarCheckApiUrl = eff.GrammarCheckApiUrl ?? string.Empty;
        _typewriterScrollEnabled = eff.TypewriterScrollEnabled;
        var twAnchor = eff.TypewriterScrollAnchor ?? "middle";
        _typewriterAnchorTop = twAnchor == "top";
        _typewriterAnchorMiddle = twAnchor == "middle";
        _typewriterAnchorBottom = twAnchor == "bottom";
        _pageViewEnabled = eff.PageViewEnabled;
        _dailyWordGoal = ActiveProjectGoals?.DailyGoal ?? 1000;
        _projectWordGoal = ActiveProjectGoals?.ProjectGoal ?? 50000;
        _projectDeadline = ActiveProjectGoals?.Deadline ?? string.Empty;
        _projectAuthor = _projectService.ProjectSettings?.Author ?? string.Empty;
        _watchFilesystem = _projectService.ProjectSettings?.WatchFilesystem ?? true;

        AvailableUiLanguages = Loc.Instance.GetAvailableLanguages()
            .Select(code => new UiLanguageItem(code, Loc.Instance.GetLanguageDisplayName(code)))
            .ToList();

        _selectedUiLanguage = AvailableUiLanguages.FirstOrDefault(item => string.Equals(item.Code, eff.Language, StringComparison.Ordinal))
            ?? AvailableUiLanguages.First();

        _selectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == App.ThemeService.ActiveThemeName)
            ?? AvailableThemes[0];

        // Resolve accent color: effective override → theme default → fallback blue
        var accentHex = eff.AccentColor
            ?? App.ThemeService.GetActiveThemeDefaultAccentColor()
            ?? "#007ACC";
        _accentColor = Color.TryParse(accentHex, out var parsed) ? parsed : Color.Parse("#007ACC");

        _selectedPageFormat = AvailablePageFormats.FirstOrDefault(f => string.Equals(f.Code, eff.BookPageFormat, StringComparison.Ordinal))
            ?? AvailablePageFormats.First();

        UpdatePreview();
        UpdateBookWidthPreview();
        LoadTemplates();

        _checkForUpdates = Settings.CheckForUpdates;
        _checkForExtensionUpdates = Settings.CheckForExtensionUpdates;
        _githubToken = Settings.GitHubToken ?? string.Empty;
        _diagnosticLoggingEnabled = Settings.DiagnosticLoggingEnabled;

        _selectedCategory = Categories[0];
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        Settings.CheckForUpdates = value;
        SaveAndNotify();
    }

    partial void OnCheckForExtensionUpdatesChanged(bool value)
    {
        Settings.CheckForExtensionUpdates = value;
        SaveAndNotify();
    }

    partial void OnGithubTokenChanged(string value)
    {
        Settings.GitHubToken = string.IsNullOrWhiteSpace(value) ? null : value;
        SaveAndNotify();
    }

    partial void OnDiagnosticLoggingEnabledChanged(bool value)
    {
        Settings.DiagnosticLoggingEnabled = value;
        Log.EnableFileLogging(value);
        Log.Info($"Diagnostic logging {(value ? "enabled" : "disabled")} by user.");
        SaveAndNotify();
    }

    partial void OnEditorFontFamilyChanged(string value)
    {
        WriteEditor(s => s.EditorFontFamily = value, o => o.EditorFontFamily = value);
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, 8, 36);
        WriteEditor(s => s.EditorFontSize = clamped, o => o.EditorFontSize = clamped);
    }

    partial void OnEnableBookParagraphSpacingChanged(bool value)
    {
        WriteEditor(s => s.EnableBookParagraphSpacing = value, o => o.EnableBookParagraphSpacing = value);
    }

    partial void OnEnableBookWidthChanged(bool value)
    {
        WriteEditor(s => s.EnableBookWidth = value, o => o.EnableBookWidth = value);
    }

    partial void OnSelectedPageFormatChanged(PageFormatItem value)
    {
        var code = value?.Code ?? "USTrade6x9";
        OnPropertyChanged(nameof(IsCustomPageFormat));
        WriteEditor(s => s.BookPageFormat = code, o => o.BookPageFormat = code);
        UpdateBookWidthPreview();
    }

    partial void OnBookTextBlockWidthChanged(double? value)
    {
        WriteEditor(s => s.BookTextBlockWidth = value, o => o.BookTextBlockWidth = value);
        UpdateBookWidthPreview();
    }

    partial void OnBookFontFamilyChanged(string value)
    {
        WriteEditor(s => s.BookFontFamily = value, o => o.BookFontFamily = value);
        UpdateBookWidthPreview();
    }

    partial void OnBookFontSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, 6, 24);
        WriteEditor(s => s.BookFontSize = clamped, o => o.BookFontSize = clamped);
        UpdateBookWidthPreview();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        var preset = AutoReplacementDefaults.GetPreset(value);
        UpdatePreview();
        WriteWriting(
            s => { s.AutoReplacementLanguage = value; s.AutoReplacements = preset; },
            o => { o.AutoReplacementLanguage = value; o.AutoReplacements = preset; });
    }

    partial void OnDialogueCorrectionEnabledChanged(bool value)
    {
        WriteWriting(s => s.DialogueCorrectionEnabled = value, o => o.DialogueCorrectionEnabled = value);
    }

    partial void OnGrammarCheckEnabledChanged(bool value)
    {
        WriteWriting(s => s.GrammarCheckEnabled = value, o => o.GrammarCheckEnabled = value);
    }

    partial void OnGrammarCheckApiUrlChanged(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        WriteWriting(s => s.GrammarCheckApiUrl = normalized, o => o.GrammarCheckApiUrl = normalized);
    }

    partial void OnTypewriterScrollEnabledChanged(bool value)
    {
        WriteEditor(s => s.TypewriterScrollEnabled = value, o => o.TypewriterScrollEnabled = value);
    }

    partial void OnTypewriterAnchorTopChanged(bool value)
    {
        if (value) ApplyTypewriterAnchor("top");
    }

    partial void OnTypewriterAnchorMiddleChanged(bool value)
    {
        if (value) ApplyTypewriterAnchor("middle");
    }

    partial void OnTypewriterAnchorBottomChanged(bool value)
    {
        if (value) ApplyTypewriterAnchor("bottom");
    }

    private void ApplyTypewriterAnchor(string anchor)
    {
        if (_settingsService.Effective.TypewriterScrollAnchor == anchor) return;
        WriteEditor(s => s.TypewriterScrollAnchor = anchor, o => o.TypewriterScrollAnchor = anchor);
    }

    partial void OnPageViewEnabledChanged(bool value)
    {
        WriteEditor(s => s.PageViewEnabled = value, o => o.PageViewEnabled = value);
    }

    partial void OnSelectedUiLanguageChanged(UiLanguageItem value)
    {
        Loc.Instance.CurrentLanguage = value.Code;
        WriteAppearance(s => s.Language = value.Code, o => o.Language = value.Code);
    }

    partial void OnSelectedThemeChanged(ThemeInfo value)
    {
        App.ThemeService.ApplyTheme(value.Name);

        // When switching themes, reset accent to the theme's default
        var themeAccent = value.Source?.AccentColor;
        var fallback = themeAccent ?? "#007ACC";
        AccentColor = Color.TryParse(fallback, out var c) ? c : Color.Parse("#007ACC");
        App.ThemeService.ApplyAccentColor(null);

        WriteAppearance(
            s => { s.Theme = value.Name; s.AccentColor = null; },
            o => { o.Theme = value.Name; o.AccentColor = null; });
    }

    partial void OnAccentColorChanged(Color value)
    {
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        App.ThemeService.ApplyAccentColor(hex);
        WriteAppearance(s => s.AccentColor = hex, o => o.AccentColor = hex);
    }

    [RelayCommand]
    private void ResetAccentColor()
    {
        var themeDefault = App.ThemeService.GetActiveThemeDefaultAccentColor() ?? "#007ACC";
        AccentColor = Color.TryParse(themeDefault, out var c) ? c : Color.Parse("#007ACC");
        App.ThemeService.ApplyAccentColor(null);
        WriteAppearance(s => s.AccentColor = null, o => o.AccentColor = null);
    }

    partial void OnDailyWordGoalChanged(int value)
    {
        var goals = ActiveProjectGoals;
        if (goals == null) return;

        goals.DailyGoal = Math.Max(0, value);
        SaveProjectSettingsAndNotify();
    }

    partial void OnProjectWordGoalChanged(int value)
    {
        var goals = ActiveProjectGoals;
        if (goals == null) return;

        goals.ProjectGoal = Math.Max(0, value);
        SaveProjectSettingsAndNotify();
    }

    partial void OnProjectDeadlineChanged(string value)
    {
        var goals = ActiveProjectGoals;
        if (goals == null) return;

        if (!string.IsNullOrWhiteSpace(value) && !System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d{4}-\\d{2}-\\d{2}$"))
            return;

        goals.Deadline = string.IsNullOrWhiteSpace(value) ? null : value;
        SaveProjectSettingsAndNotify();
    }

    partial void OnProjectAuthorChanged(string value)
    {
        if (_projectService.ProjectSettings == null) return;
        _projectService.ProjectSettings.Author = value ?? string.Empty;
        SaveProjectSettingsAndNotify();
    }

    partial void OnWatchFilesystemChanged(bool value)
    {
        if (_projectService.ProjectSettings == null) return;
        _projectService.ProjectSettings.WatchFilesystem = value;
        SaveProjectSettingsAndNotify();
    }

    [RelayCommand]
    private async Task AddCharacterTemplateAsync()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        var template = new CharacterTemplate { Name = Loc.T("template.newTemplate") };
        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildCharacterTemplate(template.Id);
            book.CharacterTemplates.Add(result);
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private async Task EditCharacterTemplateAsync(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        var template = book.CharacterTemplates.FirstOrDefault(t => t.Id == item.Id);
        if (template == null) return;

        var vm = new TemplateEditorViewModel("character");
        vm.LoadCharacterTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildCharacterTemplate(template.Id);
            var index = book.CharacterTemplates.FindIndex(t => t.Id == template.Id);
            if (index >= 0) book.CharacterTemplates[index] = result;
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private void DeleteCharacterTemplate(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        book.CharacterTemplates.RemoveAll(t => t.Id == item.Id);
        if (book.ActiveCharacterTemplateId == item.Id)
            book.ActiveCharacterTemplateId = string.Empty;
        SaveProject();
        LoadTemplates();
    }

    [RelayCommand]
    private async Task AddLocationTemplateAsync()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        var template = new LocationTemplate { Name = Loc.T("template.newTemplate") };
        var vm = new TemplateEditorViewModel("location");
        vm.LoadLocationTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildLocationTemplate(template.Id);
            book.LocationTemplates.Add(result);
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private async Task EditLocationTemplateAsync(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        var template = book.LocationTemplates.FirstOrDefault(t => t.Id == item.Id);
        if (template == null) return;

        var vm = new TemplateEditorViewModel("location");
        vm.LoadLocationTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildLocationTemplate(template.Id);
            var index = book.LocationTemplates.FindIndex(t => t.Id == template.Id);
            if (index >= 0) book.LocationTemplates[index] = result;
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private void DeleteLocationTemplate(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        book.LocationTemplates.RemoveAll(t => t.Id == item.Id);
        if (book.ActiveLocationTemplateId == item.Id)
            book.ActiveLocationTemplateId = string.Empty;
        SaveProject();
        LoadTemplates();
    }

    [RelayCommand]
    private async Task AddItemTemplateAsync()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        var template = new ItemTemplate { Name = Loc.T("template.newTemplate") };
        var vm = new TemplateEditorViewModel("item");
        vm.LoadItemTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildItemTemplate(template.Id);
            book.ItemTemplates.Add(result);
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private async Task EditItemTemplateAsync(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        var template = book.ItemTemplates.FirstOrDefault(t => t.Id == item.Id);
        if (template == null) return;

        var vm = new TemplateEditorViewModel("item");
        vm.LoadItemTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildItemTemplate(template.Id);
            var index = book.ItemTemplates.FindIndex(t => t.Id == template.Id);
            if (index >= 0) book.ItemTemplates[index] = result;
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private void DeleteItemTemplate(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        book.ItemTemplates.RemoveAll(t => t.Id == item.Id);
        if (book.ActiveItemTemplateId == item.Id)
            book.ActiveItemTemplateId = string.Empty;
        SaveProject();
        LoadTemplates();
    }

    [RelayCommand]
    private async Task AddLoreTemplateAsync()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        var template = new LoreTemplate { Name = Loc.T("template.newTemplate") };
        var vm = new TemplateEditorViewModel("lore");
        vm.LoadLoreTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildLoreTemplate(template.Id);
            book.LoreTemplates.Add(result);
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private async Task EditLoreTemplateAsync(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        var template = book.LoreTemplates.FirstOrDefault(t => t.Id == item.Id);
        if (template == null) return;

        var vm = new TemplateEditorViewModel("lore");
        vm.LoadLoreTemplate(template);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildLoreTemplate(template.Id);
            var index = book.LoreTemplates.FindIndex(t => t.Id == template.Id);
            if (index >= 0) book.LoreTemplates[index] = result;
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private void DeleteLoreTemplate(TemplateListItem? item)
    {
        var book = _projectService.ActiveBook;
        if (book == null || item == null) return;

        book.LoreTemplates.RemoveAll(t => t.Id == item.Id);
        if (book.ActiveLoreTemplateId == item.Id)
            book.ActiveLoreTemplateId = string.Empty;
        SaveProject();
        LoadTemplates();
    }

    [RelayCommand]
    private async Task AddCustomEntityTemplateAsync(string? entityTypeKey)
    {
        var book = _projectService.ActiveBook;
        if (book == null || string.IsNullOrEmpty(entityTypeKey)) return;

        var typeDef = _projectService.CurrentProject?.CustomEntityTypes
            .FirstOrDefault(t => string.Equals(t.TypeKey, entityTypeKey, StringComparison.Ordinal));
        if (typeDef == null) return;

        var knownFieldKeys = typeDef.DefaultFields.Select(f => f.Key).ToArray();
        var template = new CustomEntityTemplate { Name = Loc.T("template.newTemplate"), EntityTypeKey = entityTypeKey };
        var vm = new TemplateEditorViewModel("custom");
        vm.LoadCustomEntityTemplate(template, knownFieldKeys);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildCustomEntityTemplate(template.Id);
            book.CustomEntityTemplates.Add(result);
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private async Task EditCustomEntityTemplateAsync(CustomEntityTemplateEditRequest? request)
    {
        var book = _projectService.ActiveBook;
        if (book == null || request == null) return;

        var template = book.CustomEntityTemplates.FirstOrDefault(t => t.Id == request.TemplateId);
        if (template == null) return;

        var typeDef = _projectService.CurrentProject?.CustomEntityTypes
            .FirstOrDefault(t => string.Equals(t.TypeKey, template.EntityTypeKey, StringComparison.Ordinal));
        var knownFieldKeys = typeDef?.DefaultFields.Select(f => f.Key).ToArray() ?? [];

        var vm = new TemplateEditorViewModel("custom");
        vm.LoadCustomEntityTemplate(template, knownFieldKeys);
        if (await (ShowTemplateEditor?.Invoke(vm) ?? Task.FromResult(false)))
        {
            var result = vm.BuildCustomEntityTemplate(template.Id);
            var index = book.CustomEntityTemplates.FindIndex(t => t.Id == template.Id);
            if (index >= 0) book.CustomEntityTemplates[index] = result;
            SaveProject();
            LoadTemplates();
        }
    }

    [RelayCommand]
    private void DeleteCustomEntityTemplate(CustomEntityTemplateEditRequest? request)
    {
        var book = _projectService.ActiveBook;
        if (book == null || request == null) return;

        book.CustomEntityTemplates.RemoveAll(t => t.Id == request.TemplateId);
        book.ActiveCustomEntityTemplateIds.Remove(request.EntityTypeKey);
        SaveProject();
        LoadTemplates();
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    // ── Diagnostics ─────────────────────────────────────────────────

    // Spawns the OS file manager / shell — excluded from coverage (would open a
    // real window during tests).
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.LogDirectory);
            OpenInShell(Log.LogDirectory);
        }
        catch (Exception ex) { Log.Error("OpenLogFolder failed.", ex); }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [RelayCommand]
    private void OpenCurrentLog()
    {
        try
        {
            var path = Log.CurrentLogPath;
            if (File.Exists(path))
                OpenInShell(path);
            else
                OpenInShell(Log.LogDirectory);
        }
        catch (Exception ex) { Log.Error("OpenCurrentLog failed.", ex); }
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // real-IO defensive catch
    private void ClearLogs()
    {
        try { Log.ClearLogFiles(); }
        catch (Exception ex) { Log.Error("ClearLogs failed.", ex); }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void OpenInShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void UpdatePreview()
    {
        var pairs = AutoReplacementDefaults.GetPreset(SelectedLanguage);
        var parts = new List<string>();
        foreach (var p in pairs)
        {
            if (p.Start == p.End && p.StartReplace != p.EndReplace)
                parts.Add($"{p.Start}…{p.End} → {p.StartReplace}…{p.EndReplace}");
            else
                parts.Add($"{p.Start} → {p.StartReplace}");
        }
        AutoReplacementPreview = string.Join("   |   ", parts);
    }

    private void UpdateBookWidthPreview()
    {
        var chars = BookWidthCalculator.EstimateCharsPerLine(_settingsService.Effective);
        BookWidthCharsPerLine = $"≈ {chars} characters per line";
    }

    private void SaveAndNotify()
    {
        _ = _settingsService.SaveAsync();
        SettingsChanged?.Invoke();
    }

    private void SaveProjectSettingsAndNotify()
    {
        _ = _projectService.SaveProjectSettingsAsync();
        SettingsChanged?.Invoke();
    }

    private void LoadTemplates()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        CharacterTemplates = new ObservableCollection<TemplateListItem>(
            book.CharacterTemplates.Select(t => new TemplateListItem(t.Id, t.Name)));
        LocationTemplates = new ObservableCollection<TemplateListItem>(
            book.LocationTemplates.Select(t => new TemplateListItem(t.Id, t.Name)));
        ItemTemplates = new ObservableCollection<TemplateListItem>(
            book.ItemTemplates.Select(t => new TemplateListItem(t.Id, t.Name)));
        LoreTemplates = new ObservableCollection<TemplateListItem>(
            book.LoreTemplates.Select(t => new TemplateListItem(t.Id, t.Name)));

        var metadata = _projectService.CurrentProject;
        var customTypes = metadata?.CustomEntityTypes ?? [];
        var groups = new List<CustomEntityTemplateGroup>();
        foreach (var typeDef in customTypes)
        {
            var templates = book.CustomEntityTemplates
                .Where(t => string.Equals(t.EntityTypeKey, typeDef.TypeKey, StringComparison.Ordinal))
                .Select(t => new TemplateListItem(t.Id, t.Name))
                .ToList();
            groups.Add(new CustomEntityTemplateGroup(typeDef.TypeKey, typeDef.DisplayNamePlural, templates));
        }
        CustomEntityTemplateGroups = new ObservableCollection<CustomEntityTemplateGroup>(groups);
    }

    private void SaveProject()
    {
        _ = _projectService.SaveProjectAsync();
        SettingsChanged?.Invoke();
    }
}

public sealed class UiLanguageItem(string code, string displayName)
{
    public string Code { get; } = code;
    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj) => obj is UiLanguageItem other && string.Equals(Code, other.Code, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
}

public sealed class TemplateListItem(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;

    public override string ToString() => Name;

    public override bool Equals(object? obj) => obj is TemplateListItem other && string.Equals(Id, other.Id, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}

public sealed class CustomEntityTemplateGroup(string typeKey, string displayName, List<TemplateListItem> templates)
{
    public string TypeKey { get; } = typeKey;
    public string DisplayName { get; } = displayName;
    public ObservableCollection<TemplateListItem> Templates { get; } = new(templates);
}

public sealed class CustomEntityTemplateEditRequest(string entityTypeKey, string templateId)
{
    public string EntityTypeKey { get; } = entityTypeKey;
    public string TemplateId { get; } = templateId;
}

public sealed class PageFormatItem(string code, string displayName)
{
    public string Code { get; } = code;
    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj) => obj is PageFormatItem other && string.Equals(Code, other.Code, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);
}

public sealed class SettingsCategoryItem(string key, string locKey)
{
    public string Key { get; } = key;
    public string DisplayName => Loc.T(locKey);

    public override string ToString() => DisplayName;
    public override bool Equals(object? obj) => obj is SettingsCategoryItem other && string.Equals(Key, other.Key, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}

public sealed class ExtensionSettingsPageVM(string category, Avalonia.Controls.Control view)
{
    public string Category { get; } = category;
    public Avalonia.Controls.Control View { get; } = view;
}
