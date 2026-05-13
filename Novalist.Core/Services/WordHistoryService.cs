using System.Text;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class WordHistoryService : IWordHistoryService
{
    private readonly IFileService _fileService;
    private readonly IProjectService _projectService;
    private readonly List<WordHistoryEntry> _entries = new();
    private readonly Dictionary<string, int> _lastWordsPerScene = new(StringComparer.OrdinalIgnoreCase);
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
        _entries.Clear();
        _lastWordsPerScene.Clear();
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
                    _entries.Add(entry);
                    _lastWordsPerScene[scene.Id] = scene.WordCount;
                    sb.Append(JsonSerializer.Serialize(entry));
                    sb.Append('\n');
                }
            }

            if (sb.Length == 0) return;

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
            _entries.Clear();
            _lastWordsPerScene.Clear();
            _loaded = false;
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
                catch { /* skip malformed lines */ }
            }
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
            var existingIdx = _entries.FindLastIndex(e =>
                string.Equals(e.SceneId, sceneId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Date, todayKey, StringComparison.Ordinal));

            if (existingIdx >= 0)
            {
                var ex = _entries[existingIdx];
                ex.Delta += delta;
                ex.Words = wordsAfterSave;
                ex.BookId = bookId;
            }
            else
            {
                _entries.Add(new WordHistoryEntry
                {
                    Date = todayKey,
                    SceneId = sceneId,
                    BookId = bookId,
                    Words = wordsAfterSave,
                    Delta = delta,
                });
            }
            _lastWordsPerScene[sceneId] = wordsAfterSave;

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
