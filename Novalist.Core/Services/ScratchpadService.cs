using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novalist.Core.Services;

/// <summary>One loose note, kept outside every project.</summary>
public sealed class ScratchpadNote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Notes that belong to the writer rather than to a project.
///
/// Quick Capture writes into the open project's research inbox, which means a
/// thought that arrives before the right project is open has nowhere to go - and
/// the moment a thought arrives is exactly the moment somebody is not sitting in
/// front of the project it belongs to. These live beside the settings file,
/// survive closing every project, and can be filed into whichever project opens
/// later.
///
/// Deliberately a flat list with no folders, tags or search. A scratchpad that
/// needs organising is a second research library, and the point of this one is
/// that using it costs nothing.
/// </summary>
public sealed class ScratchpadService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private List<ScratchpadNote>? _notes;

    /// <param name="directory">
    /// Where the file lives; defaults to the Novalist application-data folder,
    /// which is the same place the settings file sits.
    /// </param>
    public ScratchpadService(string? directory = null)
    {
        var folder = directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "scratchpad.json");
    }

    /// <summary>Every note, newest first.</summary>
    public IReadOnlyList<ScratchpadNote> GetAll()
    {
        Load();
        return [.. _notes!.OrderByDescending(n => n.CreatedAt)];
    }

    /// <summary>
    /// Adds a note. Blank text is not a note, and returns null rather than
    /// leaving an empty row for somebody to wonder about later.
    /// </summary>
    public async Task<ScratchpadNote?> AddAsync(string? text)
    {
        var body = (text ?? string.Empty).Trim();
        if (body.Length == 0) return null;

        Load();
        var note = new ScratchpadNote { Text = body };
        _notes!.Add(note);
        await SaveAsync();
        return note;
    }

    /// <summary>Removes a note. Unknown ids are not an error.</summary>
    public async Task RemoveAsync(string id)
    {
        Load();
        if (_notes!.RemoveAll(n => string.Equals(n.Id, id, StringComparison.Ordinal)) == 0) return;
        await SaveAsync();
    }

    /// <summary>The note with that id, or null.</summary>
    public ScratchpadNote? Find(string id)
    {
        Load();
        return _notes!.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
    }

    private void Load()
    {
        if (_notes != null) return;
        try
        {
            _notes = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<ScratchpadNote>>(
                    File.ReadAllText(_path), JsonOptions) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A scratchpad that cannot be read starts empty rather than
            // stopping the app. The file is left alone until something is
            // added, so a recoverable one is not overwritten by the attempt.
            _notes = [];
        }
    }

    private async Task SaveAsync()
        => await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(_notes, JsonOptions));
}
