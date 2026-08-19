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
/// <summary>
/// Where in the book a line is being read, for resolving a voice that changes
/// with the story.
/// </summary>
/// <param name="ChapterGuid">Matched against an override's chapter, as is
/// <paramref name="ChapterTitle"/> - an override may have been written against
/// either, the guid when the app set it and the title when a writer edited the
/// file by hand.</param>
public sealed record NarrationPlacement(
    string? Act, string? ChapterGuid, string? ChapterTitle, string? SceneTitle);

/// <summary>One segment and where in the book it sits, so a voice the writer set
/// for part of the book can be resolved for it. Carried together because a flat
/// reading of the whole manuscript otherwise loses the position on the way
/// out of the loop that knew it.</summary>
public sealed record PlacedSegment(NarrationSegment Segment, NarrationPlacement? Where);

/// <summary>
/// A voice that only applies over part of the book.
///
/// The same act / chapter / scene shape the Codex already uses to say what a
/// character is like at a point in the story, because it is the same statement:
/// a writer who has recorded that Mira is sixty-one by chapter twenty is saying
/// something about how she sounds, and should not have to say it twice in a
/// different vocabulary.
/// </summary>
public sealed class VoiceOverride
{
    /// <summary>Whose voice this is. Blank means the narrator.</summary>
    public string CharacterId { get; set; } = string.Empty;

    public string? Act { get; set; }

    /// <summary>The chapter's guid or its title. Blank with an act set means the
    /// whole act.</summary>
    public string Chapter { get; set; } = string.Empty;

    /// <summary>The scene's title, or blank for the whole chapter.</summary>
    public string? Scene { get; set; }

    /// <summary>The voice to read them in over that stretch.</summary>
    public string VoiceId { get; set; } = string.Empty;
}

/// <summary>
/// The stretch of book a scoped voice applies over.
///
/// Blank fields widen it: an act alone is the whole act, an act and a chapter
/// the whole chapter, all three one scene. Nothing set at all is not a scope and
/// is refused rather than quietly becoming the character's standing voice.
/// </summary>
public sealed record VoiceScope(string? Act, string? Chapter, string? Scene)
{
    /// <summary>True where this names any part of the book at all.</summary>
    public bool IsSomewhere =>
        !string.IsNullOrWhiteSpace(Act)
        || !string.IsNullOrWhiteSpace(Chapter)
        || !string.IsNullOrWhiteSpace(Scene);

    /// <summary>The same stretch written the same way however the caller spelled
    /// it, so two writers of the same scope produce one override rather than
    /// two.</summary>
    public VoiceScope Trimmed() => new(
        Blank(Act) ? null : Act!.Trim(),
        Blank(Chapter) ? string.Empty : Chapter!.Trim(),
        Blank(Scene) ? null : Scene!.Trim());

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

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

    /// <summary>
    /// Voices that only apply over part of the book, most specific first when
    /// they are resolved.
    ///
    /// A character is not one voice for four hundred pages. They age, they are
    /// injured, they are disguised, they are possessed, they are remembered as a
    /// child in a chapter set thirty years earlier - and until this existed the
    /// only way to say so was to design a second voice, which silently destroyed
    /// the first.
    /// </summary>
    public List<VoiceOverride> Overrides { get; set; } = [];

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
    /// Casts a character for one stretch of the book, or clears that stretch
    /// when <paramref name="voiceId"/> is blank.
    ///
    /// Identified by the stretch rather than appended, so saying it twice
    /// changes the voice instead of leaving two overrides whose winner depends
    /// on which was written first.
    /// </summary>
    /// <param name="characterId">Null for the narrator - who does get scoped
    /// voices even though they have only one standing one, because a book with
    /// a framing narrator genuinely changes teller between its parts.</param>
    /// <returns>False where the scope names nowhere, which would otherwise be
    /// an override matching every line in the book.</returns>
    public async Task<bool> SetScopeAsync(string? characterId, VoiceScope scope, string? voiceId)
    {
        var wanted = scope.Trimmed();
        if (!wanted.IsSomewhere)
            return false;

        var sheet = await ReadAsync();
        var id = characterId ?? string.Empty;
        sheet.Overrides.RemoveAll(o => Same(o, id, wanted));

        var voice = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();
        if (voice != null)
        {
            sheet.Overrides.Add(new VoiceOverride
            {
                CharacterId = id,
                Act = wanted.Act,
                Chapter = wanted.Chapter ?? string.Empty,
                Scene = wanted.Scene,
                VoiceId = voice
            });
        }

        await WriteAsync(sheet);
        return true;
    }

    /// <summary>Whether an override is about this character over this exact
    /// stretch. Case-insensitive on the stretch, because a writer retyping a
    /// chapter title is naming the same chapter.</summary>
    private static bool Same(VoiceOverride o, string characterId, VoiceScope scope)
        => string.Equals(o.CharacterId ?? string.Empty, characterId, StringComparison.Ordinal)
           && string.Equals(o.Act ?? string.Empty, scope.Act ?? string.Empty,
               StringComparison.OrdinalIgnoreCase)
           && string.Equals(o.Chapter ?? string.Empty, scope.Chapter ?? string.Empty,
               StringComparison.OrdinalIgnoreCase)
           && string.Equals(o.Scene ?? string.Empty, scope.Scene ?? string.Empty,
               StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The id a voice designed for one stretch of the book is stored under.
    ///
    /// Distinct from the character's standing voice and from their voice
    /// anywhere else, which is the whole point: designing a second voice for
    /// somebody used to reuse the id of their first and silently overwrite it,
    /// so a writer who wanted Mira older in Act Three lost how she sounded in
    /// Act One.
    ///
    /// The stretch is hashed rather than spelled out. Voice audio is stored in a
    /// file named for its id, and a chapter called "Part Two: The Crossing"
    /// carries characters that are not legal in a file name on any platform.
    /// Hashing also keeps the id stable while the id stays short.
    /// </summary>
    public static string ScopedVoiceId(string? characterId, string engineId, VoiceScope scope)
    {
        var wanted = scope.Trimmed();
        if (!wanted.IsSomewhere)
            return $"{characterId}-{engineId}";

        var said = string.Join(
            ' ',
            (wanted.Act ?? string.Empty).ToLowerInvariant(),
            (wanted.Chapter ?? string.Empty).ToLowerInvariant(),
            (wanted.Scene ?? string.Empty).ToLowerInvariant());
        var digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(said)))[..8].ToLowerInvariant();
        return $"{characterId}-at{digest}-{engineId}";
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
    /// <param name="where">Where in the book the line is, so a voice the writer
    /// set for part of it wins over the character's standing one. Null means
    /// "wherever" and resolves to the standing voice - which is what every
    /// caller that has no position to give should pass.</param>
    public static string? Resolve(
        VoiceCastSheet sheet, string? characterId, NarrationPlacement? where = null)
    {
        var overridden = where == null ? null : Overriding(sheet, characterId, where);
        if (overridden != null)
            return overridden;

        return characterId != null && sheet.Voices.TryGetValue(characterId, out var voice)
            ? voice
            : sheet.NarratorVoiceId;
    }

    /// <summary>
    /// The voice an override gives this character here, or null where none
    /// applies.
    ///
    /// Most specific first, the same precedence the Codex resolves an entry's
    /// own fields by: a scene beats a chapter, which beats an act. Restating
    /// something in a narrower scope is how a writer says "and by this scene,
    /// more so."
    /// </summary>
    private static string? Overriding(
        VoiceCastSheet sheet, string? characterId, NarrationPlacement where)
    {
        var id = characterId ?? string.Empty;
        var mine = sheet.Overrides
            .Where(o => o.VoiceId.Length > 0
                && string.Equals(o.CharacterId ?? string.Empty, id, StringComparison.Ordinal))
            .ToList();
        if (mine.Count == 0)
            return null;

        bool Chapter(VoiceOverride o) =>
            (!string.IsNullOrWhiteSpace(where.ChapterGuid)
             && string.Equals(o.Chapter, where.ChapterGuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(where.ChapterTitle)
                && string.Equals(o.Chapter, where.ChapterTitle, StringComparison.OrdinalIgnoreCase));

        var match = mine.FirstOrDefault(o =>
            Chapter(o)
            && !string.IsNullOrWhiteSpace(o.Scene)
            && string.Equals(o.Scene, where.SceneTitle, StringComparison.OrdinalIgnoreCase));

        match ??= mine.FirstOrDefault(o => Chapter(o) && string.IsNullOrWhiteSpace(o.Scene));

        match ??= mine.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.Act)
            && string.IsNullOrWhiteSpace(o.Chapter)
            && string.Equals(o.Act, where.Act, StringComparison.OrdinalIgnoreCase));

        return match?.VoiceId;
    }

    /// <summary>Every voice the sheet names, standing and scoped, so a caller
    /// can read exactly those off disk. The narrator's included.</summary>
    public static IReadOnlyList<string> AllVoices(VoiceCastSheet sheet)
        => [.. sheet.Voices.Values
            .Concat(sheet.Overrides.Select(o => o.VoiceId))
            .Append(sheet.NarratorVoiceId ?? string.Empty)
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.Ordinal)];

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
