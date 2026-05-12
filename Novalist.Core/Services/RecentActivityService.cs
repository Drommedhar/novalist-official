using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class RecentActivityService : IRecentActivityService
{
    private const int MaxEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private string? _path;
    private readonly List<ActivityItem> _items = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public IReadOnlyList<ActivityItem> Recent => _items;
    public event Action? Changed;

    public async Task LoadAsync(string projectRoot)
    {
        _items.Clear();
        var dir = Path.Combine(projectRoot, ".novalist");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "activity.json");

        if (File.Exists(_path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_path);
                var loaded = JsonSerializer.Deserialize<List<ActivityItem>>(json, JsonOptions);
                if (loaded != null)
                    _items.AddRange(loaded);
            }
            catch
            {
                // Corrupt file — start fresh.
            }
        }

        Changed?.Invoke();
    }

    public async Task LogAsync(ActivityItem item)
    {
        if (_path == null) return;

        // Deduplicate consecutive edits of the same scene within 60 seconds.
        if (_items.Count > 0)
        {
            var last = _items[0];
            if (last.Type == item.Type
                && last.SceneId == item.SceneId
                && (item.Timestamp - last.Timestamp).TotalSeconds < 60)
            {
                last.Timestamp = item.Timestamp;
                await PersistAsync();
                Changed?.Invoke();
                return;
            }
        }

        _items.Insert(0, item);
        if (_items.Count > MaxEntries)
            _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);

        await PersistAsync();
        Changed?.Invoke();
    }

    private async Task PersistAsync()
    {
        if (_path == null) return;
        await _ioLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_items, JsonOptions);
            await File.WriteAllTextAsync(_path, json);
        }
        catch
        {
            // Swallow IO errors — activity log is non-critical.
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
