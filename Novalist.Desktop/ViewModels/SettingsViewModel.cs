using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        new("language", "settings.language"),
        new("editor", "settings.editor"),
        new("writingGoals", "settings.writingGoals"),
        new("autoReplacement", "settings.autoReplacement"),
        new("templates", "settings.templates"),
        new("hotkeys", "settings.hotkeys"),
        new("general", "settings.general"),
    ];

    [ObservableProperty] private bool _isLanguageSectionVisible = true;
    [ObservableProperty] private bool _isEditorSectionVisible = true;
    [ObservableProperty] private bool _isWritingGoalsSectionVisible = true;
    [ObservableProperty] private bool _isAutoReplacementSectionVisible = true;
    [ObservableProperty] private bool _isTemplatesSectionVisible = true;
    [ObservableProperty] private bool _isHotkeysSectionVisible = true;
    [ObservableProperty] private bool _isGeneralSectionVisible = true;

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
            IsLanguageSectionVisible = true;
            IsEditorSectionVisible = true;
            IsWritingGoalsSectionVisible = true;
            IsAutoReplacementSectionVisible = true;
            IsTemplatesSectionVisible = true;
            IsHotkeysSectionVisible = true;
            IsGeneralSectionVisible = true;
            return;
        }

        var q = SearchQuery.Trim();
        IsLanguageSectionVisible = MatchesSection(q, "language", "interface", "sprache", "theme", "farbschema", "accent", "akzentfarbe", "color",
            Loc.T("settings.language"), Loc.T("settings.uiLanguage"), Loc.T("settings.uiLanguageDesc"),
            Loc.T("settings.theme"), Loc.T("settings.themeDescription"),
            Loc.T("settings.accentColor"), Loc.T("settings.accentColorDesc"));
        IsEditorSectionVisible = MatchesSection(q, "editor", "font", "book", "page", "paragraph", "spacing", "width",
            Loc.T("settings.editor"), Loc.T("settings.fontFamily"), Loc.T("settings.fontSize"),
            Loc.T("settings.bookSpacing"), Loc.T("settings.bookWidth"), Loc.T("settings.bookWidthPageFormat"));
        IsWritingGoalsSectionVisible = MatchesSection(q, "goal", "daily", "project", "deadline", "word", "writing",
            Loc.T("settings.writingGoals"), Loc.T("settings.dailyWordGoal"), Loc.T("settings.projectWordGoal"),
            Loc.T("settings.projectDeadline"));
        IsAutoReplacementSectionVisible = MatchesSection(q, "auto", "replacement", "quote", "smart", "dialogue", "correction", "dialog",
            Loc.T("settings.autoReplacement"), Loc.T("settings.quoteStyle"), Loc.T("settings.dialogueCorrection"));
        IsTemplatesSectionVisible = MatchesSection(q, "template", "character", "location", "item", "lore",
            Loc.T("settings.templates"), Loc.T("settings.characterTemplates"), Loc.T("settings.locationTemplates"),
            Loc.T("settings.itemTemplates"), Loc.T("settings.loreTemplates"));
        IsHotkeysSectionVisible = MatchesSection(q, "hotkey", "keyboard", "shortcut", "key", "binding",
            Loc.T("settings.hotkeys"));
        IsGeneralSectionVisible = MatchesSection(q, "general", "update", "aktualisierung",
            "extension", "github", "token", "pat",
            Loc.T("update.checkForUpdates"), Loc.T("update.checkForUpdatesDesc"),
            Loc.T("settings.checkForExtensionUpdates"), Loc.T("settings.githubToken"));
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
    private int _dailyWordGoal;

    [ObservableProperty]
    private int _projectWordGoal;

    [ObservableProperty]
    private string _projectDeadline = string.Empty;

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

    public Func<TemplateEditorViewModel, Task<bool>>? ShowTemplateEditor { get; set; }

    public event Action? CloseRequested;
    public event Action? SettingsChanged;

    public SettingsViewModel(ISettingsService settingsService, IProjectService projectService)
    {
        _settingsService = settingsService;
        _projectService = projectService;

        _editorFontFamily = Settings.EditorFontFamily;
        _editorFontSize = Settings.EditorFontSize;
        _enableBookParagraphSpacing = Settings.EnableBookParagraphSpacing;
        _enableBookWidth = Settings.EnableBookWidth;
        _bookTextBlockWidth = Settings.BookTextBlockWidth;
        _bookFontFamily = Settings.BookFontFamily;
        _bookFontSize = Settings.BookFontSize;
        _selectedLanguage = Settings.AutoReplacementLanguage;
        _dialogueCorrectionEnabled = Settings.DialogueCorrectionEnabled;
        _grammarCheckEnabled = Settings.GrammarCheckEnabled;
        _dailyWordGoal = ActiveProjectGoals?.DailyGoal ?? 1000;
        _projectWordGoal = ActiveProjectGoals?.ProjectGoal ?? 50000;
        _projectDeadline = ActiveProjectGoals?.Deadline ?? string.Empty;

        AvailableUiLanguages = Loc.Instance.GetAvailableLanguages()
            .Select(code => new UiLanguageItem(code, Loc.Instance.GetLanguageDisplayName(code)))
            .ToList();

        _selectedUiLanguage = AvailableUiLanguages.FirstOrDefault(item => string.Equals(item.Code, Settings.Language, StringComparison.Ordinal))
            ?? AvailableUiLanguages.First();

        _selectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == App.ThemeService.ActiveThemeName)
            ?? AvailableThemes[0];

        // Resolve accent color: user override → theme default → fallback blue
        var accentHex = Settings.AccentColor
            ?? App.ThemeService.GetActiveThemeDefaultAccentColor()
            ?? "#007ACC";
        _accentColor = Color.TryParse(accentHex, out var parsed) ? parsed : Color.Parse("#007ACC");

        _selectedPageFormat = AvailablePageFormats.FirstOrDefault(f => string.Equals(f.Code, Settings.BookPageFormat, StringComparison.Ordinal))
            ?? AvailablePageFormats.First();

        UpdatePreview();
        UpdateBookWidthPreview();
        LoadTemplates();

        _checkForUpdates = Settings.CheckForUpdates;
        _checkForExtensionUpdates = Settings.CheckForExtensionUpdates;
        _githubToken = Settings.GitHubToken ?? string.Empty;

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

    partial void OnEditorFontFamilyChanged(string value)
    {
        Settings.EditorFontFamily = value;
        SaveAndNotify();
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        Settings.EditorFontSize = Math.Clamp(value, 8, 36);
        SaveAndNotify();
    }

    partial void OnEnableBookParagraphSpacingChanged(bool value)
    {
        Settings.EnableBookParagraphSpacing = value;
        SaveAndNotify();
    }

    partial void OnEnableBookWidthChanged(bool value)
    {
        Settings.EnableBookWidth = value;
        SaveAndNotify();
    }

    partial void OnSelectedPageFormatChanged(PageFormatItem value)
    {
        Settings.BookPageFormat = value?.Code ?? "USTrade6x9";
        OnPropertyChanged(nameof(IsCustomPageFormat));
        UpdateBookWidthPreview();
        SaveAndNotify();
    }

    partial void OnBookTextBlockWidthChanged(double? value)
    {
        Settings.BookTextBlockWidth = value;
        UpdateBookWidthPreview();
        SaveAndNotify();
    }

    partial void OnBookFontFamilyChanged(string value)
    {
        Settings.BookFontFamily = value;
        UpdateBookWidthPreview();
        SaveAndNotify();
    }

    partial void OnBookFontSizeChanged(double value)
    {
        Settings.BookFontSize = Math.Clamp(value, 6, 24);
        UpdateBookWidthPreview();
        SaveAndNotify();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        Settings.AutoReplacementLanguage = value;
        Settings.AutoReplacements = AutoReplacementDefaults.GetPreset(value);
        UpdatePreview();
        SaveAndNotify();
    }

    partial void OnDialogueCorrectionEnabledChanged(bool value)
    {
        Settings.DialogueCorrectionEnabled = value;
        SaveAndNotify();
    }

    partial void OnGrammarCheckEnabledChanged(bool value)
    {
        Settings.GrammarCheckEnabled = value;
        SaveAndNotify();
    }

    partial void OnSelectedUiLanguageChanged(UiLanguageItem value)
    {
        Settings.Language = value.Code;
        Loc.Instance.CurrentLanguage = value.Code;
        SaveAndNotify();
    }

    partial void OnSelectedThemeChanged(ThemeInfo value)
    {
        App.ThemeService.ApplyTheme(value.Name);
        Settings.Theme = value.Name;

        // When switching themes, reset accent to the theme's default
        var themeAccent = value.Source?.AccentColor;
        Settings.AccentColor = null;
        var fallback = themeAccent ?? "#007ACC";
        AccentColor = Color.TryParse(fallback, out var c) ? c : Color.Parse("#007ACC");
        App.ThemeService.ApplyAccentColor(null);

        SaveAndNotify();
    }

    partial void OnAccentColorChanged(Color value)
    {
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        Settings.AccentColor = hex;
        App.ThemeService.ApplyAccentColor(hex);
        SaveAndNotify();
    }

    [RelayCommand]
    private void ResetAccentColor()
    {
        Settings.AccentColor = null;
        var themeDefault = App.ThemeService.GetActiveThemeDefaultAccentColor() ?? "#007ACC";
        AccentColor = Color.TryParse(themeDefault, out var c) ? c : Color.Parse("#007ACC");
        App.ThemeService.ApplyAccentColor(null);
        SaveAndNotify();
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
        var chars = BookWidthCalculator.EstimateCharsPerLine(Settings);
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
