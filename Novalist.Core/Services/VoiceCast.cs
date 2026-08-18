using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>
/// Which voice reads whom, for one book.
/// </summary>
/// <remarks>
/// Kept in the project rather than beside the app, because a cast is about
/// <em>this book</em>: it travels with the folder through Git and to another
/// machine, the way the manuscript and the Codex do. What voices exist on a
/// given machine is a separate question, answered at playback - a cast naming a
/// voice this computer does not have is not corrupt, it is a cast the writer
/// assembled somewhere else.
/// </remarks>
public sealed class VoiceCastSheet
{
    /// <summary>The voice for everything nobody says out loud. One per book:
    /// a narrator that changed with the point of view would be a different
    /// book every chapter.</summary>
    public string? NarratorVoiceId { get; set; }

    /// <summary>Character id to voice id. A character absent from this map is
    /// read by the narrator rather than skipped.</summary>
    public Dictionary<string, string> Voices { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A standing note to the actor, per character: dimensions added to every
    /// line that character speaks.
    ///
    /// For somebody who is always more clipped, or warmer, or wearier than the
    /// prose says each time. The narrator's own is kept under
    /// <see cref="NarratorRegister"/> rather than under a blank key, so a
    /// character whose id somehow arrives empty cannot silently become the
    /// narrator's.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> Registers { get; set; }
        = new(StringComparer.Ordinal);

    /// <summary>The same, for the narrator.</summary>
    public Dictionary<string, double>? NarratorRegister { get; set; }

    /// <summary>The standing register for whoever is speaking, or null when
    /// they have none - which is every character until somebody sets one.</summary>
    public IReadOnlyDictionary<string, double>? RegisterFor(string? characterId)
        => string.IsNullOrEmpty(characterId)
            ? NarratorRegister
            : Registers.GetValueOrDefault(characterId);
}

/// <summary>
/// Reads and writes the book's cast sheet under
/// <c>.novalist/narration/cast.json</c>.
///
/// Deliberately incurious about what a voice id means. In the first release it
/// is a system voice from the platform's own speech engine; when a narration
/// engine is installed it will be one the writer designed. Keeping the resolver
/// out of here is what lets that change without the cast, the script or the
/// view knowing.
/// </summary>
public sealed class VoiceCast
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProjectService _projects;
    private readonly IFileService _files;

    public VoiceCast(IProjectService projects, IFileService files)
    {
        _projects = projects;
        _files = files;
    }

    /// <summary>The book's cast, or an empty one when no project is open, none
    /// has been assembled, or the file cannot be read. An unreadable cast sheet
    /// is worth no more than a missing one and is never worth an error in the
    /// writer's face - the voices are re-pickable in a few clicks.</summary>
    public async Task<VoiceCastSheet> ReadAsync()
    {
        var path = CastPath();
        if (path == null || !await _files.ExistsAsync(path))
            return new VoiceCastSheet();

        try
        {
            return JsonSerializer.Deserialize<VoiceCastSheet>(
                await _files.ReadTextAsync(path), JsonOptions) ?? new VoiceCastSheet();
        }
        catch (JsonException)
        {
            return new VoiceCastSheet();
        }
    }

    /// <summary>Writes the cast. No-op when no project is open.</summary>
    public async Task WriteAsync(VoiceCastSheet sheet)
    {
        var dir = NarrationDir();
        if (dir == null)
            return;

        await _files.CreateDirectoryAsync(dir);
        await _files.WriteTextAsync(
            _files.CombinePath(dir, "cast.json"), JsonSerializer.Serialize(sheet, JsonOptions));
    }

    /// <summary>
    /// Casts one character, or the narrator when <paramref name="characterId"/>
    /// is null. A blank <paramref name="voiceId"/> un-casts them, which sends
    /// their lines back to the narrator rather than silencing them.
    /// </summary>
    public async Task<VoiceCastSheet> SetVoiceAsync(string? characterId, string? voiceId)
    {
        var sheet = await ReadAsync();
        var voice = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();

        if (characterId == null)
            sheet.NarratorVoiceId = voice;
        else if (voice == null)
            sheet.Voices.Remove(characterId);
        else
            sheet.Voices[characterId] = voice;

        await WriteAsync(sheet);
        return sheet;
    }

    /// <summary>
    /// The voice a segment is read in: the character's own where they have one,
    /// the narrator's otherwise.
    ///
    /// The fallback is the point. A character with no voice yet is read by the
    /// narrator and shown as uncast, so a half-assembled cast produces a
    /// complete reading with some of it in the wrong voice - which the writer
    /// can hear and fix - rather than a reading with holes in it, which sounds
    /// like the feature is broken.
    /// </summary>
    public static string? Resolve(VoiceCastSheet sheet, string? characterId)
        => characterId != null && sheet.Voices.TryGetValue(characterId, out var voice)
            ? voice
            : sheet.NarratorVoiceId;

    private string? NarrationDir()
    {
        var root = _projects.ProjectRoot;
        return string.IsNullOrEmpty(root) ? null : _files.CombinePath(root, ".novalist", "narration");
    }

    private string? CastPath()
    {
        var dir = NarrationDir();
        return dir == null ? null : _files.CombinePath(dir, "cast.json");
    }
}
