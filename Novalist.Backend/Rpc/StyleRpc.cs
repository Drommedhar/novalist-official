using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Deterministic offline craft reports over a scene, a chapter, or the book.</summary>
public sealed class StyleRpc
{
    private readonly Workspace _workspace;

    public StyleRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private string Language => _workspace.Settings.Effective.AutoReplacementLanguage;

    /// <summary>
    /// Deterministic continuity checks over the whole book.
    ///
    /// Everything else here is about prose in one scene. This is the only report
    /// that reads the book as a book, which is where a character standing two
    /// chapters after their own funeral actually shows up.
    /// </summary>
    [JsonRpcMethod("style/continuity")]
    public async Task<ContinuityReportDto> ContinuityAsync()
    {
        var projects = _workspace.Projects;
        var entities = new Core.Services.EntityService(projects);

        // Reading order is the spine of two of the three rules, so it is built
        // once and everything else is looked up against it.
        var scenes = new List<Core.Services.GateScene>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        foreach (var chapter in projects.GetChaptersOrdered())
        {
            foreach (var scene in projects.GetScenesForChapter(chapter.Guid))
            {
                if (scene.ArchivedAt != null) continue;
                var date = string.IsNullOrEmpty(scene.DateRange?.Start)
                    ? (string.IsNullOrEmpty(scene.Date) ? chapter.Date : scene.Date)
                    : scene.DateRange!.Start;
                scenes.Add(new Core.Services.GateScene(
                    chapter.Guid, scene.Id, index, [.. scene.Cast ?? []], date, scene.NarrativeMode));
                indexOf[$"{chapter.Guid}/{scene.Id}"] = index;
                indexOf[chapter.Guid] = indexOf.TryGetValue(chapter.Guid, out var first) ? first : index;
                index++;
            }
        }

        int? ReadingIndexOf(string chapterGuid, string? sceneId)
            => sceneId != null && indexOf.TryGetValue($"{chapterGuid}/{sceneId}", out var exact)
                ? exact
                : indexOf.TryGetValue(chapterGuid, out var chapterStart) ? chapterStart : null;

        var all = new List<Core.Models.IEntityData>();
        all.AddRange(await entities.LoadCharactersAsync());
        all.AddRange(await entities.LoadLocationsAsync());
        all.AddRange(await entities.LoadItemsAsync());
        all.AddRange(await entities.LoadLoreAsync());
        foreach (var type in entities.GetCustomEntityTypes())
            all.AddRange(await entities.LoadCustomEntitiesAsync(type.TypeKey));

        var gateEntities = all
            .Select(e => new Core.Services.GateEntity(
                e.Id,
                e.DisplayName,
                Core.Services.ContinuityGates.GoneFrom(e.StateOverrides ?? [], ReadingIndexOf)))
            .ToList();

        var disabled = new HashSet<string>(
            projects.ProjectSettings.DisabledContinuityRules ?? [], StringComparer.Ordinal);
        var findings = Core.Services.ContinuityGates.Run(scenes, gateEntities, disabled);

        // Titles resolved here rather than in the gates: the engine deals in
        // ids so it stays testable without a project behind it.
        var titles = projects.GetChaptersOrdered()
            .ToDictionary(c => c.Guid, c => c.Title, StringComparer.Ordinal);
        var sceneTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chapter in projects.GetChaptersOrdered())
            foreach (var scene in projects.GetScenesForChapter(chapter.Guid))
                sceneTitles[scene.Id] = scene.Title;

        return new ContinuityReportDto(
            [.. findings.Select(f => new ContinuityFindingDto(
                f.RuleId,
                f.ChapterGuid,
                f.SceneId,
                titles.GetValueOrDefault(f.ChapterGuid, string.Empty),
                sceneTitles.GetValueOrDefault(f.SceneId, string.Empty),
                f.Subject,
                f.Detail))],
            [.. Core.Services.ContinuityGates.AllRules],
            [.. disabled]);
    }

    /// <summary>Turns one continuity rule on or off for this book.</summary>
    [JsonRpcMethod("style/setContinuityRule")]
    public async Task<ContinuityReportDto> SetContinuityRuleAsync(string ruleId, bool enabled)
    {
        var settings = _workspace.Projects.ProjectSettings;
        settings.DisabledContinuityRules ??= [];
        if (enabled) settings.DisabledContinuityRules.RemoveAll(r => r == ruleId);
        else if (Core.Services.ContinuityGates.AllRules.Contains(ruleId)
            && !settings.DisabledContinuityRules.Contains(ruleId))
            settings.DisabledContinuityRules.Add(ruleId);

        await _workspace.Projects.SaveProjectSettingsAsync();
        return await ContinuityAsync();
    }

    /// <summary>
    /// Grades every sentence in the text the editor is showing. Offsets are
    /// into the string as passed, because the editor decorates that same string
    /// and any normalising here would shift every mark after it.
    /// </summary>
    [JsonRpcMethod("style/sentenceReadability")]
    public SentenceReadabilityDto[] SentenceReadabilityAsync(string? text)
        => [.. TextStatistics
            .GradeSentences(text ?? string.Empty, Language)
            .Select(s => new SentenceReadabilityDto(
                s.Offset, s.Length, s.Score, s.Level.ToString()))];

    /// <summary>The writer's own flagged words, counted alongside the bundled checks.</summary>
    private IReadOnlyCollection<string> WatchWords
        => _workspace.Settings.Settings.StyleWatchWords;

    /// <summary>The words the style report is watching for this writer.</summary>
    [JsonRpcMethod("style/watchWords")]
    public async Task<string[]> GetWatchWordsAsync()
    {
        await _workspace.Settings.LoadAsync();
        return [.. WatchWords];
    }

    /// <summary>
    /// Replaces the list. Blanks and repeats are dropped: an empty entry
    /// matches nothing and a repeat would count the same word twice.
    /// </summary>
    [JsonRpcMethod("style/setWatchWords")]
    public async Task<string[]> SetWatchWordsAsync(string[]? words)
    {
        await _workspace.Settings.LoadAsync();
        _workspace.Settings.Settings.StyleWatchWords = [.. (words ?? [])
            .Select(w => (w ?? string.Empty).Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        await _workspace.Settings.SaveAsync();
        return [.. WatchWords];
    }

    /// <param name="scope">
    /// "Everything" (the default), "ProseOnly" or "DialogueOnly". A character
    /// written to speak in cliches is not a writing problem, and a report that
    /// counts their lines alongside the narration says otherwise.
    /// </param>
    [JsonRpcMethod("style/scene")]
    public async Task<StyleReportDto> SceneAsync(
        string chapterGuid, string sceneId, string? scope = null)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        return ToDto(ProseStyleAnalyzer.Analyze(
            TextDiff.StripHtml(html), Language, WatchWords, ParseScope(scope)));
    }

    /// <summary>
    /// Reads a scene's prose against the point of view it is written in.
    ///
    /// Novalist stored a POV per scene and let the writer override it, and then
    /// nothing ever checked the prose against it - so a third-limited scene
    /// marked Mira could report what Tomas was thinking with no warning.
    /// </summary>
    [JsonRpcMethod("style/povCheck")]
    public async Task<PovReportDto> PovCheckAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);

        // Every character in the project, not only this scene's cast: the slip
        // this catches is naming somebody the scene never declared was there.
        var entities = new EntityService(_workspace.Projects);
        var names = new List<string>();
        foreach (var character in await entities.LoadCharactersAsync())
        {
            names.Add(character.Name);
            if (!string.IsNullOrWhiteSpace(character.DisplayName)) names.Add(character.DisplayName);
            names.AddRange(character.Aliases);
        }

        var report = PovConsistency.Analyze(
            TextDiff.StripHtml(html), scene.AnalysisOverrides?.Pov, names, Language);

        return new PovReportDto(
            report.Pov, report.Checked, report.SkippedBecause,
            [.. report.Slips.Select(s => new PovSlipDto(s.Name, s.Verb, s.Offset, s.Context))]);
    }

    /// <summary>An unknown scope reads as everything rather than as nothing.</summary>
    private static ProseScope ParseScope(string? scope)
        => Enum.TryParse<ProseScope>(scope, ignoreCase: true, out var parsed)
            ? parsed
            : ProseScope.Everything;

    /// <summary>
    /// Whole-book report. Scenes are concatenated so cross-scene repetition is
    /// visible, which is where the interesting habits show up.
    /// </summary>
    [JsonRpcMethod("style/book")]
    public async Task<StyleReportDto> BookAsync(string? chapterGuid = null, string? scope = null)
    {
        var text = new System.Text.StringBuilder();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
        {
            if (!string.IsNullOrEmpty(chapterGuid)
                && !string.Equals(chapter.Guid, chapterGuid, StringComparison.Ordinal))
                continue;

            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                text.AppendLine(TextDiff.StripHtml(html));
            }
        }

        return ToDto(ProseStyleAnalyzer.Analyze(
            text.ToString(), Language, WatchWords, ParseScope(scope)));
    }

    private static StyleFindingDto[] ToDto(IEnumerable<ProseStyleFinding> findings) =>
        [.. findings.Select(f => new StyleFindingDto(
            f.Key,
            f.Count,
            f.Per1000Words,
            f.Supported,
            [.. f.Examples.Select(e => new StyleHitDto(e.Text, e.Offset, e.Context))]))];

    private static StyleReportDto ToDto(ProseStyleReport r) =>
        new(
            r.Language,
            r.WordCount,
            r.SentenceCount,
            r.MeanSentenceWords,
            r.SentenceLengthStdDev,
            r.LongestSentenceWords,
            r.Scope.ToString(),
            r.ParagraphCount,
            r.MeanParagraphWords,
            r.ParagraphLengthStdDev,
            ToDto(r.Findings),
            ToDto(r.Senses));
}

public sealed record StyleHitDto(string Text, int Offset, string Context);

/// <summary>One graded sentence: where it is, how it scored, and which band.</summary>
public sealed record SentenceReadabilityDto(int Offset, int Length, int Score, string Level);

public sealed record StyleFindingDto(
    string Key, int Count, double Per1000Words, bool Supported, StyleHitDto[] Examples);

public sealed record StyleReportDto(
    string Language,
    int WordCount,
    int SentenceCount,
    double MeanSentenceWords,
    double SentenceLengthStdDev,
    int LongestSentenceWords,
    /// <summary>"Everything", "ProseOnly" or "DialogueOnly".</summary>
    string Scope,
    int ParagraphCount,
    double MeanParagraphWords,
    /// <summary>A chapter of identically-sized paragraphs reads as flat for the
    /// same reason a run of identically-sized sentences does.</summary>
    double ParagraphLengthStdDev,
    StyleFindingDto[] Findings,
    /// <summary>
    /// One row per sense, always all five and always in the same order. Kept
    /// apart from the findings because these are not problems: the reading is
    /// which senses the prose forgot, not which counts to reduce.
    /// </summary>
    StyleFindingDto[] Senses);

/// <summary>One place the narration entered somebody else's head.</summary>
public sealed record PovSlipDto(string Name, string Verb, int Offset, string Context);

/// <summary>
/// What a POV check found. <paramref name="Checked"/> is false when it could
/// not run - a zero from a check that never ran reads as a clean scene, which
/// is the worse failure.
/// </summary>
public sealed record PovReportDto(
    string Pov, bool Checked, string SkippedBecause, PovSlipDto[] Slips);

/// <summary>One continuity finding, with enough to jump to it.</summary>
public sealed record ContinuityFindingDto(
    string RuleId,
    string ChapterGuid,
    string SceneId,
    string ChapterTitle,
    string SceneTitle,
    string Subject,
    string Detail);

/// <summary>What the gates found, and which of them ran.</summary>
public sealed record ContinuityReportDto(
    IReadOnlyList<ContinuityFindingDto> Findings,
    IReadOnlyList<string> AllRules,
    IReadOnlyList<string> DisabledRules);
