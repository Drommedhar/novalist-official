using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class ContextSidebarViewModel : ObservableObject
{
    private static readonly Regex DialogueRegex = new(
        "(?:\"[^\"]*\"|“[^”]*”|„[^“]*“|«[^»]*»|»[^«]*«|‹[^›]*›|‚[^‘]*‘)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex SentenceRegex = new(
        @"[^.!?]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FirstPersonRegex = new(
        @"\b(i|me|my|mine|myself|we|us|our|ours|ourselves)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] PositiveWords =
    [
        "hope",
        "joy",
        "warm",
        "smile",
        "relief",
        "victory",
        "triumph",
        "laugh",
        "laughing",
        "love",
        "gentle",
        "bright",
        "calm",
        "peace",
        "safe"
    ];

    private static readonly string[] NegativeWords =
    [
        "fear",
        "panic",
        "anger",
        "angry",
        "blood",
        "hurt",
        "threat",
        "danger",
        "despair",
        "sad",
        "grief",
        "dark",
        "cold",
        "cry",
        "scream"
    ];

    private static readonly string[] ConflictKeywords =
    [
        "argue",
        "battle",
        "chase",
        "clash",
        "conflict",
        "demand",
        "fight",
        "flee",
        "force",
        "hide",
        "refuse",
        "secret",
        "struggle",
        "threat",
        "warn"
    ];

    private static readonly IReadOnlyList<EmotionProfile> EmotionProfiles =
    [
        new("neutral", "Neutral", ["steady", "plain", "quiet", "routine", "settled"]),
        new("tense", "Tense", ["tense", "edge", "pressure", "alarm", "strain", "uneasy"]),
        new("joyful", "Joyful", ["joy", "glad", "celebrate", "delight", "smile", "laugh"]),
        new("melancholic", "Melancholic", ["melancholy", "lonely", "empty", "wistful", "faded"]),
        new("angry", "Angry", ["anger", "furious", "rage", "snap", "resent", "spite"]),
        new("fearful", "Fearful", ["fear", "panic", "terror", "dread", "afraid", "shiver"]),
        new("romantic", "Romantic", ["kiss", "touch", "beloved", "heart", "desire", "tender"]),
        new("mysterious", "Mysterious", ["shadow", "mystery", "secret", "strange", "unknown", "whisper"]),
        new("humorous", "Humorous", ["joke", "laugh", "grin", "tease", "comic", "amused"]),
        new("hopeful", "Hopeful", ["hope", "promise", "rise", "chance", "believe", "future"]),
        new("desperate", "Desperate", ["desperate", "last", "plead", "beg", "hopeless", "breaking"]),
        new("peaceful", "Peaceful", ["peace", "calm", "still", "soft", "rest", "gentle"]),
        new("chaotic", "Chaotic", ["chaos", "riot", "wild", "fracture", "spiral", "rattle"]),
        new("sorrowful", "Sorrowful", ["sorrow", "grief", "weep", "mourning", "ache", "loss"]),
        new("triumphant", "Triumphant", ["triumph", "victory", "won", "conquer", "defiant", "surge"]),
        new("somber", "Somber", ["somber", "grim", "mournful", "bleak", "subdued", "solemn", "heavy"])
    ];

    private static readonly IReadOnlyList<string> EmotionKeys = EmotionProfiles
        .Select(profile => profile.Key)
        .ToList();

    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    private EditorViewModel? _editor;
    private Dictionary<string, ContextSidebarChapterSnapshot> _chapterSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private List<ContextSidebarEntitySource> _characterSources = [];
    private List<ContextSidebarEntitySource> _locationSources = [];
    private List<ContextSidebarEntitySource> _itemSources = [];
    private List<ContextSidebarEntitySource> _loreSources = [];
    private int _snapshotVersion;

    [ObservableProperty]
    private bool _isContextAvailable;

    [ObservableProperty]
    private bool _isContextLoading;

    [ObservableProperty]
    private string _contextLabel = string.Empty;

    [ObservableProperty]
    private string _contextSubtitle = string.Empty;

    [ObservableProperty]
    private string _contextDate = string.Empty;

    [ObservableProperty]
    private bool _isCharactersSectionExpanded = true;

    [ObservableProperty]
    private bool _isMentionsSectionExpanded = true;

    [ObservableProperty]
    private bool _isLocationsSectionExpanded = true;

    [ObservableProperty]
    private bool _isItemsSectionExpanded = true;

    [ObservableProperty]
    private bool _isLoreSectionExpanded = true;

    [ObservableProperty]
    private bool _isSceneAnalysisSectionExpanded = true;

    [ObservableProperty]
    private ObservableCollection<ContextSidebarEntityCardViewModel> _characterCards = [];

    [ObservableProperty]
    private ObservableCollection<ContextSidebarMentionRowViewModel> _mentionRows = [];

    [ObservableProperty]
    private ObservableCollection<ContextSidebarEntityCardViewModel> _locationCards = [];

    [ObservableProperty]
    private ObservableCollection<ContextSidebarEntityCardViewModel> _itemCards = [];

    [ObservableProperty]
    private ObservableCollection<ContextSidebarEntityCardViewModel> _loreCards = [];

    [ObservableProperty]
    private ContextSidebarSceneAnalysisViewModel? _sceneAnalysis;

    public event Action<EntityType, object>? EntityOpenRequested;

    public bool HasContextDate => !string.IsNullOrWhiteSpace(ContextDate);

    public string ContextDateDisplay
    {
        get
        {
            var raw = ContextDate;
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return raw;
            try
            {
                var culture = CultureInfo.GetCultureInfo(Loc.Instance.CurrentLanguage);
                var dayName = dt.ToString("dddd", culture);
                return $"{raw} · {dayName}";
            }
            catch
            {
                return $"{raw} · {dt:dddd}";
            }
        }
    }
    public bool HasContextSubtitle => !string.IsNullOrWhiteSpace(ContextSubtitle);
    public bool HasCharacterCards => CharacterCards.Count > 0;
    public bool HasMentionFrequency => MentionRows.Count > 0;
    public bool HasLocationCards => LocationCards.Count > 0;
    public bool HasItemCards => ItemCards.Count > 0;
    public bool HasLoreCards => LoreCards.Count > 0;
    public bool HasSceneAnalysis => SceneAnalysis != null;
    public bool HasAnyContent => HasCharacterCards || HasMentionFrequency || HasLocationCards || HasItemCards || HasLoreCards || HasSceneAnalysis;
    public string ContextStatusMessage => IsContextLoading
        ? Loc.T("context.loading")
        : Loc.T("context.empty");

    public ContextSidebarViewModel(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
        LoadSectionState();
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(ContextDateDisplay));
    }

    private void LoadSectionState()
    {
        var vs = _projectService.ProjectSettings.ViewState;
        IsCharactersSectionExpanded = vs.ContextCharactersExpanded;
        IsMentionsSectionExpanded = vs.ContextMentionsExpanded;
        IsLocationsSectionExpanded = vs.ContextLocationsExpanded;
        IsItemsSectionExpanded = vs.ContextItemsExpanded;
        IsLoreSectionExpanded = vs.ContextLoreExpanded;
        IsSceneAnalysisSectionExpanded = vs.ContextSceneAnalysisExpanded;
    }

    partial void OnIsCharactersSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextCharactersExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    partial void OnIsMentionsSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextMentionsExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    partial void OnIsLocationsSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextLocationsExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    partial void OnIsItemsSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextItemsExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    partial void OnIsLoreSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextLoreExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    partial void OnIsSceneAnalysisSectionExpandedChanged(bool value)
    {
        _projectService.ProjectSettings.ViewState.ContextSceneAnalysisExpanded = value;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    [RelayCommand]
    private void ToggleCharactersSection() => IsCharactersSectionExpanded = !IsCharactersSectionExpanded;

    [RelayCommand]
    private void ToggleMentionsSection() => IsMentionsSectionExpanded = !IsMentionsSectionExpanded;

    [RelayCommand]
    private void ToggleLocationsSection() => IsLocationsSectionExpanded = !IsLocationsSectionExpanded;

    [RelayCommand]
    private void ToggleItemsSection() => IsItemsSectionExpanded = !IsItemsSectionExpanded;

    [RelayCommand]
    private void ToggleLoreSection() => IsLoreSectionExpanded = !IsLoreSectionExpanded;

    [RelayCommand]
    private void ToggleSceneAnalysisSection() => IsSceneAnalysisSectionExpanded = !IsSceneAnalysisSectionExpanded;

    public void AttachEditor(EditorViewModel? editor)
    {
        if (_editor != null)
            _editor.PropertyChanged -= OnEditorPropertyChanged;

        _editor = editor;

        if (_editor != null)
            _editor.PropertyChanged += OnEditorPropertyChanged;

        RefreshContext();
    }

    public async Task RefreshEntityDataAsync()
    {
        if (!_projectService.IsProjectLoaded)
        {
            _characterSources.Clear();
            _locationSources.Clear();
            _itemSources.Clear();
            _loreSources.Clear();
            RefreshContext();
            return;
        }

        var charactersTask = _entityService.LoadCharactersAsync();
        var locationsTask = _entityService.LoadLocationsAsync();
        var itemsTask = _entityService.LoadItemsAsync();
        var loreTask = _entityService.LoadLoreAsync();

        await Task.WhenAll(charactersTask, locationsTask, itemsTask, loreTask);

        _characterSources = charactersTask.Result
            .Select(character => new ContextSidebarEntitySource(
                EntityType.Character,
                character,
                BuildPatterns(GetCharacterAliases(character))))
            .ToList();

        _locationSources = locationsTask.Result
            .Select(location => new ContextSidebarEntitySource(
                EntityType.Location,
                location,
                BuildPatterns([location.Name])))
            .ToList();

        _itemSources = itemsTask.Result
            .Select(item => new ContextSidebarEntitySource(
                EntityType.Item,
                item,
                BuildPatterns([item.Name])))
            .ToList();

        _loreSources = loreTask.Result
            .Select(lore => new ContextSidebarEntitySource(
                EntityType.Lore,
                lore,
                BuildPatterns([lore.Name])))
            .ToList();

        RefreshContext();
    }

    public void RefreshContext()
    {
        if (_editor?.IsDocumentOpen != true || _editor.CurrentChapter == null || _editor.CurrentScene == null)
        {
            ClearContext();
            return;
        }

        var chapter = _editor.CurrentChapter;
        var scene = _editor.CurrentScene;
        var scenes = _projectService.GetScenesForChapter(chapter.Guid)
            .OrderBy(candidate => candidate.Order)
            .ToList();
        var sceneIndex = scenes.FindIndex(candidate => candidate.Id == scene.Id) + 1;

        ContextLabel = string.IsNullOrWhiteSpace(scene.Title)
            ? chapter.Title.Trim()
            : scene.Title.Trim();
        ContextSubtitle = BuildContextSubtitle(chapter, sceneIndex, scenes.Count);
        ContextDate = !string.IsNullOrWhiteSpace(scene.Date)
            ? scene.Date.Trim()
            : chapter.Date.Trim();

        var currentContent = _editor.PlainTextContent;

        UpdateCurrentSceneSnapshot(chapter, scene, currentContent);
        BuildVisibleContext(chapter, scene, currentContent);

        IsContextAvailable = true;

        var forceReload = NeedsSnapshotReload(chapter.Guid, scenes);
        _ = EnsureProjectContextAsync(forceReload);
    }

    public void RefreshContextForScene(ChapterData chapter, SceneData scene, string plainTextContent)
    {
        var scenes = _projectService.GetScenesForChapter(chapter.Guid)
            .OrderBy(candidate => candidate.Order)
            .ToList();
        var sceneIndex = scenes.FindIndex(candidate => candidate.Id == scene.Id) + 1;

        ContextLabel = string.IsNullOrWhiteSpace(scene.Title)
            ? chapter.Title.Trim()
            : scene.Title.Trim();
        ContextSubtitle = BuildContextSubtitle(chapter, sceneIndex, scenes.Count);
        ContextDate = !string.IsNullOrWhiteSpace(scene.Date)
            ? scene.Date.Trim()
            : chapter.Date.Trim();

        UpdateCurrentSceneSnapshot(chapter, scene, plainTextContent);
        BuildVisibleContext(chapter, scene, plainTextContent);

        IsContextAvailable = true;

        var forceReload = NeedsSnapshotReload(chapter.Guid, scenes);
        _ = EnsureProjectContextAsync(forceReload);
    }

    private bool _refreshPending;

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Content)
            or nameof(EditorViewModel.IsDocumentOpen)
            or nameof(EditorViewModel.DocumentTitle))
        {
            if (_refreshPending) return;
            _refreshPending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _refreshPending = false;
                RefreshContext();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private async Task EnsureProjectContextAsync(bool forceReload)
    {
        if (!forceReload || _editor?.CurrentChapter == null || _editor.CurrentScene == null)
            return;

        var currentChapter = _editor.CurrentChapter;
        var currentScene = _editor.CurrentScene;
        var currentContent = _editor.PlainTextContent;
        var refreshVersion = ++_snapshotVersion;

        IsContextLoading = true;

        try
        {
            var snapshots = await BuildProjectSnapshotsAsync(currentChapter, currentScene, currentContent);
            if (refreshVersion != _snapshotVersion)
                return;

            _chapterSnapshots = snapshots;
            UpdateCurrentSceneSnapshot(currentChapter, currentScene, currentContent);
            BuildVisibleContext(currentChapter, currentScene, currentContent);
        }
        catch
        {
            if (refreshVersion == _snapshotVersion)
                BuildVisibleContext(currentChapter, currentScene, currentContent);
        }
        finally
        {
            if (refreshVersion == _snapshotVersion)
                IsContextLoading = false;
        }
    }

    /// <summary>
    /// Eager preload: builds chapter snapshots from disk so first scene-open Ctx refresh
    /// runs forceReload=False instead of doing N file reads inline.
    /// </summary>
    public async Task PreloadSnapshotsAsync()
    {
        var snapshots = new Dictionary<string, ContextSidebarChapterSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid)
                .OrderBy(scene => scene.Order)
                .ToList();
            var sceneSnapshots = new List<ContextSidebarSceneSnapshot>(scenes.Count);
            foreach (var scene in scenes)
            {
                var content = NormalizeSceneContent(await _projectService.ReadSceneContentAsync(chapter, scene));
                sceneSnapshots.Add(new ContextSidebarSceneSnapshot(scene, content));
            }
            snapshots[chapter.Guid] = new ContextSidebarChapterSnapshot(chapter, sceneSnapshots);
        }
        _chapterSnapshots = snapshots;
    }

    private async Task<Dictionary<string, ContextSidebarChapterSnapshot>> BuildProjectSnapshotsAsync(
        ChapterData currentChapter,
        SceneData currentScene,
        string currentContent)
    {
        var snapshots = new Dictionary<string, ContextSidebarChapterSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid)
                .OrderBy(scene => scene.Order)
                .ToList();

            var sceneSnapshots = new List<ContextSidebarSceneSnapshot>(scenes.Count);
            foreach (var scene in scenes)
            {
                var content = string.Equals(chapter.Guid, currentChapter.Guid, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(scene.Id, currentScene.Id, StringComparison.OrdinalIgnoreCase)
                    ? currentContent
                    : NormalizeSceneContent(await _projectService.ReadSceneContentAsync(chapter, scene));

                sceneSnapshots.Add(new ContextSidebarSceneSnapshot(scene, content));
            }

            snapshots[chapter.Guid] = new ContextSidebarChapterSnapshot(chapter, sceneSnapshots);
        }

        return snapshots;
    }

    private void BuildVisibleContext(ChapterData chapter, SceneData scene, string sceneContent)
    {
        var chapterSnapshot = _chapterSnapshots.TryGetValue(chapter.Guid, out var snapshot) ? snapshot : null;
        var currentSceneSnapshot = chapterSnapshot?.Scenes.FirstOrDefault(candidate => string.Equals(candidate.Scene.Id, scene.Id, StringComparison.OrdinalIgnoreCase));
        var entityContent = currentSceneSnapshot?.Content ?? sceneContent;

        var matchedCharacters = MatchSources(entityContent, _characterSources);
        var matchedLocations = MatchSources(entityContent, _locationSources);
        var matchedItems = MatchSources(entityContent, _itemSources);
        var matchedLore = MatchSources(entityContent, _loreSources);
        var povOptions = BuildPovOptions(matchedCharacters, chapter, scene);

        CharacterCards = new ObservableCollection<ContextSidebarEntityCardViewModel>(BuildCharacterCards(matchedCharacters, chapter, scene));
        MentionRows = new ObservableCollection<ContextSidebarMentionRowViewModel>(BuildMentionRows(matchedCharacters, chapter, scene));
        LocationCards = new ObservableCollection<ContextSidebarEntityCardViewModel>(BuildLocationCards(matchedLocations));
        ItemCards = new ObservableCollection<ContextSidebarEntityCardViewModel>(BuildItemCards(matchedItems));
        LoreCards = new ObservableCollection<ContextSidebarEntityCardViewModel>(BuildLoreCards(matchedLore));
        SceneAnalysis = BuildSceneAnalysis(chapterSnapshot, chapter, scene, sceneContent, povOptions);

        NotifyStateChanged();
    }

    private IReadOnlyList<ContextSidebarEntityCardViewModel> BuildCharacterCards(
        IReadOnlyList<ContextSidebarMatchedSource> matches,
        ChapterData chapter,
        SceneData scene)
        => matches
            .Select(match =>
            {
                var character = (CharacterData)match.Source.Entity;
                var display = ResolveCharacterDisplay(character, chapter, scene);
                var pills = new List<ContextSidebarPillViewModel>();
                AddPill(pills, Loc.T("context.genderPill"), display.Gender);
                AddPill(pills, Loc.T("context.agePill"), display.Age);

                var groupBadge = string.IsNullOrWhiteSpace(display.Group)
                    ? string.Empty
                    : Loc.T("context.groupBadge", display.Group.Trim());

                return new ContextSidebarEntityCardViewModel(
                    display.Name,
                    display.Role,
                    groupBadge,
                    string.Empty,
                    string.Empty,
                    pills,
                    new RelayCommand(() => EntityOpenRequested?.Invoke(EntityType.Character, character)));
            })
            .ToList();

    private IReadOnlyList<ContextSidebarMentionRowViewModel> BuildMentionRows(
        IReadOnlyList<ContextSidebarMatchedSource> matchedCharacters,
        ChapterData chapter,
        SceneData scene)
    {
        var chapters = _projectService.GetChaptersOrdered();
        if (chapters.Count == 0 || _chapterSnapshots.Count != chapters.Count || matchedCharacters.Count == 0)
            return [];

        var rows = new List<ContextSidebarMentionRowViewModel>(matchedCharacters.Count);
        for (var index = 0; index < matchedCharacters.Count; index++)
        {
            var matched = matchedCharacters[index];
            if (matched.Source.Entity is not CharacterData character)
                continue;

            var display = ResolveCharacterDisplay(character, chapter, scene);
            var cells = new List<ContextSidebarMentionCellViewModel>(chapters.Count);
            var mentions = new bool[chapters.Count];

            for (var chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                var candidateChapter = chapters[chapterIndex];
                var present = _chapterSnapshots.TryGetValue(candidateChapter.Guid, out var chapterSnapshot)
                    && matched.Source.IsMatch(chapterSnapshot.AggregateText);
                mentions[chapterIndex] = present;

                var label = (chapterIndex + 1).ToString();
                var toolTip = present
                    ? Loc.T("context.mentionedTooltip", candidateChapter.Title)
                    : Loc.T("context.absentTooltip", candidateChapter.Title);

                cells.Add(new ContextSidebarMentionCellViewModel(
                    label,
                    present,
                    string.Equals(candidateChapter.Guid, chapter.Guid, StringComparison.OrdinalIgnoreCase),
                    toolTip));
            }

            var gap = 0;
            for (var chapterIndex = mentions.Length - 1; chapterIndex >= 0; chapterIndex--)
            {
                if (mentions[chapterIndex])
                    break;

                gap++;
            }

            var gapWarning = gap >= 3
                ? Loc.T("context.lastSeenGap", gap)
                : string.Empty;

            rows.Add(new ContextSidebarMentionRowViewModel(
                display.Name,
                gapWarning,
                cells,
                new RelayCommand(() => EntityOpenRequested?.Invoke(EntityType.Character, character))));
        }

        return rows;
    }

    private IReadOnlyList<ContextSidebarEntityCardViewModel> BuildLocationCards(IReadOnlyList<ContextSidebarMatchedSource> matches)
        => matches
            .Select(match =>
            {
                var location = (LocationData)match.Source.Entity;
                var parent = NormalizeEntityReference(location.Parent);

                return new ContextSidebarEntityCardViewModel(
                    location.Name,
                    string.Empty,
                    string.IsNullOrWhiteSpace(parent) ? string.Empty : Loc.T("context.inLocation", parent),
                    TrimDescription(location.Description),
                    string.Empty,
                    [],
                    new RelayCommand(() => EntityOpenRequested?.Invoke(EntityType.Location, location)));
            })
            .ToList();

    private IReadOnlyList<ContextSidebarEntityCardViewModel> BuildItemCards(IReadOnlyList<ContextSidebarMatchedSource> matches)
        => matches
            .Select(match =>
            {
                var item = (ItemData)match.Source.Entity;
                return new ContextSidebarEntityCardViewModel(
                    item.Name,
                    item.Type,
                    string.Empty,
                    TrimDescription(item.Description),
                    string.Empty,
                    [],
                    new RelayCommand(() => EntityOpenRequested?.Invoke(EntityType.Item, item)));
            })
            .ToList();

    private IReadOnlyList<ContextSidebarEntityCardViewModel> BuildLoreCards(IReadOnlyList<ContextSidebarMatchedSource> matches)
        => matches
            .Select(match =>
            {
                var lore = (LoreData)match.Source.Entity;
                return new ContextSidebarEntityCardViewModel(
                    lore.Name,
                    lore.Category,
                    string.Empty,
                    TrimDescription(lore.Description),
                    string.Empty,
                    [],
                    new RelayCommand(() => EntityOpenRequested?.Invoke(EntityType.Lore, lore)));
            })
            .ToList();

    private ContextSidebarSceneAnalysisViewModel BuildSceneAnalysis(
        ContextSidebarChapterSnapshot? chapterSnapshot,
        ChapterData chapter,
        SceneData scene,
        string sceneContent,
        IReadOnlyList<string> povOptions)
    {
        var currentSceneSnapshot = chapterSnapshot?.Scenes.FirstOrDefault(candidate => string.Equals(candidate.Scene.Id, scene.Id, StringComparison.OrdinalIgnoreCase));
        var analysisContent = currentSceneSnapshot?.Content ?? sceneContent;
        var currentSceneCharacters = MatchSources(analysisContent, _characterSources);
        var currentSceneLocations = MatchSources(analysisContent, _locationSources);
        var currentSceneItems = MatchSources(analysisContent, _itemSources);
        var currentSceneLore = MatchSources(analysisContent, _loreSources);

        var wordCount = CountWords(analysisContent);
        var dialogueRatio = ComputeDialogueRatio(analysisContent, wordCount);
        var avgSentenceLength = ComputeAverageSentenceLength(analysisContent, wordCount);
        var autoIntensity = ComputeIntensity(analysisContent);
        var autoEmotion = DetectEmotion(analysisContent, autoIntensity);
        var autoPov = DetectPov(analysisContent, currentSceneCharacters, chapter, scene);
        var autoConflict = ExtractConflictSnippet(analysisContent);
        var autoTags = BuildSceneTags(
            analysisContent,
            currentSceneCharacters.Count,
            currentSceneLocations.Count,
            currentSceneItems.Count,
            currentSceneLore.Count,
            autoIntensity,
            autoEmotion.Key,
            dialogueRatio,
            wordCount,
            autoConflict);

        var overrides = scene.AnalysisOverrides;
        var pov = overrides?.Pov ?? autoPov;
        var emotion = overrides?.Emotion ?? autoEmotion.Key;
        var intensity = overrides?.Intensity ?? autoIntensity;
        var conflict = overrides?.Conflict ?? autoConflict;
        var tags = overrides?.Tags != null ? [.. overrides.Tags] : autoTags;

        var sparkValues = chapterSnapshot?.Scenes.Count > 0
            ? chapterSnapshot.Scenes.Select(GetEffectiveIntensity).ToList()
            : [intensity];
        var currentIndex = chapterSnapshot?.Scenes.FindIndex(candidate => string.Equals(candidate.Scene.Id, scene.Id, StringComparison.OrdinalIgnoreCase)) ?? 0;
        var sparkline = BuildSparkline(sparkValues, currentIndex);

        return new ContextSidebarSceneAnalysisViewModel(
            pov,
            emotion,
            intensity,
            conflict,
            tags,
            wordCount,
            dialogueRatio,
            avgSentenceLength,
            sparkline,
            overrides?.Pov != null,
            overrides?.Emotion != null,
            overrides?.Intensity.HasValue == true,
            overrides?.Conflict != null,
            overrides?.Tags != null,
            povOptions,
            EmotionKeys,
            value => SaveSceneAnalysisOverrideAsync(candidate => candidate.Pov = value.Trim()),
            value => SaveSceneAnalysisOverrideAsync(candidate => candidate.Emotion = value.Trim()),
            value => SaveSceneAnalysisOverrideAsync(candidate => candidate.Intensity = Math.Clamp(value, -10, 10)),
            value => SaveSceneAnalysisOverrideAsync(candidate => candidate.Conflict = value.Trim()),
            value => SaveSceneAnalysisOverrideAsync(candidate => candidate.Tags = [.. value]),
            () => SaveSceneAnalysisOverrideAsync(candidate => candidate.Pov = null),
            () => SaveSceneAnalysisOverrideAsync(candidate => candidate.Emotion = null),
            () => SaveSceneAnalysisOverrideAsync(candidate => candidate.Intensity = null),
            () => SaveSceneAnalysisOverrideAsync(candidate => candidate.Conflict = null),
            () => SaveSceneAnalysisOverrideAsync(candidate => candidate.Tags = null));
    }

    private IReadOnlyList<string> BuildPovOptions(
        IReadOnlyList<ContextSidebarMatchedSource> matchedCharacters,
        ChapterData chapter,
        SceneData scene)
    {
        var chapterNames = matchedCharacters
            .Select(match => match.Source.Entity)
            .OfType<CharacterData>()
            .Select(character => ResolveCharacterDisplay(character, chapter, scene).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chapterNames.Count > 0)
            return chapterNames;

        return _characterSources
            .Select(source => source.Entity)
            .OfType<CharacterData>()
            .Select(character => ResolveCharacterDisplay(character, chapter, scene).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task SaveSceneAnalysisOverrideAsync(Action<SceneAnalysisOverrides> updateOverrides)
    {
        if (_editor?.CurrentChapter == null || _editor.CurrentScene == null)
            return;

        var scene = _editor.CurrentScene;
        var overrides = scene.AnalysisOverrides?.Clone() ?? new SceneAnalysisOverrides();
        updateOverrides(overrides);

        await _projectService.SetSceneAnalysisOverridesAsync(
            _editor.CurrentChapter.Guid,
            scene.Id,
            overrides.HasValues ? overrides : null);

        RefreshContext();
    }

    private static int GetEffectiveIntensity(ContextSidebarSceneSnapshot snapshot)
        => snapshot.Scene.AnalysisOverrides?.Intensity ?? ComputeIntensity(snapshot.Content);

    private CharacterDisplaySnapshot ResolveCharacterDisplay(CharacterData character, ChapterData chapter, SceneData scene)
    {
        var match = character.ChapterOverrides.FirstOrDefault(overrideEntry =>
            (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
             || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
            && string.Equals(overrideEntry.Scene, scene.Title, StringComparison.OrdinalIgnoreCase))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(overrideEntry.Scene)
                && string.IsNullOrWhiteSpace(overrideEntry.Act))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                string.Equals(overrideEntry.Act, chapter.Act, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(overrideEntry.Chapter)
                && string.IsNullOrWhiteSpace(overrideEntry.Scene));

        var displayName = string.IsNullOrWhiteSpace(match?.Name) ? character.Name : match.Name!;
        var displaySurname = string.IsNullOrWhiteSpace(match?.Surname) ? character.Surname : match.Surname!;
        var displayAge = ResolveDisplayAge(character, match, chapter, scene);

        return new CharacterDisplaySnapshot(
            string.IsNullOrWhiteSpace(displaySurname) ? displayName : $"{displayName} {displaySurname}".Trim(),
            string.IsNullOrWhiteSpace(match?.Role) ? character.Role : match.Role!,
            string.IsNullOrWhiteSpace(match?.Gender) ? character.Gender : match.Gender!,
            displayAge,
            character.Group);
    }

    private static IReadOnlyList<ContextSidebarMatchedSource> MatchSources(string content, IEnumerable<ContextSidebarEntitySource> sources)
        => sources
            .Select(source => new ContextSidebarMatchedSource(source, source.FindFirstMatchIndex(content)))
            .Where(entry => entry.MatchIndex.HasValue)
            .OrderBy(entry => entry.MatchIndex)
            .ThenBy(entry => entry.Source.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildContextSubtitle(ChapterData chapter, int sceneIndex, int totalScenes)
    {
        if (string.IsNullOrWhiteSpace(chapter.Title))
            return totalScenes > 0 ? Loc.T("context.sceneOf", sceneIndex, totalScenes) : string.Empty;

        return totalScenes > 0
            ? Loc.T("context.sceneOfChapter", chapter.Title.Trim(), sceneIndex, totalScenes)
            : chapter.Title.Trim();
    }

    private bool NeedsSnapshotReload(string chapterGuid, IReadOnlyList<SceneData> scenes)
    {
        var orderedChapters = _projectService.GetChaptersOrdered();
        if (_chapterSnapshots.Count != orderedChapters.Count)
            return true;

        if (!_chapterSnapshots.TryGetValue(chapterGuid, out var snapshot))
            return true;

        return !SnapshotMatches(snapshot, scenes);
    }

    private static bool SnapshotMatches(ContextSidebarChapterSnapshot snapshot, IReadOnlyList<SceneData> scenes)
    {
        if (snapshot.Scenes.Count != scenes.Count)
            return false;

        for (var index = 0; index < scenes.Count; index++)
        {
            if (!string.Equals(snapshot.Scenes[index].Scene.Id, scenes[index].Id, StringComparison.OrdinalIgnoreCase)
                || snapshot.Scenes[index].Scene.Order != scenes[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateCurrentSceneSnapshot(ChapterData chapter, SceneData scene, string content)
    {
        if (_chapterSnapshots.TryGetValue(chapter.Guid, out var snapshot))
            snapshot.UpdateSceneContent(scene.Id, content);
    }

    private static string DetectPov(
        string content,
        IReadOnlyList<ContextSidebarMatchedSource> currentSceneCharacters,
        ChapterData chapter,
        SceneData scene)
    {
        if (currentSceneCharacters.Count == 0)
        {
            return FirstPersonRegex.Matches(content).Count >= 4
                ? Loc.T("pov.firstPerson")
                : string.Empty;
        }

        var bestMatch = currentSceneCharacters
            .Select(match => new
            {
                Match = match,
                Count = match.Source.FindMentionCount(content)
            })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Match.MatchIndex)
            .FirstOrDefault();

        if (bestMatch?.Match.Source.Entity is CharacterData character)
        {
            var display = ResolveStaticCharacterDisplay(character, chapter, scene);
            return display.Name;
        }

        return string.Empty;
    }

    private static SceneEmotionSnapshot DetectEmotion(string content, int intensity)
    {
        var normalized = content.ToLowerInvariant();

        var best = EmotionProfiles
            .Select(profile => new SceneEmotionSnapshot(
                profile.Key,
                profile.Label,
                profile.Keywords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal))))
            .OrderByDescending(entry => entry.Score)
            .FirstOrDefault();

        if (best == null || best.Score <= 0)
        {
            return intensity switch
            {
                <= -6 => new SceneEmotionSnapshot("tense", "tense", 1),
                >= 6 => new SceneEmotionSnapshot("triumphant", "triumphant", 1),
                _ => new SceneEmotionSnapshot("neutral", "neutral", 1)
            };
        }

        return best;
    }

    private static int ComputeIntensity(string content)
    {
        var normalized = content.ToLowerInvariant();
        var positiveCount = PositiveWords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var negativeCount = NegativeWords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var conflictCount = ConflictKeywords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var exclamations = content.Count(character => character == '!');

        var score = ((positiveCount - negativeCount) * 2) - conflictCount;
        if (score > 0)
            score += Math.Min(2, exclamations);
        else if (score < 0)
            score -= Math.Min(2, exclamations);
        else if (conflictCount > 0)
            score = -Math.Min(6, conflictCount + exclamations);

        return Math.Clamp(score, -10, 10);
    }

    private static string ExtractConflictSnippet(string content)
    {
        foreach (var sentence in ExtractSentences(content))
        {
            var normalized = sentence.ToLowerInvariant();
            if (!ConflictKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
                continue;

            return TrimExcerpt(sentence, 92);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildSceneTags(
        string content,
        int characterCount,
        int locationCount,
        int itemCount,
        int loreCount,
        int intensity,
        string emotionLabel,
        double dialogueRatio,
        int wordCount,
        string conflict)
    {
        var tags = new List<string>();

        if (dialogueRatio >= 0.35)
            tags.Add(Loc.T("sceneTag.dialogue"));

        if (Math.Abs(intensity) >= 6)
            tags.Add(Loc.T("sceneTag.highTension"));

        if (!string.IsNullOrWhiteSpace(conflict))
            tags.Add(Loc.T("sceneTag.conflict"));

        if (characterCount >= 3)
            tags.Add(Loc.T("sceneTag.ensemble"));

        if (locationCount >= 2)
            tags.Add(Loc.T("sceneTag.travel"));

        if (itemCount + loreCount >= 2)
            tags.Add(Loc.T("sceneTag.worldbuilding"));

        if (FirstPersonRegex.Matches(content).Count >= 4)
            tags.Add(Loc.T("sceneTag.interior"));

        if (wordCount >= 1200)
            tags.Add(Loc.T("sceneTag.longScene"));

        if (!string.Equals(emotionLabel, "neutral", StringComparison.OrdinalIgnoreCase))
            tags.Add(Loc.T($"emotion.{emotionLabel}"));

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static double ComputeDialogueRatio(string content, int wordCount)
    {
        if (wordCount <= 0)
            return 0;

        var dialogueChars = DialogueRegex.Matches(content)
            .Select(match => match.Length)
            .Sum();

        var totalChars = Regex.Replace(content, "\\s+", " ").Length;
        if (totalChars <= 0)
            return 0;

        return Math.Clamp(dialogueChars / (double)totalChars, 0, 1);
    }

    private static double ComputeAverageSentenceLength(string content, int wordCount)
    {
        if (wordCount <= 0)
            return 0;

        var sentenceCount = Math.Max(1, SentenceRegex.Matches(content).Count);
        return Math.Round(wordCount / (double)sentenceCount, 1);
    }

    private static ContextSidebarSparklineViewModel BuildSparkline(IReadOnlyList<int> values, int currentIndex)
    {
        const double width = 180;
        const double height = 44;
        const double padding = 5;
        const double maxRadius = 5;
        const double minRadius = 3;

        if (values.Count == 0)
        {
            return new ContextSidebarSparklineViewModel(width, height, string.Empty, string.Empty, []);
        }

        var zeroY = height / 2d;
        var points = new List<(double X, double Y)>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var x = values.Count == 1
                ? width / 2d
                : padding + ((width - (padding * 2d)) * index / (values.Count - 1d));
            var y = zeroY - (Math.Clamp(values[index], -10, 10) / 10d) * ((height / 2d) - 4d);
            points.Add((x, y));
        }

        var lineData = points.Count > 1
            ? $"M {points[0].X:0.##},{points[0].Y:0.##} "
              + string.Join(" ", points.Skip(1).Select(point => $"L {point.X:0.##},{point.Y:0.##}"))
            : string.Empty;

        var dotItems = points
            .Select((point, index) =>
            {
                var isCurrent = index == Math.Clamp(currentIndex, 0, values.Count - 1);
                var diameter = isCurrent ? maxRadius * 2d : minRadius * 2d;

                return new ContextSidebarSparkPointViewModel(
                    point.X - (diameter / 2d),
                    point.Y - (diameter / 2d),
                    diameter,
                    isCurrent);
            })
            .ToList();

        return new ContextSidebarSparklineViewModel(
            width,
            height,
            $"M 0,{zeroY:0.##} L {width:0.##},{zeroY:0.##}",
            lineData,
            dotItems);
    }

    private static IEnumerable<string> ExtractSentences(string content)
        => SentenceRegex.Matches(content)
            .Select(match => match.Value.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence));

    private static int CountWords(string content)
        => WordRegex.Matches(content).Count;

    private static string NormalizeSceneContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        if (!content.TrimStart().StartsWith('<'))
            return content;

        var text = Regex.Replace(content, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    private static string TrimExcerpt(string value, int maxLength)
    {
        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private void ClearContext()
    {
        IsContextAvailable = false;
        IsContextLoading = false;
        ContextLabel = string.Empty;
        ContextSubtitle = string.Empty;
        ContextDate = string.Empty;
        CharacterCards = [];
        MentionRows = [];
        LocationCards = [];
        ItemCards = [];
        LoreCards = [];
        SceneAnalysis = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasContextDate));
        OnPropertyChanged(nameof(ContextDateDisplay));
        OnPropertyChanged(nameof(HasContextSubtitle));
        OnPropertyChanged(nameof(HasCharacterCards));
        OnPropertyChanged(nameof(HasMentionFrequency));
        OnPropertyChanged(nameof(HasLocationCards));
        OnPropertyChanged(nameof(HasItemCards));
        OnPropertyChanged(nameof(HasLoreCards));
        OnPropertyChanged(nameof(HasSceneAnalysis));
        OnPropertyChanged(nameof(HasAnyContent));
        OnPropertyChanged(nameof(ContextStatusMessage));
    }

    private static CharacterDisplaySnapshot ResolveStaticCharacterDisplay(CharacterData character, ChapterData chapter, SceneData scene)
    {
        var match = character.ChapterOverrides.FirstOrDefault(overrideEntry =>
            (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
             || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
            && string.Equals(overrideEntry.Scene, scene.Title, StringComparison.OrdinalIgnoreCase))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(overrideEntry.Scene)
                && string.IsNullOrWhiteSpace(overrideEntry.Act))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                string.Equals(overrideEntry.Act, chapter.Act, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(overrideEntry.Chapter)
                && string.IsNullOrWhiteSpace(overrideEntry.Scene));

        var displayName = string.IsNullOrWhiteSpace(match?.Name) ? character.Name : match.Name!;
        var displaySurname = string.IsNullOrWhiteSpace(match?.Surname) ? character.Surname : match.Surname!;
        var displayAge = ResolveDisplayAge(character, match, chapter, scene);

        return new CharacterDisplaySnapshot(
            string.IsNullOrWhiteSpace(displaySurname) ? displayName : $"{displayName} {displaySurname}".Trim(),
            string.IsNullOrWhiteSpace(match?.Role) ? character.Role : match.Role!,
            string.IsNullOrWhiteSpace(match?.Gender) ? character.Gender : match.Gender!,
            displayAge,
            character.Group);
    }

    private static string ResolveDisplayAge(CharacterData character, CharacterOverride? match, ChapterData chapter, SceneData scene)
    {
        if (string.Equals(character.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(character.BirthDate))
        {
            var referenceDate = !string.IsNullOrWhiteSpace(scene.Date) ? scene.Date
                : !string.IsNullOrWhiteSpace(chapter.Date) ? chapter.Date
                : null;
            var computed = AgeComputation.ComputeAge(character.BirthDate, referenceDate,
                character.AgeIntervalUnit ?? IntervalUnit.Years);
            if (!string.IsNullOrWhiteSpace(computed))
                return computed;
        }

        return string.IsNullOrWhiteSpace(match?.Age) ? character.Age : match.Age!;
    }

    private static IEnumerable<string> GetCharacterAliases(CharacterData character)
    {
        yield return character.DisplayName;

        if (!string.Equals(character.Name, character.DisplayName, StringComparison.OrdinalIgnoreCase))
            yield return character.Name;
    }

    private static IReadOnlyList<Regex> BuildPatterns(IEnumerable<string> aliases)
        => aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(NormalizeEntityReference)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(alias => new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
            .ToList();

    private static void AddPill(ICollection<ContextSidebarPillViewModel> pills, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            pills.Add(new ContextSidebarPillViewModel(label, value.Trim()));
    }

    private static string NormalizeEntityReference(string? value)
        => (value ?? string.Empty)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string TrimDescription(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= 180)
            return normalized;

        return normalized[..177].TrimEnd() + "...";
    }

    partial void OnContextDateChanged(string value)
        => NotifyStateChanged();

    partial void OnContextSubtitleChanged(string value)
        => NotifyStateChanged();

    partial void OnCharacterCardsChanged(ObservableCollection<ContextSidebarEntityCardViewModel> value)
        => NotifyStateChanged();

    partial void OnMentionRowsChanged(ObservableCollection<ContextSidebarMentionRowViewModel> value)
        => NotifyStateChanged();

    partial void OnLocationCardsChanged(ObservableCollection<ContextSidebarEntityCardViewModel> value)
        => NotifyStateChanged();

    partial void OnItemCardsChanged(ObservableCollection<ContextSidebarEntityCardViewModel> value)
        => NotifyStateChanged();

    partial void OnLoreCardsChanged(ObservableCollection<ContextSidebarEntityCardViewModel> value)
        => NotifyStateChanged();

    partial void OnSceneAnalysisChanged(ContextSidebarSceneAnalysisViewModel? value)
        => NotifyStateChanged();

    partial void OnIsContextLoadingChanged(bool value)
        => NotifyStateChanged();

    private sealed record CharacterDisplaySnapshot(string Name, string Role, string Gender, string Age, string Group);
    private sealed record EmotionProfile(string Key, string Label, IReadOnlyList<string> Keywords);
    private sealed record SceneEmotionSnapshot(string Key, string Label, int Score);
}

public sealed class ContextSidebarEntityCardViewModel
{
    public ContextSidebarEntityCardViewModel(
        string title,
        string primaryBadge,
        string secondaryBadge,
        string description,
        string infoText,
        IReadOnlyList<ContextSidebarPillViewModel> pills,
        IRelayCommand openCommand)
    {
        Title = title;
        PrimaryBadge = primaryBadge;
        SecondaryBadge = secondaryBadge;
        Description = description;
        InfoText = infoText;
        Pills = new ObservableCollection<ContextSidebarPillViewModel>(pills);
        OpenCommand = openCommand;
    }

    public string Title { get; }
    public string PrimaryBadge { get; }
    public string SecondaryBadge { get; }
    public string Description { get; }
    public string InfoText { get; }
    public ObservableCollection<ContextSidebarPillViewModel> Pills { get; }
    public IRelayCommand OpenCommand { get; }
    public bool HasPrimaryBadge => !string.IsNullOrWhiteSpace(PrimaryBadge);
    public bool HasSecondaryBadge => !string.IsNullOrWhiteSpace(SecondaryBadge);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasInfoText => !string.IsNullOrWhiteSpace(InfoText);
    public bool HasPills => Pills.Count > 0;
}

public sealed class ContextSidebarPillViewModel
{
    public ContextSidebarPillViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}

public sealed class ContextSidebarMentionRowViewModel
{
    public ContextSidebarMentionRowViewModel(
        string name,
        string gapWarning,
        IReadOnlyList<ContextSidebarMentionCellViewModel> cells,
        IRelayCommand openCommand)
    {
        Name = name;
        GapWarning = gapWarning;
        Cells = new ObservableCollection<ContextSidebarMentionCellViewModel>(cells);
        OpenCommand = openCommand;
    }

    public string Name { get; }
    public string GapWarning { get; }
    public ObservableCollection<ContextSidebarMentionCellViewModel> Cells { get; }
    public IRelayCommand OpenCommand { get; }
    public bool HasGapWarning => !string.IsNullOrWhiteSpace(GapWarning);
}

public sealed class ContextSidebarMentionCellViewModel
{
    public ContextSidebarMentionCellViewModel(string label, bool isPresent, bool isCurrentChapter, string toolTip)
    {
        Label = label;
        IsPresent = isPresent;
        IsCurrentChapter = isCurrentChapter;
        ToolTip = toolTip;
    }

    public string Label { get; }
    public bool IsPresent { get; }
    public bool IsCurrentChapter { get; }
    public string ToolTip { get; }
    public bool IsAbsent => !IsPresent;
}

public partial class ContextSidebarSceneAnalysisViewModel : ObservableObject
{
    private readonly Func<string, Task> _savePovAsync;
    private readonly Func<string, Task> _saveEmotionAsync;
    private readonly Func<int, Task> _saveIntensityAsync;
    private readonly Func<string, Task> _saveConflictAsync;
    private readonly Func<IReadOnlyList<string>, Task> _saveTagsAsync;
    private readonly Dictionary<string, string> _emotionKeyToDisplay;
    private readonly Dictionary<string, string> _emotionDisplayToKey;
    private readonly Func<Task> _resetPovAsync;
    private readonly Func<Task> _resetEmotionAsync;
    private readonly Func<Task> _resetIntensityAsync;
    private readonly Func<Task> _resetConflictAsync;
    private readonly Func<Task> _resetTagsAsync;

    [ObservableProperty]
    private bool _isEditingPov;

    [ObservableProperty]
    private bool _isEditingEmotion;

    [ObservableProperty]
    private bool _isEditingIntensity;

    [ObservableProperty]
    private bool _isEditingConflict;

    [ObservableProperty]
    private bool _isEditingTags;

    [ObservableProperty]
    private string _povInput = string.Empty;

    [ObservableProperty]
    private string _selectedEmotion = string.Empty;

    [ObservableProperty]
    private string _intensityInput = string.Empty;

    [ObservableProperty]
    private string _conflictInput = string.Empty;

    [ObservableProperty]
    private string _tagsInput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _povSuggestions = [];

    [ObservableProperty]
    private bool _isPovSuggestionOpen;

    public ContextSidebarSceneAnalysisViewModel(
        string pov,
        string emotion,
        int intensity,
        string conflict,
        IReadOnlyList<string> tags,
        int wordCount,
        double dialogueRatio,
        double averageSentenceLength,
        ContextSidebarSparklineViewModel sparkline,
        bool hasPovOverride,
        bool hasEmotionOverride,
        bool hasIntensityOverride,
        bool hasConflictOverride,
        bool hasTagsOverride,
        IReadOnlyList<string> povOptions,
        IReadOnlyList<string> emotionOptions,
        Func<string, Task> savePovAsync,
        Func<string, Task> saveEmotionAsync,
        Func<int, Task> saveIntensityAsync,
        Func<string, Task> saveConflictAsync,
        Func<IReadOnlyList<string>, Task> saveTagsAsync,
        Func<Task> resetPovAsync,
        Func<Task> resetEmotionAsync,
        Func<Task> resetIntensityAsync,
        Func<Task> resetConflictAsync,
        Func<Task> resetTagsAsync)
    {
        Pov = pov ?? string.Empty;
        Emotion = string.IsNullOrWhiteSpace(emotion) ? "neutral" : emotion;
        Intensity = intensity;
        Conflict = conflict ?? string.Empty;
        Tags = new ObservableCollection<string>(tags);
        WordCountDisplay = Loc.T("context.wordsDisplay", wordCount);
        DialogueDisplay = Loc.T("context.dialogueDisplay", Math.Round(dialogueRatio * 100d));
        AverageSentenceDisplay = Loc.T("context.avgSentenceDisplay", averageSentenceLength.ToString("0.#"));
        Sparkline = sparkline;
        HasPovOverride = hasPovOverride;
        HasEmotionOverride = hasEmotionOverride;
        HasIntensityOverride = hasIntensityOverride;
        HasConflictOverride = hasConflictOverride;
        HasTagsOverride = hasTagsOverride;
        PovOptions = new ObservableCollection<string>(povOptions);

        _emotionKeyToDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _emotionDisplayToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in emotionOptions)
        {
            var display = Loc.T($"emotion.{key}");
            _emotionKeyToDisplay[key] = display;
            _emotionDisplayToKey[display] = key;
        }
        EmotionOptions = new ObservableCollection<string>(_emotionKeyToDisplay.Values);

        _savePovAsync = savePovAsync;
        _saveEmotionAsync = saveEmotionAsync;
        _saveIntensityAsync = saveIntensityAsync;
        _saveConflictAsync = saveConflictAsync;
        _saveTagsAsync = saveTagsAsync;
        _resetPovAsync = resetPovAsync;
        _resetEmotionAsync = resetEmotionAsync;
        _resetIntensityAsync = resetIntensityAsync;
        _resetConflictAsync = resetConflictAsync;
        _resetTagsAsync = resetTagsAsync;

        PovInput = Pov;
        SelectedEmotion = EmotionDisplay;
        IntensityInput = intensity.ToString();
        ConflictInput = Conflict;
        TagsInput = string.Join(", ", Tags);

        const double totalWidth = 84d;
        const double halfWidth = totalWidth / 2d;
        var normalized = Math.Clamp(Math.Abs(intensity) / 10d, 0d, 1d);
        IntensityBarWidth = normalized * halfWidth;
        IntensityBarLeft = intensity < 0 ? halfWidth - IntensityBarWidth : halfWidth;

        BeginEditPovCommand = new RelayCommand(BeginEditPov);
        SavePovCommand = new AsyncRelayCommand(SavePovAsync);
        CancelEditPovCommand = new RelayCommand(CancelAllEdits);
        ResetPovCommand = new AsyncRelayCommand(ResetPovAsync);

        BeginEditEmotionCommand = new RelayCommand(BeginEditEmotion);
        SaveEmotionCommand = new AsyncRelayCommand(SaveEmotionAsync);
        CancelEditEmotionCommand = new RelayCommand(CancelAllEdits);
        ResetEmotionCommand = new AsyncRelayCommand(ResetEmotionAsync);

        BeginEditIntensityCommand = new RelayCommand(BeginEditIntensity);
        SaveIntensityCommand = new AsyncRelayCommand(SaveIntensityAsync);
        CancelEditIntensityCommand = new RelayCommand(CancelAllEdits);
        ResetIntensityCommand = new AsyncRelayCommand(ResetIntensityAsync);

        BeginEditConflictCommand = new RelayCommand(BeginEditConflict);
        SaveConflictCommand = new AsyncRelayCommand(SaveConflictAsync);
        CancelEditConflictCommand = new RelayCommand(CancelAllEdits);
        ResetConflictCommand = new AsyncRelayCommand(ResetConflictAsync);

        BeginEditTagsCommand = new RelayCommand(BeginEditTags);
        SaveTagsCommand = new AsyncRelayCommand(SaveTagsAsync);
        CancelEditTagsCommand = new RelayCommand(CancelAllEdits);
        ResetTagsCommand = new AsyncRelayCommand(ResetTagsAsync);
    }

    public string Pov { get; }
    public string Emotion { get; }
    public int Intensity { get; }
    public string Conflict { get; }
    public ObservableCollection<string> Tags { get; }
    public ObservableCollection<string> PovOptions { get; }
    public ObservableCollection<string> EmotionOptions { get; }
    public string WordCountDisplay { get; }
    public string DialogueDisplay { get; }
    public string AverageSentenceDisplay { get; }
    public ContextSidebarSparklineViewModel Sparkline { get; }
    public bool HasPovOverride { get; }
    public bool HasEmotionOverride { get; }
    public bool HasIntensityOverride { get; }
    public bool HasConflictOverride { get; }
    public bool HasTagsOverride { get; }
    public string PovDisplay => string.IsNullOrWhiteSpace(Pov) ? Loc.T("pov.unknown") : Pov;
    public string EmotionDisplay => _emotionKeyToDisplay.TryGetValue(Emotion, out var display) ? display : Loc.T("emotion.neutral");
    public string IntensityDisplay => Intensity > 0 ? $"+{Intensity}" : Intensity.ToString();
    public double IntensityBarWidth { get; }
    public double IntensityBarLeft { get; }
    public bool IsPositiveIntensity => Intensity > 0;
    public bool IsNegativeIntensity => Intensity < 0;
    public bool HasConflict => !string.IsNullOrWhiteSpace(Conflict);
    public bool HasTags => Tags.Count > 0;
    public bool HasPovSuggestions => PovSuggestions.Count > 0;
    public bool PovSuggestionsVisible => IsEditingPov && IsPovSuggestionOpen && HasPovSuggestions;
    public string ConflictDisplay => HasConflict ? Conflict : Loc.T("context.none");
    public bool SuppressPovLostFocusCommit { get; set; }

    public IRelayCommand BeginEditPovCommand { get; }
    public IAsyncRelayCommand SavePovCommand { get; }
    public IRelayCommand CancelEditPovCommand { get; }
    public IAsyncRelayCommand ResetPovCommand { get; }

    public IRelayCommand BeginEditEmotionCommand { get; }
    public IAsyncRelayCommand SaveEmotionCommand { get; }
    public IRelayCommand CancelEditEmotionCommand { get; }
    public IAsyncRelayCommand ResetEmotionCommand { get; }

    public IRelayCommand BeginEditIntensityCommand { get; }
    public IAsyncRelayCommand SaveIntensityCommand { get; }
    public IRelayCommand CancelEditIntensityCommand { get; }
    public IAsyncRelayCommand ResetIntensityCommand { get; }

    public IRelayCommand BeginEditConflictCommand { get; }
    public IAsyncRelayCommand SaveConflictCommand { get; }
    public IRelayCommand CancelEditConflictCommand { get; }
    public IAsyncRelayCommand ResetConflictCommand { get; }

    public IRelayCommand BeginEditTagsCommand { get; }
    public IAsyncRelayCommand SaveTagsCommand { get; }
    public IRelayCommand CancelEditTagsCommand { get; }
    public IAsyncRelayCommand ResetTagsCommand { get; }

    private void BeginEditPov()
    {
        CancelAllEdits();
        PovInput = Pov;
        IsEditingPov = true;
        UpdatePovSuggestions(PovInput);
    }

    private async Task SavePovAsync()
    {
        var value = PovInput.Trim();
        HidePovSuggestions();
        if (string.Equals(value, Pov, StringComparison.Ordinal))
        {
            CancelAllEdits();
            return;
        }

        await _savePovAsync(value);
    }

    private async Task ResetPovAsync()
    {
        await _resetPovAsync();
    }

    private void BeginEditEmotion()
    {
        CancelAllEdits();
        SelectedEmotion = EmotionDisplay;
        IsEditingEmotion = true;
    }

    private async Task SaveEmotionAsync()
    {
        var displayValue = (SelectedEmotion ?? string.Empty).Trim();
        var key = _emotionDisplayToKey.TryGetValue(displayValue, out var k) ? k : displayValue;
        if (string.Equals(key, Emotion, StringComparison.Ordinal))
        {
            CancelAllEdits();
            return;
        }

        await _saveEmotionAsync(key);
    }

    private async Task ResetEmotionAsync()
    {
        await _resetEmotionAsync();
    }

    private void BeginEditIntensity()
    {
        CancelAllEdits();
        IntensityInput = Intensity.ToString();
        IsEditingIntensity = true;
    }

    private async Task SaveIntensityAsync()
    {
        var parsed = int.TryParse(IntensityInput, out var intensityValue)
            ? intensityValue
            : Intensity;

        parsed = Math.Clamp(parsed, -10, 10);
        if (parsed == Intensity)
        {
            CancelAllEdits();
            return;
        }

        await _saveIntensityAsync(parsed);
    }

    private async Task ResetIntensityAsync()
    {
        await _resetIntensityAsync();
    }

    private void BeginEditConflict()
    {
        CancelAllEdits();
        ConflictInput = Conflict;
        IsEditingConflict = true;
    }

    private async Task SaveConflictAsync()
    {
        var value = ConflictInput.Trim();
        if (string.Equals(value, Conflict, StringComparison.Ordinal))
        {
            CancelAllEdits();
            return;
        }

        await _saveConflictAsync(value);
    }

    private async Task ResetConflictAsync()
    {
        await _resetConflictAsync();
    }

    private void BeginEditTags()
    {
        CancelAllEdits();
        TagsInput = string.Join(", ", Tags);
        IsEditingTags = true;
    }

    private async Task SaveTagsAsync()
    {
        var parsed = TagsInput
            .Split(',', StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parsed.SequenceEqual(Tags, StringComparer.OrdinalIgnoreCase))
        {
            CancelAllEdits();
            return;
        }

        await _saveTagsAsync(parsed);
    }

    private async Task ResetTagsAsync()
    {
        await _resetTagsAsync();
    }

    private void CancelAllEdits()
    {
        IsEditingPov = false;
        IsEditingEmotion = false;
        IsEditingIntensity = false;
        IsEditingConflict = false;
        IsEditingTags = false;
        SuppressPovLostFocusCommit = false;
        HidePovSuggestions();
    }

    public void UpdatePovSuggestions(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            HidePovSuggestions();
            PovSuggestions = [];
            return;
        }

        var filtered = PovOptions
            .Where(option => option.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        PovSuggestions = new ObservableCollection<string>(filtered);
        IsPovSuggestionOpen = PovSuggestions.Count > 0;
        OnPropertyChanged(nameof(HasPovSuggestions));
        OnPropertyChanged(nameof(PovSuggestionsVisible));
    }

    public void HidePovSuggestions()
    {
        IsPovSuggestionOpen = false;
        OnPropertyChanged(nameof(PovSuggestionsVisible));
    }

    public async Task CommitPovAsync()
        => await SavePovAsync();

    public async Task CommitEmotionAsync()
        => await SaveEmotionAsync();

    public async Task CommitIntensityAsync()
        => await SaveIntensityAsync();

    public async Task CommitConflictAsync()
        => await SaveConflictAsync();

    public async Task CommitTagsAsync()
        => await SaveTagsAsync();

    public void CancelEditing()
        => CancelAllEdits();

    public async Task SelectPovSuggestionAsync(string suggestion)
    {
        PovInput = suggestion;
        await SavePovAsync();
    }

    partial void OnPovSuggestionsChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(HasPovSuggestions));
        OnPropertyChanged(nameof(PovSuggestionsVisible));
    }

    partial void OnIsPovSuggestionOpenChanged(bool value)
        => OnPropertyChanged(nameof(PovSuggestionsVisible));

    partial void OnIsEditingPovChanged(bool value)
        => OnPropertyChanged(nameof(PovSuggestionsVisible));
}

public sealed class ContextSidebarSparklineViewModel
{
    public ContextSidebarSparklineViewModel(
        double width,
        double height,
        string zeroLineData,
        string lineData,
        IReadOnlyList<ContextSidebarSparkPointViewModel> sparkPoints)
    {
        Width = width;
        Height = height;
        ZeroLineData = zeroLineData;
        LineData = lineData;
        SparkPoints = new ObservableCollection<ContextSidebarSparkPointViewModel>(sparkPoints);
    }

    public double Width { get; }
    public double Height { get; }
    public string ZeroLineData { get; }
    public string LineData { get; }
    public ObservableCollection<ContextSidebarSparkPointViewModel> SparkPoints { get; }
    public bool HasLine => !string.IsNullOrWhiteSpace(LineData);
    public bool HasSparkPoints => SparkPoints.Count > 0;
}

public sealed class ContextSidebarSparkPointViewModel
{
    public ContextSidebarSparkPointViewModel(double left, double top, double diameter, bool isCurrent)
    {
        Left = left;
        Top = top;
        Diameter = diameter;
        IsCurrent = isCurrent;
    }

    public double Left { get; }
    public double Top { get; }
    public double Diameter { get; }
    public bool IsCurrent { get; }
}

internal sealed class ContextSidebarEntitySource
{
    public ContextSidebarEntitySource(EntityType type, object entity, IReadOnlyList<Regex> patterns)
    {
        Type = type;
        Entity = entity;
        Patterns = patterns;
    }

    public EntityType Type { get; }
    public object Entity { get; }
    public IReadOnlyList<Regex> Patterns { get; }

    public string SortKey => Entity switch
    {
        CharacterData character => character.DisplayName,
        LocationData location => location.Name,
        ItemData item => item.Name,
        LoreData lore => lore.Name,
        _ => string.Empty
    };

    public bool IsMatch(string content) => FindFirstMatchIndex(content).HasValue;

    public int? FindFirstMatchIndex(string content)
    {
        int? bestIndex = null;
        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(content);
            if (!match.Success)
                continue;

            if (!bestIndex.HasValue || match.Index < bestIndex.Value)
                bestIndex = match.Index;
        }

        return bestIndex;
    }

    public int FindMentionCount(string content)
        => Patterns.Count == 0
            ? 0
            : Patterns.Max(pattern => pattern.Matches(content).Count);
}

internal sealed record ContextSidebarMatchedSource(ContextSidebarEntitySource Source, int? MatchIndex);

internal sealed class ContextSidebarChapterSnapshot
{
    public ContextSidebarChapterSnapshot(ChapterData chapter, IReadOnlyList<ContextSidebarSceneSnapshot> scenes)
    {
        Chapter = chapter;
        Scenes = scenes.ToList();
        RebuildAggregateText();
    }

    public ChapterData Chapter { get; }
    public List<ContextSidebarSceneSnapshot> Scenes { get; }
    public string AggregateText { get; private set; } = string.Empty;

    public void UpdateSceneContent(string sceneId, string content)
    {
        var scene = Scenes.FirstOrDefault(candidate => string.Equals(candidate.Scene.Id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene == null)
            return;

        scene.Content = content;
        RebuildAggregateText();
    }

    private void RebuildAggregateText()
    {
        AggregateText = string.Join(
            Environment.NewLine + Environment.NewLine,
            Scenes.Select(scene => scene.Content));
    }
}

internal sealed class ContextSidebarSceneSnapshot
{
    public ContextSidebarSceneSnapshot(SceneData scene, string content)
    {
        Scene = scene;
        Content = content;
    }

    public SceneData Scene { get; }
    public string Content { get; set; }
}