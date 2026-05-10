using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Desktop.Editor;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Utilities;

namespace Novalist.Desktop.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;
    private readonly IEntityService _entityService;
    private readonly FocusPeekExtension _focusPeekExtension;

    private ChapterData? _chapter;
    private SceneData? _scene;
    private string _savedContent = string.Empty;
    private string _plainText = string.Empty;
    private CancellationTokenSource? _autoSaveCts;
    private readonly SemaphoreSlim _openSceneGate = new(1, 1);
    private int _openSceneRequestId;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isDocumentOpen;

    [ObservableProperty]
    private bool _isSceneLoading;

    [ObservableProperty]
    private string _documentTitle = string.Empty;

    [ObservableProperty]
    private string _sceneTabTitle = string.Empty;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _characterCount;

    [ObservableProperty]
    private int _characterCountWithoutSpaces;

    [ObservableProperty]
    private int _readingTimeMinutes;

    [ObservableProperty]
    private int _readabilityScore;

    [ObservableProperty]
    private string _readabilityLevelLabel = string.Empty;

    [ObservableProperty]
    private string _readabilityColor = "#B91C1C";

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    /// <summary>Auto-save delay in milliseconds. 0 = disabled.</summary>
    public int AutoSaveDelayMs { get; set; } = 2000;

    public EditorExtensionManager ExtensionManager { get; } = new();
    public AutoReplacementExtension AutoReplacement { get; } = new();
    public DialogueCorrectionExtension DialogueCorrection { get; } = new();
    public GrammarCheckExtension GrammarCheck { get; } = new();
    public FocusPeekViewModel FocusPeek { get; } = new();
    public FocusPeekExtension FocusPeekExtension => _focusPeekExtension;

    /// <summary>
    /// Sets the grammar check contributors from loaded extensions.
    /// Called by MainWindowViewModel after extensions are loaded.
    /// </summary>
    public void SetGrammarCheckContributors(List<Novalist.Sdk.Hooks.IGrammarCheckContributor> contributors)
    {
        GrammarCheck.SetContributors(contributors);
    }

    public event Action<EntityType, object>? FocusPeekEntityOpenRequested;

    // ── Formatting state (updated by EditorView) ────────────────────

    [ObservableProperty]
    private bool _isBoldActive;

    [ObservableProperty]
    private bool _isItalicActive;

    [ObservableProperty]
    private bool _isUnderlineActive;

    [ObservableProperty]
    private bool _isAlignLeft;

    [ObservableProperty]
    private bool _isAlignCenter;

    [ObservableProperty]
    private bool _isAlignRight;

    [ObservableProperty]
    private bool _isAlignJustify;

    // ── Formatting action delegates (set by EditorView) ─────────────

    public Action? ToggleBoldAction { get; set; }
    public Action? ToggleItalicAction { get; set; }
    public Action? ToggleUnderlineAction { get; set; }
    public Action? AlignLeftAction { get; set; }
    public Action? AlignCenterAction { get; set; }
    public Action? AlignRightAction { get; set; }
    public Action? AlignJustifyAction { get; set; }

    [RelayCommand]
    private void ToggleBold() => ToggleBoldAction?.Invoke();

    [RelayCommand]
    private void ToggleItalic() => ToggleItalicAction?.Invoke();

    [RelayCommand]
    private void ToggleUnderline() => ToggleUnderlineAction?.Invoke();

    [RelayCommand]
    private void AlignLeft() => AlignLeftAction?.Invoke();

    [RelayCommand]
    private void AlignCenter() => AlignCenterAction?.Invoke();

    [RelayCommand]
    private void AlignRight() => AlignRightAction?.Invoke();

    [RelayCommand]
    private void AlignJustify() => AlignJustifyAction?.Invoke();

    public void UpdateFormattingState(bool bold, bool italic, bool underline, Avalonia.Media.TextAlignment alignment)
    {
        IsBoldActive = bold;
        IsItalicActive = italic;
        IsUnderlineActive = underline;
        IsAlignLeft = alignment == Avalonia.Media.TextAlignment.Left;
        IsAlignCenter = alignment == Avalonia.Media.TextAlignment.Center;
        IsAlignRight = alignment == Avalonia.Media.TextAlignment.Right;
        IsAlignJustify = alignment == Avalonia.Media.TextAlignment.Justify;
    }

    /// <summary>Font family from settings.</summary>
    public string EditorFontFamily => _settingsService.Settings.EditorFontFamily;

    /// <summary>Font size from settings.</summary>
    public double EditorFontSize => _settingsService.Settings.EditorFontSize;

    /// <summary>Whether book-style paragraph spacing is enabled.</summary>
    public bool BookParagraphSpacingEnabled => _settingsService.Settings.EnableBookParagraphSpacing;

    /// <summary>Whether book-width mode is enabled.</summary>
    public bool BookWidthEnabled => _settingsService.Settings.EnableBookWidth;

    /// <summary>Calculated editor max width in pixels when book-width mode is active.</summary>
    public double BookEditorWidth => BookWidthCalculator.Calculate(_settingsService.Settings);

    /// <summary>Update the editor font size and persist to settings.</summary>
    public void SetFontSize(double size)
    {
        _settingsService.Settings.EditorFontSize = Math.Clamp(size, 8, 36);
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(BookEditorWidth));
    }

    public bool HasReadability => ReadabilityScore > 0;
    public string ReadingTimeDisplay => LocFormatters.ReadingTime(ReadingTimeMinutes);
    public string ReadabilityDisplay => TextStatistics.FormatReadabilityScore(new ReadabilityResult { Score = ReadabilityScore });
    public string PlainTextContent => _plainText;

    public EditorViewModel(IProjectService projectService, ISettingsService settingsService, IEntityService entityService)
    {
        _projectService = projectService;
        _settingsService = settingsService;
        _entityService = entityService;

        // Configure auto-replacement from settings
        AutoReplacement.Pairs = settingsService.Settings.AutoReplacements;
        ExtensionManager.Register(AutoReplacement);

        // Configure dialogue correction from settings
        DialogueCorrection.Enabled = settingsService.Settings.DialogueCorrectionEnabled;
        DialogueCorrection.Language = settingsService.Settings.AutoReplacementLanguage;
        ExtensionManager.Register(DialogueCorrection);

        // Configure grammar check from settings
        GrammarCheck.Enabled = settingsService.Settings.GrammarCheckEnabled;
        GrammarCheck.Language = settingsService.Settings.Language;
        GrammarCheck.CustomApiUrl = settingsService.Settings.GrammarCheckApiUrl;
        ExtensionManager.Register(GrammarCheck);

        _focusPeekExtension = new FocusPeekExtension(FocusPeek, _projectService, _entityService, HandleFocusPeekOpenRequested);
        ExtensionManager.Register(_focusPeekExtension);
    }

    /// <summary>
    /// Re-reads settings and notifies the view to update (paragraph spacing, auto-replacements).
    /// </summary>
    public void ApplySettings()
    {
        AutoReplacement.Pairs = _settingsService.Settings.AutoReplacements;
        DialogueCorrection.Enabled = _settingsService.Settings.DialogueCorrectionEnabled;
        DialogueCorrection.Language = _settingsService.Settings.AutoReplacementLanguage;
        GrammarCheck.Enabled = _settingsService.Settings.GrammarCheckEnabled;
        GrammarCheck.Language = _settingsService.Settings.Language;
        GrammarCheck.CustomApiUrl = _settingsService.Settings.GrammarCheckApiUrl;
        OnPropertyChanged(nameof(EditorFontFamily));
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(BookParagraphSpacingEnabled));
        OnPropertyChanged(nameof(BookWidthEnabled));
        OnPropertyChanged(nameof(BookEditorWidth));
        OnPropertyChanged(nameof(AutoReplacement));
        OnPropertyChanged(nameof(DialogueCorrection));
        OnPropertyChanged(nameof(GrammarCheck));
        UpdateStats(_plainText);
    }

    public ChapterData? CurrentChapter => _chapter;
    public SceneData? CurrentScene => _scene;

    /// <summary>
    /// Opens a scene for editing. Saves the previous document first if dirty.
    /// </summary>
    public async Task OpenSceneAsync(ChapterData chapter, SceneData scene)
    {
        var requestId = Interlocked.Increment(ref _openSceneRequestId);
        IsSceneLoading = true;

        await _openSceneGate.WaitAsync();
        try
        {
            if (requestId != _openSceneRequestId)
            {
                return;
            }

            CancelAutoSave();

            // Save current if dirty
            if (IsDirty && _chapter != null && _scene != null)
            {
                await SaveAsync();
            }

            if (requestId != _openSceneRequestId)
            {
                return;
            }

            // Close previous
            ExtensionManager.NotifyDocumentClosing();

            var text = await _projectService.ReadSceneContentAsync(chapter, scene);
            if (requestId != _openSceneRequestId)
            {
                return;
            }

            _chapter = chapter;
            _scene = scene;
            _savedContent = text;
            Content = text;
            IsDirty = false;
            IsDocumentOpen = true;
            SceneTabTitle = string.IsNullOrWhiteSpace(scene.Title) ? chapter.Title : scene.Title;
            DocumentTitle = $"{chapter.Title} — {scene.Title}";
            OnPropertyChanged(nameof(CurrentChapter));
            OnPropertyChanged(nameof(CurrentScene));
            _plainText = StripHtmlForStats(text);
            UpdateStats(_plainText);

            ExtensionManager.NotifyDocumentOpened(new EditorDocumentContext
            {
                SceneId = scene.Id,
                ChapterGuid = chapter.Guid,
                SceneTitle = scene.Title,
                ChapterTitle = chapter.Title,
                FilePath = _projectService.GetSceneFilePath(chapter, scene)
            });

            // Notify the SDK-level SceneOpened event so extensions (AI Assistant,
            // etc.) can react to scene navigation.
            App.ExtensionManager?.Host?.RaiseSceneOpened(
                scene.Id, scene.Title, chapter.Guid, chapter.Title, scene.WordCount);

            if (requestId == _openSceneRequestId)
            {
                IsSceneLoading = false;
            }
        }
        finally
        {
            if (requestId == _openSceneRequestId)
            {
                IsSceneLoading = false;
            }

            _openSceneGate.Release();
        }
    }

    /// <summary>
    /// Re-reads the current scene from disk and replaces the editor content.
    /// Used after operations like snapshot restore that bypass the editor.
    /// </summary>
    public async Task ReloadCurrentSceneAsync()
    {
        if (_chapter == null || _scene == null)
            return;

        var text = await _projectService.ReadSceneContentAsync(_chapter, _scene);
        _savedContent = text;
        Content = text;
        IsDirty = false;
        _plainText = StripHtmlForStats(text);
        UpdateStats(_plainText);
    }

    /// <summary>
    /// Called by the view when the editor content changes.
    /// Content is stored as HTML to preserve formatting (bold, italic, etc.).
    /// </summary>
    public void OnTextChanged(string htmlContent, string plainText)
    {
        Content = htmlContent;
        _plainText = plainText;
        IsDirty = htmlContent != _savedContent;
        UpdateStats(plainText);
        ScheduleAutoSave();
    }

    public void OnCaretPositionChanged(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
    }

    public async Task SaveAsync()
    {
        if (_chapter == null || _scene == null || !IsDirty) return;

        await _projectService.WriteSceneContentAsync(_chapter, _scene, Content);
        _savedContent = Content;
        IsDirty = false;

        // Update word count in scene metadata
        _scene.WordCount = WordCount;
        await _projectService.SaveScenesAsync();
    }

    public async Task CloseAsync()
    {
        IsSceneLoading = true;
        CancelAutoSave();
        if (IsDirty) await SaveAsync();
        ExtensionManager.NotifyDocumentClosing();

        _chapter = null;
        _scene = null;
        Content = string.Empty;
        _savedContent = string.Empty;
        _plainText = string.Empty;
        IsDirty = false;
        IsDocumentOpen = false;
        SceneTabTitle = string.Empty;
        DocumentTitle = string.Empty;
        OnPropertyChanged(nameof(CurrentChapter));
        OnPropertyChanged(nameof(CurrentScene));
        WordCount = 0;
        CharacterCount = 0;
        CharacterCountWithoutSpaces = 0;
        ReadingTimeMinutes = 0;
        ReadabilityScore = 0;
        ReadabilityLevelLabel = string.Empty;
        ReadabilityColor = "#B91C1C";
        FocusPeek.Hide();
        IsSceneLoading = false;
    }

    private void UpdateStats(string text)
    {
        var statistics = TextStatistics.Calculate(text, _settingsService.Settings.AutoReplacementLanguage);

        CharacterCount = statistics.CharacterCount;
        CharacterCountWithoutSpaces = statistics.CharacterCountWithoutSpaces;
        WordCount = statistics.WordCount;
        ReadingTimeMinutes = statistics.ReadingTimeMinutes;
        ReadabilityScore = statistics.Readability.Score;
        ReadabilityLevelLabel = LocFormatters.ReadabilityLevel(statistics.Readability.Level);
        ReadabilityColor = TextStatistics.GetReadabilityColor(statistics.Readability.Level);

        OnPropertyChanged(nameof(HasReadability));
        OnPropertyChanged(nameof(ReadingTimeDisplay));
        OnPropertyChanged(nameof(ReadabilityDisplay));
    }

    private void ScheduleAutoSave()
    {
        if (AutoSaveDelayMs <= 0) return;

        CancelAutoSave();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSaveDelayMs, token);
                if (!token.IsCancellationRequested && IsDirty)
                {
                    await SaveAsync();
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void CancelAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
    }

    public Task RefreshFocusPeekAsync()
        => _focusPeekExtension.RefreshEntityIndexAsync();

    private void HandleFocusPeekOpenRequested(EntityType type, object entity)
    {
        FocusPeekEntityOpenRequested?.Invoke(type, entity);
    }

    private static string StripHtmlForStats(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (!content.TrimStart().StartsWith('<')) return content;
        // Quick HTML tag strip for stats — not a full parser, just enough for word/char counts.
        var text = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(text);
    }
}
