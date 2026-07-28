using Novalist.Backend.Extensions;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Backs the Dialogue view: every line one character speaks across the active
/// book, grouped by story time, with the writer able to correct a speaker or
/// rewrite a line in place.
///
/// Attribution is deterministic — no AI. It reads the prose itself: entity
/// mention spans in a dialogue tag, a speech verb beside a name, then
/// back-and-forth alternation, and finally the overrides the writer set by hand.
/// The confidence that comes back with each line says which of those produced
/// it, so a guess is never dressed up as a fact.
/// </summary>
public sealed class DialogueRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public DialogueRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    /// <summary>
    /// One pass over the manuscript: the roster of characters who speak, how many
    /// lines nobody could be found for, and the selected character's lines grouped
    /// by story date. A blank <paramref name="characterId"/> opens on the busiest
    /// speaker so the view is never empty on arrival.
    /// </summary>
    [JsonRpcMethod("dialogue/index")]
    public async Task<DialogueIndexDto> IndexAsync(string? characterId)
    {
        var characters = await _entities.LoadCharactersAsync();
        var service = BuildService();
        var index = await service.BuildAsync(characters, characterId, WritingLanguage());

        var selected = ResolveSelected(characterId, index);
        Log.Info(
            $"dialogue/index speakers={index.Speakers.Count} unassigned={index.UnassignedCount} " +
            $"groups={index.Groups.Count} selected={selected != null}.");

        return new DialogueIndexDto(
            index.Speakers
                .Select(s => new DialogueSpeakerDto(s.CharacterId, s.Name, s.LineCount))
                .ToArray(),
            // The whole cast, not just those already speaking — a character whose
            // every line was misattributed still has to be pickable when the
            // writer corrects one.
            characters
                .Select(c => new DialogueCharacterDto(
                    c.Id, EntityResolveIndex.Compose(c.Name, c.Surname)))
                .Where(c => c.Name.Length > 0)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            index.UnassignedCount,
            selected,
            index.Groups
                .Select(g => new DialogueGroupDto(
                    g.StoryDate,
                    g.Scenes
                        .Select(scene => new DialogueSceneDto(
                            scene.ChapterGuid, scene.SceneId, scene.ChapterTitle, scene.SceneTitle,
                            scene.StoryDate,
                            scene.Lines
                                .Select(l => new DialogueLineDto(
                                    l.LineKey, l.Text, l.Confidence.ToString(), l.Editable,
                                    l.ContextBefore, l.ContextAfter,
                                    l.Candidates
                                        .Select(c => new DialogueCandidateDto(c.CharacterId, c.Percent))
                                        .ToArray()))
                                .ToArray()))
                        .ToArray()))
                .ToArray());
    }

    /// <summary>Assigns a line to a character. A blank id clears a wrong guess
    /// and leaves the line unattributed rather than re-guessing it.</summary>
    [JsonRpcMethod("dialogue/setSpeaker")]
    public async Task<bool> SetSpeakerAsync(
        string chapterGuid, string sceneId, string lineKey, string? characterId)
    {
        var ok = await BuildService().SetSpeakerAsync(chapterGuid, sceneId, lineKey, characterId);
        Log.Info($"dialogue/setSpeaker ok={ok} assigned={!string.IsNullOrEmpty(characterId)}.");
        return ok;
    }

    /// <summary>Drops an override so the line returns to automatic attribution.</summary>
    [JsonRpcMethod("dialogue/clearSpeaker")]
    public async Task<bool> ClearSpeakerAsync(string chapterGuid, string sceneId, string lineKey)
    {
        var ok = await BuildService().ClearSpeakerAsync(chapterGuid, sceneId, lineKey);
        Log.Info($"dialogue/clearSpeaker ok={ok}.");
        return ok;
    }

    /// <summary>
    /// Rewrites the words inside one line's quote marks in the scene file.
    /// <paramref name="originalText"/> is what the caller last displayed; the
    /// write is refused when the scene no longer reads that way, so an edit here
    /// cannot overwrite one made in the editor. A snapshot is taken first.
    /// </summary>
    [JsonRpcMethod("dialogue/updateLine")]
    public async Task<DialogueUpdateDto> UpdateLineAsync(
        string chapterGuid, string sceneId, string lineKey, string originalText, string newText)
    {
        var result = await BuildService()
            .UpdateLineAsync(chapterGuid, sceneId, lineKey, originalText, newText);

        if (result.Status == DialogueUpdateStatus.Updated)
        {
            var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
            _workspace.RaiseSceneSaved(chapter, scene);
        }

        Log.Info($"dialogue/updateLine status={result.Status} len={newText.Length}.");
        return new DialogueUpdateDto(result.Status.ToString(), result.LineKey);
    }

    /// <summary>The project's writing language, resolved exactly as the
    /// Inspector's scene analysis resolves it, so both read the same lexicon.</summary>
    private string WritingLanguage()
    {
        var overrides = _workspace.Projects.ProjectRoot == null
            ? null
            : _workspace.Projects.ProjectSettings.Overrides;
        return overrides?.AutoReplacementLanguage
               ?? _workspace.Settings.Settings.AutoReplacementLanguage
               ?? "en";
    }

    private DialogueIndexService BuildService()
        => new(
            _workspace.Projects,
            new SnapshotService(_workspace.Projects, _workspace.FileService));

    /// <summary>Which speaker the returned groups belong to. Mirrors the
    /// service's own fallback so the renderer can highlight the right roster
    /// row without repeating the rule.</summary>
    private static string? ResolveSelected(string? requested, DialogueIndex index)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            if (requested == DialogueIndexService.UnassignedSpeakerId)
                return index.UnassignedCount > 0 ? requested : null;
            return index.Speakers.Any(s => s.CharacterId == requested) ? requested : null;
        }
        return index.Speakers.Count > 0 ? index.Speakers[0].CharacterId : null;
    }
}

/// <summary>A character who speaks, with their line count for the roster.</summary>
public sealed record DialogueSpeakerDto(string CharacterId, string Name, int LineCount);

/// <summary>A character the writer can reassign a line to — the whole Codex
/// cast, including those with no attributed lines yet.</summary>
public sealed record DialogueCharacterDto(string Id, string Name);

/// <summary>Another character the line might belong to, with their share of the
/// evidence. Shares sum to 100 across a line's candidates.</summary>
public sealed record DialogueCandidateDto(string CharacterId, int Percent);

/// <summary>One spoken line. <see cref="Confidence"/> is the
/// <c>DialogueConfidence</c> name ("Manual", "High", "Inferred", "Medium",
/// "Low", "None"), which the renderer localizes. <see cref="Editable"/> is false
/// when the line carries markup and can only be changed in the editor.
/// <see cref="Candidates"/> is empty where the prose names the speaker outright,
/// and otherwise ranks who else it might be so the writer can fix it in a
/// click.</summary>
public sealed record DialogueLineDto(
    string LineKey, string Text, string Confidence, bool Editable,
    string ContextBefore, string ContextAfter, DialogueCandidateDto[] Candidates);

public sealed record DialogueSceneDto(
    string ChapterGuid, string SceneId, string ChapterTitle, string SceneTitle,
    string StoryDate, DialogueLineDto[] Lines);

/// <summary>A run of scenes at one point in story time. <see cref="StoryDate"/>
/// is blank for the run before any date is known.</summary>
public sealed record DialogueGroupDto(string StoryDate, DialogueSceneDto[] Scenes);

public sealed record DialogueIndexDto(
    DialogueSpeakerDto[] Speakers, DialogueCharacterDto[] Characters,
    int UnassignedCount, string? SelectedId, DialogueGroupDto[] Groups);

/// <summary>Outcome of a line edit. <see cref="Status"/> is "Updated", "Stale"
/// (the scene changed underneath the caller) or "NotEditable".
/// <see cref="LineKey"/> is the edited line's new key when it changed.</summary>
public sealed record DialogueUpdateDto(string Status, string? LineKey);
