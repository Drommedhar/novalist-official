using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>
/// One designed voice, as the project remembers it.
/// </summary>
/// <param name="VoiceId">What the cast sheet and the engine both call it.</param>
/// <param name="DisplayName">Who it was designed for, so the list reads as a
/// cast rather than as a column of ids.</param>
/// <param name="Description">The brief it came from. Kept so a re-design starts
/// from what the writer asked for last time rather than from nothing - not so
/// the voice can be re-derived, which it cannot be.</param>
/// <param name="EngineId">Which engine made it. A voice designed by one engine
/// is not usable by another, and saying so beats a reading that sounds wrong for
/// reasons nobody can see.</param>
/// <param name="AudioFormat">Container of the stored reference audio.</param>
/// <param name="SampleRate">Sample rate of the stored reference audio.</param>
/// <param name="DesignedAt">When it was made, ISO-8601.</param>
public sealed record DesignedVoice(
    string VoiceId,
    string DisplayName,
    string Description,
    string EngineId,
    string AudioFormat,
    int SampleRate,
    string DesignedAt);

/// <summary>
/// The voices this book has been given, and the audio that is each one.
///
/// Kept in the project, under <c>.novalist/narration/voices/</c>, for the same
/// reason the cast sheet is: a designed voice is about <em>this book</em>, and it
/// should travel with the folder through Git and to another machine the way the
/// manuscript and the Codex do.
///
/// <b>The audio is the voice.</b> Voice design is not deterministic - the same
/// description and the same seed give a similar but measurably different speaker
/// every run - so storing the brief and re-deriving at playback would hand the
/// writer a slightly different actor each session, and a different one again in
/// any rendered file. The brief is kept only so a re-design can start from it.
/// </summary>
public sealed class VoiceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Tells Git the reference audio is binary.
    ///
    /// Without it a checkout with autocrlf on rewrites bytes inside a WAV that
    /// happen to look like line endings, and the voice comes back as noise - on
    /// somebody else's machine, long after the commit that did it.
    /// </summary>
    private const string GitAttributes = "* -text\n*.wav binary\n*.mp3 binary\n*.opus binary\n";

    private readonly IProjectService _projects;
    private readonly IFileService _files;

    public VoiceStore(IProjectService projects, IFileService files)
    {
        _projects = projects;
        _files = files;
    }

    /// <summary>Every designed voice in this book, oldest first. Empty when no
    /// project is open, none has been designed, or the index cannot be read - an
    /// unreadable index is worth no more than a missing one.</summary>
    public async Task<IReadOnlyList<DesignedVoice>> ListAsync()
    {
        var path = IndexPath();
        if (path == null || !await _files.ExistsAsync(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<DesignedVoice>>(
                await _files.ReadTextAsync(path), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>One designed voice, or null when this book has no such voice.</summary>
    public async Task<DesignedVoice?> GetAsync(string voiceId)
        => (await ListAsync()).FirstOrDefault(
            v => string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal));

    /// <summary>
    /// Stores a designed voice and its audio, replacing any voice of the same
    /// id. Returns false when no project is open, because there is nowhere to
    /// put it.
    /// </summary>
    public async Task<bool> SaveAsync(DesignedVoice voice, byte[] audio)
    {
        var dir = VoicesDir();
        if (dir == null)
            return false;

        await _files.CreateDirectoryAsync(dir);
        await _files.WriteTextAsync(_files.CombinePath(dir, ".gitattributes"), GitAttributes);
        await _files.WriteBytesAsync(AudioPath(dir, voice), audio);

        var voices = (await ListAsync())
            .Where(v => !string.Equals(v.VoiceId, voice.VoiceId, StringComparison.Ordinal))
            .Append(voice)
            .ToList();
        await _files.WriteTextAsync(
            _files.CombinePath(dir, "voices.json"), JsonSerializer.Serialize(voices, JsonOptions));
        return true;
    }

    /// <summary>
    /// The reference audio for a voice, or null when this machine does not have
    /// it - which is a cast assembled elsewhere, not a fault.
    /// </summary>
    public async Task<byte[]?> ReadAudioAsync(string voiceId)
    {
        var dir = VoicesDir();
        var voice = await GetAsync(voiceId);
        if (dir == null || voice == null)
            return null;

        var path = AudioPath(dir, voice);
        if (!await _files.ExistsAsync(path))
            return null;

        try
        {
            return await _files.ReadBytesAsync(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Forgets a voice: its entry and its audio. False when there was no such
    /// voice, so a caller can tell "deleted" from "was not there".
    /// </summary>
    public async Task<bool> DeleteAsync(string voiceId)
    {
        var dir = VoicesDir();
        var voice = await GetAsync(voiceId);
        if (dir == null || voice == null)
            return false;

        var path = AudioPath(dir, voice);
        if (await _files.ExistsAsync(path))
            await _files.DeleteFileAsync(path);

        var remaining = (await ListAsync())
            .Where(v => !string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal))
            .ToList();
        await _files.WriteTextAsync(
            _files.CombinePath(dir, "voices.json"), JsonSerializer.Serialize(remaining, JsonOptions));
        return true;
    }

    /// <summary>The audio for every voice named, for handing to an engine with a
    /// render request. Voices this machine does not have are left out rather than
    /// carried as nulls.</summary>
    public async Task<IReadOnlyDictionary<string, byte[]>> ReadAudioForAsync(
        IEnumerable<string> voiceIds)
    {
        var wanted = voiceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal);

        var audio = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var id in wanted)
        {
            var bytes = await ReadAudioAsync(id);
            if (bytes != null)
                audio[id] = bytes;
        }
        return audio;
    }

    /// <summary>The file one voice's audio lives in. The id is a host-made
    /// identifier rather than anything the writer typed, so it is safe as a file
    /// name.</summary>
    private string AudioPath(string dir, DesignedVoice voice)
        => _files.CombinePath(dir, $"{voice.VoiceId}.{Extension(voice.AudioFormat)}");

    /// <summary>A container name as a file extension, defaulting to wav rather
    /// than to something the operating system will not open.</summary>
    private static string Extension(string? format)
    {
        var trimmed = (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return trimmed.Length > 0 && trimmed.All(char.IsLetterOrDigit) ? trimmed : "wav";
    }

    private string? VoicesDir()
    {
        var root = _projects.ProjectRoot;
        return string.IsNullOrEmpty(root)
            ? null
            : _files.CombinePath(root, ".novalist", "narration", "voices");
    }

    private string? IndexPath()
    {
        var dir = VoicesDir();
        return dir == null ? null : _files.CombinePath(dir, "voices.json");
    }
}
