using System.Text;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class WordHistoryService : IWordHistoryService
{
    private readonly IFileService _fileService;
    private readonly IProjectService _projectService;
    /// <summary>
    /// The history, replaced on a reload rather than emptied and refilled.
    /// </summary>
    /// <remarks>
    /// Every writer here takes <see cref="_gate"/>. The readers never did, and
    /// could not without turning a synchronous total into an async call - so a
    /// reload cleared this list and refilled it line by line while a reader was
    /// part-way through a <c>foreach</c> over it, and the reader died with
    /// "Collection was modified; enumeration operation may not execute".
    ///
    /// Not hypothetical and not rare: two overlapping dashboard reads are
    /// enough, which is what opening the Dashboard, going to Settings and
    /// coming back produces on a book big enough for the first read to still be
    /// running. The backend serves requests concurrently and shares one
    /// workspace between all of them.
    ///
    /// A reader now enumerates whichever list was current when it started.
    /// Whatever a reload does afterwards happens to a different list, so the
    /// reader's view stays whole - a moment out of date rather than an
    /// exception, which for a running total is the right trade.
    /// </remarks>
    private List<WordHistoryEntry> _entries = new();
    private Dictionary<string, int> _lastWordsPerScene = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public event Action? HistoryChanged;

    public WordHistoryService(IFileService fileService, IProjectService projectService)
    {
        _fileService = fileService;
        _projectService = projectService;
    }

    private string? HistoryFilePath
    {
        get
        {
            var root = _projectService.ProjectRoot;
            return root == null ? null : _fileService.CombinePath(root, ".novalist", "word-history.jsonl");
        }
    }

    public void Reset()
    {
        // Replaced rather than cleared, for the reason on _entries: a reader
        // may be part-way through the list this drops.
        _entries = new List<WordHistoryEntry>();
        _lastWordsPerScene = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _loaded = false;
    }

    public async Task MigrateLegacyBaselineAsync()
    {
        var path = HistoryFilePath;
        if (path == null) return;

        await _gate.WaitAsync();
        try
        {
            if (!_loaded) await LoadInternalNoLockAsync();
            // Only seed if the journal is empty.
            if (_entries.Count > 0) return;

            var manifest = _projectService.ScenesManifest;
            var book = _projectService.ActiveBook;
            if (manifest == null || book == null) return;

            var goals = _projectService.ProjectSettings?.WordCountGoals;
            // Treat any legacy baseline data OR an existing manuscript as worth seeding.
            var hasLegacy = goals != null
                && (goals.DailyBaselineWords.HasValue || !string.IsNullOrEmpty(goals.DailyBaselineDate));

            var anyScene = manifest.Chapters.Any(c => c.Value.Count > 0);
            if (!hasLegacy && !anyScene) return;

            var seedDateKey = !string.IsNullOrEmpty(goals?.DailyBaselineDate)
                ? goals.DailyBaselineDate
                : DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            var bookId = book.Id;
            var sb = new StringBuilder();
            // Seeded to one side and published at the end, like a reload.
            var seeded = new List<WordHistoryEntry>(_entries);
            var seededWords = new Dictionary<string, int>(_lastWordsPerScene, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in manifest.Chapters)
            {
                foreach (var scene in pair.Value)
                {
                    var entry = new Novalist.Core.Models.WordHistoryEntry
                    {
                        Date = seedDateKey,
                        SceneId = scene.Id,
                        BookId = bookId,
                        Words = scene.WordCount,
                        Delta = 0,
                    };
                    seeded.Add(entry);
                    seededWords[scene.Id] = scene.WordCount;
                    sb.Append(JsonSerializer.Serialize(entry));
                    sb.Append('\n');
                }
            }

            if (sb.Length == 0) return;

            _entries = seeded;
            _lastWordsPerScene = seededWords;

            var dir = _fileService.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                await _fileService.CreateDirectoryAsync(dir);
            await _fileService.WriteTextAsync(path, sb.ToString());

            // Clear obsolete baseline fields so future loads do not re-seed.
            if (goals != null)
            {
                goals.DailyBaselineWords = null;
                goals.DailyBaselineDate = null;
                await _projectService.SaveProjectSettingsAsync();
            }
        }
        finally
        {
            _gate.Release();
        }

        HistoryChanged?.Invoke();
    }

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _loaded = false;
            // Built to one side and published in one assignment at the end, so
            // a reader part-way through a scan is never looking at a list that
            // is being emptied and refilled underneath it.
            var entries = new List<WordHistoryEntry>();
            var lastWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var path = HistoryFilePath;
            if (path == null || !await _fileService.ExistsAsync(path))
            {
                _entries = entries;
                _lastWordsPerScene = lastWords;
                _loaded = true;
                return;
            }

            var raw = await _fileService.ReadTextAsync(path);
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<WordHistoryEntry>(trimmed);
                    if (entry == null) continue;
                    entries.Add(entry);
                    lastWords[entry.SceneId] = entry.Words;
                }
                catch { /* skip malformed lines */ }
            }
            _entries = entries;
            _lastWordsPerScene = lastWords;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordSaveAsync(string bookId, string sceneId, int wordsAfterSave)
    {
        if (string.IsNullOrEmpty(sceneId)) return;
        var path = HistoryFilePath;
        if (path == null) return;

        await _gate.WaitAsync();
        try
        {
            if (!_loaded) await LoadInternalNoLockAsync();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var todayKey = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var prev = _lastWordsPerScene.TryGetValue(sceneId, out var prevWords) ? prevWords : 0;
            var delta = wordsAfterSave - prev;

            // Look for an existing in-memory row for (date, sceneId). If present, update its
            // delta cumulatively. The journal stays append-only.
            //
            // On a copy: a save lands while the Dashboard is reading, and
            // appending to the list it is scanning is the same crash a reload
            // used to cause.
            var updated = new List<WordHistoryEntry>(_entries);
            var existingIdx = updated.FindLastIndex(e =>
                string.Equals(e.SceneId, sceneId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Date, todayKey, StringComparison.Ordinal));

            if (existingIdx >= 0)
            {
                var ex = updated[existingIdx];
                ex.Delta += delta;
                ex.Words = wordsAfterSave;
                ex.BookId = bookId;
            }
            else
            {
                updated.Add(new WordHistoryEntry
                {
                    Date = todayKey,
                    SceneId = sceneId,
                    BookId = bookId,
                    Words = wordsAfterSave,
                    Delta = delta,
                });
            }
            _entries = updated;
            var words = new Dictionary<string, int>(_lastWordsPerScene, StringComparer.OrdinalIgnoreCase)
            {
                [sceneId] = wordsAfterSave
            };
            _lastWordsPerScene = words;

            var dir = _fileService.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                await _fileService.CreateDirectoryAsync(dir);

            var append = JsonSerializer.Serialize(new WordHistoryEntry
            {
                Date = todayKey,
                SceneId = sceneId,
                BookId = bookId,
                Words = wordsAfterSave,
                Delta = delta,
            }) + "\n";
            // Append-only: read existing and write concatenation (IFileService does not expose appends).
            var existing = await _fileService.ExistsAsync(path)
                ? await _fileService.ReadTextAsync(path)
                : string.Empty;
            await _fileService.WriteTextAsync(path, existing + append);
        }
        finally
        {
            _gate.Release();
        }

        HistoryChanged?.Invoke();
    }

    private async Task LoadInternalNoLockAsync()
    {
        // Caller must hold _gate.
        var path = HistoryFilePath;
        if (path == null || !await _fileService.ExistsAsync(path))
        {
            _loaded = true;
            return;
        }
        var raw = await _fileService.ReadTextAsync(path);
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<WordHistoryEntry>(trimmed);
                if (entry == null) continue;
                _entries.Add(entry);
                _lastWordsPerScene[entry.SceneId] = entry.Words;
            }
            catch { }
        }
        _loaded = true;
    }

    public int TotalForDay(DateOnly date, string? bookId = null)
    {
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var sum = 0;
        foreach (var e in _entries)
        {
            if (!string.Equals(e.Date, key, StringComparison.Ordinal)) continue;
            if (bookId != null && !string.Equals(e.BookId, bookId, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Delta > 0) sum += e.Delta;
        }
        return sum;
    }

    public IReadOnlyList<WordHistoryEntry> ReadRange(DateOnly from, DateOnly to, string? bookId = null)
    {
        var fromKey = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var toKey = to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var result = new List<WordHistoryEntry>();
        foreach (var e in _entries)
        {
            if (string.Compare(e.Date, fromKey, StringComparison.Ordinal) < 0) continue;
            if (string.Compare(e.Date, toKey, StringComparison.Ordinal) > 0) continue;
            if (bookId != null && !string.Equals(e.BookId, bookId, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(e);
        }
        return result;
    }

    public int CurrentStreak(DateOnly today, int dailyGoal, string? bookId = null)
    {
        if (dailyGoal <= 0) return 0;
        var streak = 0;
        var day = today;
        for (int i = 0; i < 366; i++)
        {
            if (TotalForDay(day, bookId) >= dailyGoal)
            {
                streak++;
                day = day.AddDays(-1);
            }
            else
            {
                break;
            }
        }
        return streak;
    }

    public IReadOnlyDictionary<string, int> ScenesTouchedOn(DateOnly date, string? bookId = null)
    {
        var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            if (!string.Equals(e.Date, key, StringComparison.Ordinal)) continue;
            if (bookId != null && !string.Equals(e.BookId, bookId, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Delta == 0) continue;
            dict[e.SceneId] = dict.GetValueOrDefault(e.SceneId) + e.Delta;
        }
        return dict;
    }
}
