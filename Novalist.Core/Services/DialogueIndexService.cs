using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>One character's line, with the scene it belongs to.</summary>
/// <param name="Candidates">Who else the line might belong to, with each one's
/// share of the evidence. Empty where the prose names the speaker outright and
/// there is nothing to second-guess.</param>
public sealed record DialogueLine(
    string LineKey,
    string Text,
    DialogueConfidence Confidence,
    bool Editable,
    string ContextBefore,
    string ContextAfter,
    IReadOnlyList<DialogueCandidate> Candidates);

/// <summary>The lines one scene contributes, in the order they are spoken.</summary>
public sealed record DialogueScene(
    string ChapterGuid,
    string SceneId,
    string ChapterTitle,
    string SceneTitle,
    string StoryDate,
    IReadOnlyList<DialogueLine> Lines);

/// <summary>
/// A run of scenes sharing one point in story time. <see cref="StoryDate"/> is
/// blank for the run before any date is known.
/// </summary>
public sealed record DialogueGroup(string StoryDate, IReadOnlyList<DialogueScene> Scenes);

/// <summary>How many lines a character has, for the roster beside the list.</summary>
public sealed record DialogueSpeakerTally(string CharacterId, string Name, int LineCount);

/// <summary>Everything one pass over the manuscript produced.</summary>
public sealed record DialogueIndex(
    IReadOnlyList<DialogueSpeakerTally> Speakers,
    int UnassignedCount,
    IReadOnlyList<DialogueGroup> Groups);

/// <summary>Why a line edit did not land.</summary>
public enum DialogueUpdateStatus
{
    Updated,

    /// <summary>The line key no longer resolves, or the text at that spot is not
    /// what the caller last read — the scene changed underneath them.</summary>
    Stale,

    /// <summary>The spoken text carries markup, so it cannot be rewritten from
    /// here without destroying it.</summary>
    NotEditable
}

public sealed record DialogueUpdateResult(DialogueUpdateStatus Status, string? LineKey);

/// <summary>
/// Collects every line a character speaks across the active book and groups it
/// by story time, so drift in how somebody talks is readable end to end.
///
/// Scenes are walked in reading order and a new group starts each time the
/// resolved in-world date changes; an undated scene continues the group it
/// follows rather than being exiled to a bucket at the end. That makes scene
/// order the fallback the writer asked for without ever dropping a scene out of
/// sequence.
///
/// Edits made in the view come back through <see cref="UpdateLineAsync"/>, which
/// rewrites just the words inside the quote marks in the scene file, takes a
/// snapshot first, and refuses the write outright if the scene moved on since
/// the caller last read it.
/// </summary>
public sealed class DialogueIndexService
{
    private readonly IProjectService _projects;
    private readonly ISnapshotService? _snapshots;

    public DialogueIndexService(IProjectService projects, ISnapshotService? snapshots = null)
    {
        _projects = projects;
        _snapshots = snapshots;
    }

    /// <summary>
    /// Scans the active book once. Returns the roster of characters who speak,
    /// how many lines could not be attributed to anyone, and — for
    /// <paramref name="characterId"/>, or the busiest speaker when that is blank —
    /// their lines grouped by story time. Pass <see cref="UnassignedSpeakerId"/>
    /// to list the lines nobody could be found for.
    /// </summary>
    public async Task<DialogueIndex> BuildAsync(
        IReadOnlyList<CharacterData> characters, string? characterId, string? writingLanguage)
    {
        var lexicon = SceneAnalysisLexicon.For(writingLanguage);
        var candidates = DialogueAttributor.BuildCandidates(
            characters, lexicon?.WordBoundaries ?? true);
        var language = DialogueAttributor.BuildLanguage(lexicon);

        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        var unassigned = 0;
        // Reading order, with each scene's lines already bucketed by speaker.
        var scanned = new List<(ChapterData Chapter, SceneData Scene, string StoryDate,
            Dictionary<string, List<DialogueLine>> BySpeaker)>();

        foreach (var chapter in _projects.GetChaptersOrdered())
        {
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _projects.ReadSceneContentAsync(chapter, scene);
                var (sceneText, spans) = DialogueScanner.ScanScene(html);
                if (spans.Count == 0)
                    continue;

                var attributions = DialogueAttributor.Attribute(
                    spans, sceneText, candidates, language, scene.DialogueSpeakers);

                var bySpeaker = new Dictionary<string, List<DialogueLine>>(StringComparer.Ordinal);
                for (var i = 0; i < spans.Count; i++)
                {
                    var span = spans[i];
                    var speaker = attributions[i].CharacterId ?? UnassignedSpeakerId;
                    if (speaker == UnassignedSpeakerId)
                        unassigned++;
                    else
                        tally[speaker] = tally.GetValueOrDefault(speaker) + 1;

                    if (!bySpeaker.TryGetValue(speaker, out var lines))
                    {
                        lines = [];
                        bySpeaker[speaker] = lines;
                    }
                    lines.Add(new DialogueLine(
                        span.LineKey, span.Text, attributions[i].Confidence, span.Editable,
                        span.ContextBefore.Trim(), span.ContextAfter.Trim(),
                        attributions[i].Candidates));
                }

                scanned.Add((chapter, scene, SceneStoryDate.Resolve(chapter, scene), bySpeaker));
            }
        }

        var speakers = characters
            .Where(c => tally.ContainsKey(c.Id))
            .Select(c => new DialogueSpeakerTally(
                c.Id, EntityResolveIndex.Compose(c.Name, c.Surname), tally[c.Id]))
            .OrderByDescending(s => s.LineCount)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var selected = ResolveSelection(characterId, speakers, unassigned);
        var groups = selected == null ? [] : BuildGroups(scanned, selected);
        return new DialogueIndex(speakers, unassigned, groups);
    }

    /// <summary>Sentinel speaker for lines no character could be found for. Not a
    /// valid entity id, so it can never collide with one.</summary>
    public const string UnassignedSpeakerId = "?unassigned";

    /// <summary>Honours an explicit pick, else opens on the character with the
    /// most lines so the view is never empty on arrival.</summary>
    private static string? ResolveSelection(
        string? requested, IReadOnlyList<DialogueSpeakerTally> speakers, int unassigned)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            if (requested == UnassignedSpeakerId)
                return unassigned > 0 ? UnassignedSpeakerId : null;
            return speakers.Any(s => s.CharacterId == requested) ? requested : null;
        }
        return speakers.Count > 0 ? speakers[0].CharacterId : null;
    }

    private static IReadOnlyList<DialogueGroup> BuildGroups(
        IReadOnlyList<(ChapterData Chapter, SceneData Scene, string StoryDate,
            Dictionary<string, List<DialogueLine>> BySpeaker)> scanned,
        string speaker)
    {
        var groups = new List<DialogueGroup>();
        var current = new List<DialogueScene>();
        var currentDate = string.Empty;

        void Flush()
        {
            if (current.Count > 0)
                groups.Add(new DialogueGroup(currentDate, current.ToArray()));
            current.Clear();
        }

        foreach (var (chapter, scene, storyDate, bySpeaker) in scanned)
        {
            // A dated scene opens a new run; an undated one carries on the last.
            if (storyDate.Length > 0 && storyDate != currentDate)
            {
                Flush();
                currentDate = storyDate;
            }

            if (!bySpeaker.TryGetValue(speaker, out var lines))
                continue;

            current.Add(new DialogueScene(
                chapter.Guid, scene.Id, chapter.Title, scene.Title, storyDate, lines.ToArray()));
        }

        Flush();
        return groups;
    }

    /// <summary>
    /// Records who speaks a line. A blank <paramref name="characterId"/> clears a
    /// wrong guess without naming a replacement; passing the id that automatic
    /// attribution already produced still stores an override, because the writer
    /// confirming a guess is worth keeping. Returns false when the scene or line
    /// no longer exists.
    /// </summary>
    public async Task<bool> SetSpeakerAsync(
        string chapterGuid, string sceneId, string lineKey, string? characterId)
    {
        var located = Locate(chapterGuid, sceneId);
        if (located == null)
            return false;

        var (chapter, scene) = located.Value;
        var html = await _projects.ReadSceneContentAsync(chapter, scene);
        if (!DialogueScanner.Scan(html).Any(s => s.LineKey == lineKey))
            return false;

        scene.DialogueSpeakers ??= new Dictionary<string, string>(StringComparer.Ordinal);
        scene.DialogueSpeakers[lineKey] = characterId ?? string.Empty;
        await _projects.SaveScenesAsync();
        return true;
    }

    /// <summary>Drops an override so the line goes back to automatic attribution.</summary>
    public async Task<bool> ClearSpeakerAsync(string chapterGuid, string sceneId, string lineKey)
    {
        var located = Locate(chapterGuid, sceneId);
        if (located == null)
            return false;

        var scene = located.Value.Scene;
        if (scene.DialogueSpeakers?.Remove(lineKey) != true)
            return false;

        if (scene.DialogueSpeakers.Count == 0)
            scene.DialogueSpeakers = null;
        await _projects.SaveScenesAsync();
        return true;
    }

    /// <summary>
    /// Rewrites one line's spoken text in the scene file. <paramref name="originalText"/>
    /// is what the caller last saw; if the scene no longer reads that way the write
    /// is refused, so an edit made here can never overwrite one made in the editor.
    /// A snapshot is taken first, exactly as find/replace does. Any speaker
    /// override follows the line to its new key.
    /// </summary>
    public async Task<DialogueUpdateResult> UpdateLineAsync(
        string chapterGuid, string sceneId, string lineKey, string originalText, string newText)
    {
        var located = Locate(chapterGuid, sceneId);
        if (located == null)
            return new DialogueUpdateResult(DialogueUpdateStatus.Stale, null);

        var (chapter, scene) = located.Value;
        var html = await _projects.ReadSceneContentAsync(chapter, scene);
        var spans = DialogueScanner.Scan(html);

        var span = spans.FirstOrDefault(s => s.LineKey == lineKey);
        if (span == null || !string.Equals(span.Text, originalText.Trim(), StringComparison.Ordinal))
            return new DialogueUpdateResult(DialogueUpdateStatus.Stale, null);
        if (!span.Editable)
            return new DialogueUpdateResult(DialogueUpdateStatus.NotEditable, null);

        // Non-null: the span came from this very scan, and the two conditions
        // ReplaceLine would decline on — markup in the range, text that no longer
        // matches — were both just checked above.
        var updated = DialogueScanner.ReplaceLine(html, span, newText)!;

        if (_snapshots != null)
            await _snapshots.TakeAsync(chapter, scene, "Auto-snapshot before dialogue edit");

        await _projects.WriteSceneContentAsync(chapter, scene, updated);
        scene.WordCount = CountWords(TextDiff.StripHtml(updated));

        var newKey = MigrateOverride(scene, spans, span, newText);
        await _projects.SaveScenesAsync();
        return new DialogueUpdateResult(DialogueUpdateStatus.Updated, newKey);
    }

    /// <summary>
    /// Moves a speaker override onto the key the edited text will produce. The
    /// new ordinal is the number of earlier lines that already read that way, so
    /// the key matches what a fresh scan of the saved scene will compute.
    /// </summary>
    private static string MigrateOverride(
        SceneData scene, IReadOnlyList<DialogueSpan> spans, DialogueSpan edited, string newText)
    {
        var normalized = DialogueScanner.Normalize(newText);
        var ordinal = 0;
        foreach (var span in spans)
        {
            if (span.LineKey == edited.LineKey)
                break;
            if (DialogueScanner.Normalize(span.Text) == normalized)
                ordinal++;
        }

        var newKey = DialogueScanner.BuildLineKey(normalized, ordinal);
        if (newKey == edited.LineKey || scene.DialogueSpeakers == null)
            return newKey;

        // One entry out, the same entry back in under the new key — the map can
        // never be emptied here, so it is never dropped either.
        if (scene.DialogueSpeakers.Remove(edited.LineKey, out var speaker))
            scene.DialogueSpeakers[newKey] = speaker;
        return newKey;
    }

    /// <summary>Same word pattern the editor and find/replace use, so a line
    /// edited here leaves the scene's stored count identical to a save from the
    /// editor.</summary>
    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : System.Text.RegularExpressions.Regex.Matches(
                text,
                @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;

    private (ChapterData Chapter, SceneData Scene)? Locate(string chapterGuid, string sceneId)
    {
        var chapter = _projects.GetChaptersOrdered()
            .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
        if (chapter == null)
            return null;
        var scene = _projects.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
        return scene == null ? null : (chapter, scene);
    }
}
