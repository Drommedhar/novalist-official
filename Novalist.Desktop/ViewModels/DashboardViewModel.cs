using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    public Func<Task<AddImageSourceChoice?>>? ChooseAddImageSourceRequested { get; set; }
    public Func<string?, Task<string?>>? PickCoverImageRequested { get; set; }
    public Func<Task<string?>>? BrowseImageRequested { get; set; }
    public Func<AddImageSourceChoice, Task<string?>>? ImportExternalImageRequested { get; set; }
    public Func<string?, Task>? CoverImageSelected { get; set; }

    private string? _currentCoverRelativePath;

    // ── Enhanced Statistics ──

    [ObservableProperty]
    private ObservableCollection<StatusBreakdownItem> _statusBreakdown = [];

    [ObservableProperty]
    private ObservableCollection<ChapterPacingItem> _chapterPacing = [];

    [ObservableProperty]
    private ObservableCollection<EchoPhrase> _echoPhrases = [];

    [ObservableProperty]
    private bool _hasEnhancedStats;

    [ObservableProperty]
    private int _longestChapterWords;

    [ObservableProperty]
    private int _shortestChapterWords;

    [ObservableProperty]
    private double _averageSceneWords;

    [ObservableProperty]
    private int _scenesInOutline;

    [ObservableProperty]
    private int _scenesInFirstDraft;

    [ObservableProperty]
    private int _scenesInRevised;

    [ObservableProperty]
    private int _scenesInEdited;

    [ObservableProperty]
    private int _scenesInFinal;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ActivityItem> _recentActivity = [];

    public bool HasRecentActivity => RecentActivity.Count > 0;

    partial void OnRecentActivityChanged(ObservableCollection<ActivityItem> value)
        => OnPropertyChanged(nameof(HasRecentActivity));

    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);

    partial void OnAuthorChanged(string value) => OnPropertyChanged(nameof(HasAuthor));

    [ObservableProperty]
    private int _totalWords;

    [ObservableProperty]
    private int _chapterCount;

    [ObservableProperty]
    private int _sceneCount;

    [ObservableProperty]
    private int _characterCount;

    [ObservableProperty]
    private int _locationCount;

    [ObservableProperty]
    private int _readingTimeMinutes;

    [ObservableProperty]
    private int _averageChapterWords;

    [ObservableProperty]
    private int _dailyGoalCurrent;

    [ObservableProperty]
    private int _dailyGoalTarget;

    [ObservableProperty]
    private int _dailyGoalPercent;

    [ObservableProperty]
    private int _projectGoalTarget;

    [ObservableProperty]
    private int _projectGoalPercent;

    [ObservableProperty]
    private string? _deadline;

    [ObservableProperty]
    private int _daysRemaining;

    [ObservableProperty]
    private int _wordsPerDayNeeded;

    [ObservableProperty]
    private Bitmap? _coverImage;

    // ── Word history chart ──
    private IWordHistoryService? _wordHistory;
    private string? _activeBookId;

    [ObservableProperty]
    private int _historyRangeDays = 30;

    [ObservableProperty]
    private int _currentStreak;

    [ObservableProperty]
    private int _todayWords;

    [ObservableProperty]
    private ObservableCollection<WordHistoryBarItem> _wordHistoryBars = [];

    public void AttachWordHistory(IWordHistoryService service)
    {
        if (_wordHistory != null)
            _wordHistory.HistoryChanged -= OnHistoryChanged;
        _wordHistory = service;
        _wordHistory.HistoryChanged += OnHistoryChanged;
    }

    public void SetActiveBookId(string bookId)
    {
        _activeBookId = bookId;
        RefreshWordHistory();
    }

    private void OnHistoryChanged() => RefreshWordHistory();

    [RelayCommand]
    private void SetHistoryRange(string? days)
    {
        if (!int.TryParse(days, out var d)) return;
        HistoryRangeDays = d;
        RefreshWordHistory();
    }

    public void RefreshWordHistory()
    {
        if (_wordHistory == null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var goal = DailyGoalTarget;

        TodayWords = _wordHistory.TotalForDay(today, _activeBookId);
        CurrentStreak = _wordHistory.CurrentStreak(today, Math.Max(1, goal), _activeBookId);

        var bars = new List<WordHistoryBarItem>();
        var maxWords = 1;
        for (int i = HistoryRangeDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var words = _wordHistory.TotalForDay(day, _activeBookId);
            if (words > maxWords) maxWords = words;
            bars.Add(new WordHistoryBarItem
            {
                Date = day,
                Words = words,
                MetGoal = goal > 0 && words >= goal,
            });
        }
        const double MaxBarPx = 100.0;
        foreach (var b in bars)
        {
            b.HeightFraction = Math.Min(1.0, b.Words / (double)maxWords);
            b.HeightPx = Math.Max(2.0, b.HeightFraction * MaxBarPx);
        }

        WordHistoryBars = new ObservableCollection<WordHistoryBarItem>(bars);
    }

    public string TotalWordsDisplay => TextStatistics.FormatCompactCount(TotalWords);
    public string AverageChapterWordsDisplay => TextStatistics.FormatCompactCount(AverageChapterWords);
    public string ReadingTimeDisplay => LocFormatters.ReadingTime(ReadingTimeMinutes);
    public bool HasDeadline => !string.IsNullOrWhiteSpace(Deadline);
    public bool HasCoverImage => CoverImage != null;

    [RelayCommand]
    private async Task PickCoverImageAsync()
    {
        if (CoverImageSelected == null)
            return;

        // If a cover is already set, go straight to library picker to swap it
        if (!string.IsNullOrEmpty(_currentCoverRelativePath))
        {
            if (PickCoverImageRequested == null) return;
            var selected = await PickCoverImageRequested.Invoke(_currentCoverRelativePath);
            if (string.IsNullOrEmpty(selected) ||
                string.Equals(selected, _currentCoverRelativePath, StringComparison.OrdinalIgnoreCase))
                return;

            await CoverImageSelected.Invoke(selected);
            return;
        }

        // No cover yet — show source choice dialog
        var choice = await (ChooseAddImageSourceRequested?.Invoke() ?? Task.FromResult<AddImageSourceChoice?>(null));
        if (choice == null) return;

        string? relativePath;
        switch (choice.Value)
        {
            case AddImageSourceChoice.Library:
                if (PickCoverImageRequested == null) return;
                relativePath = await PickCoverImageRequested.Invoke(null);
                break;
            case AddImageSourceChoice.Import:
                if (BrowseImageRequested == null) return;
                var filePath = await BrowseImageRequested.Invoke();
                if (string.IsNullOrEmpty(filePath)) return;
                // Pass with "import:" prefix so the parent knows to import
                await CoverImageSelected.Invoke("import:" + filePath);
                return;
            case AddImageSourceChoice.Clipboard:
            case AddImageSourceChoice.Url:
                if (ImportExternalImageRequested == null) return;
                var externalPath = await ImportExternalImageRequested.Invoke(choice.Value);
                if (string.IsNullOrEmpty(externalPath)) return;
                await CoverImageSelected.Invoke("import:" + externalPath);
                return;
            default:
                return;
        }

        if (string.IsNullOrEmpty(relativePath)) return;
        await CoverImageSelected.Invoke(relativePath);
    }
    public string DeadlineDisplay => HasDeadline ? Deadline! : string.Empty;
    public string DaysRemainingLabel => Loc.T("dashboard.daysRemaining", DaysRemaining);
    public string WordsPerDayLabel => Loc.T("dashboard.wordsPerDay", WordsPerDayNeeded.ToString("N0"));

    public void Refresh(
        string projectName,
        int totalWords,
        int chapterCount,
        int sceneCount,
        int characterCount,
        int locationCount,
        int readingTimeMinutes,
        int averageChapterWords,
        int dailyGoalCurrent,
        int dailyGoalTarget,
        int dailyGoalPercent,
        int projectGoalTarget,
        int projectGoalPercent,
        string? deadline,
        string? coverImagePath,
        string? coverRelativePath = null)
    {
        ProjectName = projectName;
        TotalWords = totalWords;
        ChapterCount = chapterCount;
        SceneCount = sceneCount;
        CharacterCount = characterCount;
        LocationCount = locationCount;
        ReadingTimeMinutes = readingTimeMinutes;
        AverageChapterWords = averageChapterWords;
        DailyGoalCurrent = dailyGoalCurrent;
        DailyGoalTarget = dailyGoalTarget;
        DailyGoalPercent = dailyGoalPercent;
        ProjectGoalTarget = projectGoalTarget;
        ProjectGoalPercent = projectGoalPercent;
        Deadline = deadline;

        LoadCoverImage(coverImagePath);
        _currentCoverRelativePath = coverRelativePath;
        ComputeDeadlineMetrics(totalWords, projectGoalTarget, deadline);
        NotifyComputedProperties();
        RefreshWordHistory();
    }

    private void LoadCoverImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            CoverImage = null;
            return;
        }

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            CoverImage = Bitmap.DecodeToWidth(stream, 240);
        }
        catch
        {
            CoverImage = null;
        }
    }

    private void ComputeDeadlineMetrics(int totalWords, int projectGoal, string? deadline)
    {
        if (string.IsNullOrWhiteSpace(deadline)
            || !DateTime.TryParse(deadline, out var deadlineDate))
        {
            DaysRemaining = 0;
            WordsPerDayNeeded = 0;
            return;
        }

        var remaining = (deadlineDate.Date - DateTime.Today).Days;
        DaysRemaining = Math.Max(0, remaining);

        var wordsLeft = Math.Max(0, projectGoal - totalWords);
        WordsPerDayNeeded = DaysRemaining > 0
            ? (int)Math.Ceiling(wordsLeft / (double)DaysRemaining)
            : wordsLeft;
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(TotalWordsDisplay));
        OnPropertyChanged(nameof(AverageChapterWordsDisplay));
        OnPropertyChanged(nameof(ReadingTimeDisplay));
        OnPropertyChanged(nameof(HasDeadline));
        OnPropertyChanged(nameof(HasCoverImage));
        OnPropertyChanged(nameof(DeadlineDisplay));
        OnPropertyChanged(nameof(DaysRemainingLabel));
        OnPropertyChanged(nameof(WordsPerDayLabel));
    }

    /// <summary>
    /// Refreshes the enhanced statistics (progress breakdown, pacing, echo finder).
    /// Called after the basic Refresh with detailed chapter/scene data.
    /// </summary>
    public void RefreshEnhancedStats(
        List<ChapterData> chapters,
        Dictionary<string, List<SceneData>> scenesByChapter,
        Dictionary<string, string> sceneContents)
    {
        // ── Status breakdown ──
        var statusCounts = new Dictionary<ChapterStatus, int>();
        var statusWords = new Dictionary<ChapterStatus, int>();
        foreach (var status in Enum.GetValues<ChapterStatus>())
        {
            statusCounts[status] = 0;
            statusWords[status] = 0;
        }

        var allSceneWords = new List<int>();
        var chapterWordCounts = new List<int>();

        foreach (var chapter in chapters)
        {
            statusCounts[chapter.Status]++;
            var chapterWords = 0;

            if (scenesByChapter.TryGetValue(chapter.Guid, out var scenes))
            {
                foreach (var scene in scenes)
                {
                    chapterWords += scene.WordCount;
                    allSceneWords.Add(scene.WordCount);
                }
            }

            statusWords[chapter.Status] += chapterWords;
            chapterWordCounts.Add(chapterWords);
        }

        ScenesInOutline = statusCounts[ChapterStatus.Outline];
        ScenesInFirstDraft = statusCounts[ChapterStatus.FirstDraft];
        ScenesInRevised = statusCounts[ChapterStatus.Revised];
        ScenesInEdited = statusCounts[ChapterStatus.Edited];
        ScenesInFinal = statusCounts[ChapterStatus.Final];

        var totalScenes = statusCounts.Values.Sum();
        var breakdownItems = new ObservableCollection<StatusBreakdownItem>();
        foreach (var status in Enum.GetValues<ChapterStatus>())
        {
            if (statusCounts[status] > 0)
            {
                var pct = totalScenes > 0 ? (double)statusCounts[status] / totalScenes : 0.0;
                breakdownItems.Add(new StatusBreakdownItem(
                    status.ToString(),
                    statusCounts[status],
                    statusWords[status],
                    GetStatusColor(status),
                    pct));
            }
        }
        StatusBreakdown = breakdownItems;

        // ── Pacing analysis ──
        LongestChapterWords = chapterWordCounts.Count > 0 ? chapterWordCounts.Max() : 0;
        ShortestChapterWords = chapterWordCounts.Count > 0 ? chapterWordCounts.Min() : 0;
        AverageSceneWords = allSceneWords.Count > 0 ? allSceneWords.Average() : 0;

        var pacingItems = new ObservableCollection<ChapterPacingItem>();
        var maxWords = Math.Max(1, LongestChapterWords);
        for (int i = 0; i < chapters.Count; i++)
        {
            var words = chapterWordCounts[i];
            pacingItems.Add(new ChapterPacingItem(
                chapters[i].Title,
                words,
                Math.Round(100d * words / maxWords, 1)));
        }
        ChapterPacing = pacingItems;

        // ── Echo finder (repeated phrases) ──
        var allText = string.Join(" ", sceneContents.Values);
        var echos = FindEchoPhrases(allText, 3, 5);
        EchoPhrases = new ObservableCollection<EchoPhrase>(echos.Take(20));

        HasEnhancedStats = chapters.Count > 0;
    }

    private static string GetStatusColor(ChapterStatus status) => status switch
    {
        ChapterStatus.Outline => "#6B7280",
        ChapterStatus.FirstDraft => "#3B82F6",
        ChapterStatus.Revised => "#F59E0B",
        ChapterStatus.Edited => "#8B5CF6",
        ChapterStatus.Final => "#10B981",
        _ => "#9CA3AF"
    };

    /// <summary>
    /// Finds repeated n-gram phrases in the text with a frequency above the threshold.
    /// </summary>
    private static List<EchoPhrase> FindEchoPhrases(string text, int minWords, int threshold)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Strip HTML tags for analysis
        var clean = Regex.Replace(text, "<[^>]+>", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim().ToLowerInvariant();
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < minWords)
            return [];

        var phraseCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // 3-word and 4-word n-grams
        for (int n = minWords; n <= Math.Min(minWords + 1, 4); n++)
        {
            for (int i = 0; i <= words.Length - n; i++)
            {
                var phrase = string.Join(' ', words.Skip(i).Take(n));
                // Skip phrases that are mostly stop words
                if (IsStopPhrase(phrase)) continue;

                phraseCounts.TryGetValue(phrase, out var count);
                phraseCounts[phrase] = count + 1;
            }
        }

        return phraseCounts
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new EchoPhrase(kv.Key, kv.Value))
            .ToList();
    }

    private static bool IsStopPhrase(string phrase)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "is", "was", "are", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "shall", "can", "it", "its", "he", "she",
            "him", "her", "his", "they", "them", "their", "this", "that", "these",
            "those", "i", "you", "we", "not", "no", "if", "so", "as", "from"
        };

        var words = phrase.Split(' ');
        var stopCount = words.Count(w => stopWords.Contains(w));
        // Phrase is a "stop phrase" if most words are stop words
        return stopCount >= words.Length - 1;
    }
}

public sealed class StatusBreakdownItem
{
    public string Status { get; }
    public int Count { get; }
    public int WordCount { get; }
    public string Color { get; }
    public double BarPercent { get; }
    public string WordCountDisplay => TextStatistics.FormatCompactCount(WordCount);

    public StatusBreakdownItem(string status, int count, int wordCount, string color, double barPercent = 0)
    {
        Status = status;
        Count = count;
        WordCount = wordCount;
        Color = color;
        BarPercent = barPercent;
    }
}

public sealed class ChapterPacingItem
{
    public string Title { get; }
    public int Words { get; }
    public double BarPercent { get; }
    public string WordsDisplay => TextStatistics.FormatCompactCount(Words);

    public ChapterPacingItem(string title, int words, double barPercent)
    {
        Title = title;
        Words = words;
        BarPercent = barPercent;
    }
}

public sealed class EchoPhrase
{
    public string Phrase { get; }
    public int Count { get; }

    public EchoPhrase(string phrase, int count)
    {
        Phrase = phrase;
        Count = count;
    }
}

public sealed class WordHistoryBarItem
{
    public DateOnly Date { get; set; }
    public int Words { get; set; }
    public double HeightFraction { get; set; }
    public double HeightPx { get; set; }
    public bool MetGoal { get; set; }
    public string BarColor => MetGoal ? "#5BA855" : "#7C7CB2";
    public string DateLabel => Date.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture);
    public string Tooltip => $"{DateLabel}: {Words:N0} words";
}
