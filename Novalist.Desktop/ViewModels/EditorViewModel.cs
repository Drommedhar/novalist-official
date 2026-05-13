using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>
    /// Open scene tabs in this editor pane. The active tab's content is shown
    /// in the WebView; switching tabs caches the live content of the previous
    /// tab and restores the new tab's cached content into the editor.
    /// </summary>
    public ObservableCollection<EditorOpenScene> OpenScenes { get; } = [];

    [ObservableProperty]
    private EditorOpenScene? _activeOpenScene;

    /// <summary>True when this pane has focus (drives ActiveEditor in MainWindow).</summary>
    [ObservableProperty]
    private bool _isPaneFocused;

    public bool HasMultipleTabs => OpenScenes.Count > 1;
    public bool HasOpenScenes => OpenScenes.Count > 0;

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
    public event Action<ChapterData, SceneData>? SceneSaved;

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

    /// <summary>Wraps the current selection in a comment span. Param = comment id.</summary>
    public Action<string>? AddCommentAction { get; set; }
    /// <summary>Inserts a footnote anchor at the current caret position. Param = footnote id.</summary>
    public Action<string>? AddFootnoteAction { get; set; }
    /// <summary>Applies a named paragraph style to the paragraph at the caret.
    /// Pass empty string to clear styles. Built-in ids: heading, subheading,
    /// blockquote, poetry.</summary>
    public Action<string>? ApplyParagraphStyleAction { get; set; }
    /// <summary>Removes a footnote anchor. Param = footnote id.</summary>
    public Action<string>? RemoveFootnoteAction { get; set; }
    /// <summary>Scrolls to and highlights a footnote anchor. Param = footnote id.</summary>
    public Action<string>? ScrollToFootnoteAction { get; set; }

    /// <summary>Fired by the view when the WebView reports a new footnote anchor.</summary>
    public event Action<string, int>? FootnoteInserted; // footnoteId, ordinal
    /// <summary>Fired when the user clicks a footnote anchor.</summary>
    public event Action<string>? FootnoteClicked;
    internal void RaiseFootnoteInserted(string id, int number) => FootnoteInserted?.Invoke(id, number);
    internal void RaiseFootnoteClicked(string id) => FootnoteClicked?.Invoke(id);

    /// <summary>Fired when the user clicks "Add comment" in the WebView
    /// (toolbar / context menu). MainWindow listens to drive the existing
    /// AddComment flow.</summary>
    public event Action? AddCommentRequested;
    /// <summary>Fired when the user clicks "Add footnote" in the WebView.</summary>
    public event Action? AddFootnoteRequested;
    internal void RaiseAddCommentRequested() => AddCommentRequested?.Invoke();
    internal void RaiseAddFootnoteRequested() => AddFootnoteRequested?.Invoke();
    /// <summary>Removes the comment span for the given id.</summary>
    public Action<string>? RemoveCommentAction { get; set; }
    /// <summary>Scrolls to and highlights the comment span.</summary>
    public Action<string>? ScrollToCommentAction { get; set; }
    /// <summary>Pushes the current scene's comment list to the WebView gutter.</summary>
    public Action? SyncCommentsAction { get; set; }

    /// <summary>Fired by the view when the WebView reports a new comment was anchored.</summary>
    public event Action<string, string>? CommentAnchored; // commentId, anchorText
    /// <summary>Fired by the view when the user clicks an existing comment span.</summary>
    public event Action<string>? CommentClicked; // commentId
    /// <summary>Fired when the gutter card text is edited.</summary>
    public event Action<string, string>? CommentTextEdited; // commentId, text
    /// <summary>Fired when the gutter card delete button is clicked.</summary>
    public event Action<string>? CommentDeleteRequested; // commentId

    internal void RaiseCommentAnchored(string id, string anchor) => CommentAnchored?.Invoke(id, anchor);
    internal void RaiseCommentClicked(string id) => CommentClicked?.Invoke(id);
    internal void RaiseCommentTextEdited(string id, string text) => CommentTextEdited?.Invoke(id, text);
    internal void RaiseCommentDeleteRequested(string id) => CommentDeleteRequested?.Invoke(id);

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

    /// <summary>Whether typewriter scroll is enabled.</summary>
    public bool TypewriterScrollEnabled => _settingsService.Settings.TypewriterScrollEnabled;

    /// <summary>Vertical anchor for typewriter scroll: "top" | "middle" | "bottom".</summary>
    public string TypewriterScrollAnchor => _settingsService.Settings.TypewriterScrollAnchor ?? "middle";

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

        // Persist comment edits made in the margin gutter back to the scene file.
        CommentTextEdited += (id, newText) =>
        {
            if (_scene?.Comments == null) return;
            var c = _scene.Comments.FirstOrDefault(x => x.Id == id);
            if (c == null) return;
            c.Text = newText;
            _ = _projectService.SaveScenesAsync();
        };
        CommentDeleteRequested += id =>
        {
            if (_scene == null) return;
            RemoveCommentAction?.Invoke(id);
            _scene.Comments?.RemoveAll(x => x.Id == id);
            _ = _projectService.SaveScenesAsync();
            SyncCommentsAction?.Invoke();
        };
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
        OnPropertyChanged(nameof(TypewriterScrollEnabled));
        OnPropertyChanged(nameof(TypewriterScrollAnchor));
        OnPropertyChanged(nameof(AutoReplacement));
        OnPropertyChanged(nameof(DialogueCorrection));
        OnPropertyChanged(nameof(GrammarCheck));
        UpdateStats(_plainText);
    }

    public ChapterData? CurrentChapter => _chapter;
    public SceneData? CurrentScene => _scene;

    /// <summary>
    /// Opens a scene for editing. If a tab for the same scene already exists,
    /// activates it; otherwise adds a new tab and activates it. Saves the
    /// previously active tab's dirty content first.
    /// </summary>
    public async Task OpenSceneAsync(ChapterData chapter, SceneData scene)
    {
        // Existing tab? Just switch to it.
        var existing = OpenScenes.FirstOrDefault(t => string.Equals(t.Scene.Id, scene.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            await ActivateTabAsync(existing);
            return;
        }

        // Cache live content of currently active tab before switching.
        await CacheActiveTabAsync();

        var newTab = new EditorOpenScene(chapter, scene);
        OpenScenes.Add(newTab);
        OnPropertyChanged(nameof(HasMultipleTabs)); OnPropertyChanged(nameof(HasOpenScenes));
        await ActivateTabAsync(newTab, isFreshLoad: true);
    }

    /// <summary>
    /// Switches the editor to show the given already-open tab.
    /// </summary>
    public async Task ActivateTabAsync(EditorOpenScene tab, bool isFreshLoad = false)
    {
        if (!OpenScenes.Contains(tab)) return;
        if (ActiveOpenScene == tab && !isFreshLoad) return;

        // Snapshot live state into outgoing tab before switch.
        if (!isFreshLoad)
            await CacheActiveTabAsync();

        var requestId = Interlocked.Increment(ref _openSceneRequestId);
        IsSceneLoading = true;
        await _openSceneGate.WaitAsync();
        try
        {
            if (requestId != _openSceneRequestId) return;

            CancelAutoSave();
            ExtensionManager.NotifyDocumentClosing();

            // Use cached content if available; otherwise read from disk.
            string text;
            if (tab.HasCachedContent)
            {
                text = tab.CachedContent;
            }
            else
            {
                text = await _projectService.ReadSceneContentAsync(tab.Chapter, tab.Scene);
                tab.CachedContent = text;
                tab.SavedContent = text;
                tab.HasCachedContent = true;
            }

            if (requestId != _openSceneRequestId) return;

            _chapter = tab.Chapter;
            _scene = tab.Scene;
            _savedContent = tab.SavedContent;
            Content = text;
            IsDirty = tab.IsDirty;
            IsDocumentOpen = true;
            SceneTabTitle = string.IsNullOrWhiteSpace(tab.Scene.Title) ? tab.Chapter.Title : tab.Scene.Title;
            DocumentTitle = $"{tab.Chapter.Title} — {tab.Scene.Title}";
            foreach (var t in OpenScenes) t.IsActive = t == tab;
            ActiveOpenScene = tab;
            OnPropertyChanged(nameof(CurrentChapter));
            OnPropertyChanged(nameof(CurrentScene));
            _plainText = StripHtmlForStats(text);
            UpdateStats(_plainText);

            ExtensionManager.NotifyDocumentOpened(new EditorDocumentContext
            {
                SceneId = tab.Scene.Id,
                ChapterGuid = tab.Chapter.Guid,
                SceneTitle = tab.Scene.Title,
                ChapterTitle = tab.Chapter.Title,
                FilePath = _projectService.GetSceneFilePath(tab.Chapter, tab.Scene)
            });

            App.ExtensionManager?.Host?.RaiseSceneOpened(
                tab.Scene.Id, tab.Scene.Title, tab.Chapter.Guid, tab.Chapter.Title, tab.Scene.WordCount);
        }
        finally
        {
            if (requestId == _openSceneRequestId)
                IsSceneLoading = false;
            _openSceneGate.Release();
        }
    }

    /// <summary>
    /// Closes a tab. Saves dirty content first. Activates the next available
    /// tab, or clears the editor if this was the last one.
    /// </summary>
    public async Task CloseTabAsync(EditorOpenScene tab)
    {
        if (!OpenScenes.Contains(tab)) return;

        // If closing the active tab, save its current state first.
        if (ActiveOpenScene == tab)
        {
            if (IsDirty) await SaveAsync();
        }
        else if (tab.IsDirty)
        {
            // Persist dirty cached content for non-active tab.
            await _projectService.WriteSceneContentAsync(tab.Chapter, tab.Scene, tab.CachedContent);
            tab.Scene.WordCount = CountWords(StripHtmlForStats(tab.CachedContent));
            await _projectService.SaveScenesAsync();
            tab.IsDirty = false;
            tab.SavedContent = tab.CachedContent;
        }

        var idx = OpenScenes.IndexOf(tab);
        OpenScenes.Remove(tab);
        OnPropertyChanged(nameof(HasMultipleTabs)); OnPropertyChanged(nameof(HasOpenScenes));

        if (ActiveOpenScene == tab)
        {
            if (OpenScenes.Count == 0)
            {
                await ClearEditorStateAsync();
            }
            else
            {
                var next = OpenScenes[Math.Clamp(idx, 0, OpenScenes.Count - 1)];
                await ActivateTabAsync(next, isFreshLoad: true);
            }
        }
    }

    /// <summary>Removes a tab from this pane without saving (caller handles transfer).</summary>
    public async Task<EditorOpenScene?> DetachTabAsync(EditorOpenScene tab)
    {
        if (!OpenScenes.Contains(tab)) return null;
        if (ActiveOpenScene == tab)
        {
            // Cache live content first so we don't lose user edits.
            await CacheActiveTabAsync();
        }
        var idx = OpenScenes.IndexOf(tab);
        OpenScenes.Remove(tab);
        OnPropertyChanged(nameof(HasMultipleTabs)); OnPropertyChanged(nameof(HasOpenScenes));

        if (ActiveOpenScene == tab)
        {
            if (OpenScenes.Count == 0) await ClearEditorStateAsync();
            else await ActivateTabAsync(OpenScenes[Math.Clamp(idx, 0, OpenScenes.Count - 1)], isFreshLoad: true);
        }
        return tab;
    }

    /// <summary>Adds a previously-detached tab to this pane and activates it.</summary>
    public async Task AttachTabAsync(EditorOpenScene tab)
    {
        OpenScenes.Add(tab);
        OnPropertyChanged(nameof(HasMultipleTabs)); OnPropertyChanged(nameof(HasOpenScenes));
        await ActivateTabAsync(tab, isFreshLoad: true);
    }

    private async Task CacheActiveTabAsync()
    {
        var active = ActiveOpenScene;
        if (active == null) return;
        active.CachedContent = Content;
        active.IsDirty = IsDirty;
        active.HasCachedContent = true;
        if (IsDirty) await SaveAsync();
    }

    private async Task ClearEditorStateAsync()
    {
        await Task.CompletedTask;
        CancelAutoSave();
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
        ActiveOpenScene = null;
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
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return System.Text.RegularExpressions.Regex.Matches(text,
            @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;
    }

    [RelayCommand]
    private async Task ActivateTabFromUi(EditorOpenScene? tab)
    {
        if (tab != null) await ActivateTabAsync(tab);
    }

    [RelayCommand]
    private async Task CloseTabFromUi(EditorOpenScene? tab)
    {
        if (tab != null) await CloseTabAsync(tab);
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
        if (ActiveOpenScene != null) ActiveOpenScene.IsDirty = IsDirty;
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

        // Mirror into the active tab so future switches see clean state.
        if (ActiveOpenScene != null)
        {
            ActiveOpenScene.CachedContent = Content;
            ActiveOpenScene.SavedContent = Content;
            ActiveOpenScene.IsDirty = false;
        }

        // Update word count in scene metadata
        _scene.WordCount = WordCount;
        await _projectService.SaveScenesAsync();
        SceneSaved?.Invoke(_chapter, _scene);
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

public partial class EditorOpenScene : ObservableObject
{
    public ChapterData Chapter { get; }
    public SceneData Scene { get; }

    [ObservableProperty]
    private string _displayTitle;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    public string CachedContent { get; set; } = string.Empty;
    public string SavedContent { get; set; } = string.Empty;
    public bool HasCachedContent { get; set; }

    public EditorOpenScene(ChapterData chapter, SceneData scene)
    {
        Chapter = chapter;
        Scene = scene;
        _displayTitle = string.IsNullOrWhiteSpace(scene.Title) ? chapter.Title : scene.Title;
    }
}
