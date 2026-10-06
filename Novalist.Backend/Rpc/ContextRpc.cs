using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Scene context and analysis for the Inspector. Ported from the Avalonia
/// <c>ContextSidebarViewModel</c>: the regexes and math are byte-faithful copies
/// so headless results match the desktop app. The keyword lists and emotion keys
/// that were hardcoded (and English-only) now come from
/// <see cref="SceneAnalysisLexicon"/>, one JSON per writing language, so the
/// analysis works in every language that ships one.
///
/// The one unavoidable difference is display resolution: the Avalonia VM runs
/// tags / emotions / the first-person POV label through <c>Loc.T</c> to produce
/// localized strings. The backend has no localizer, so it emits the underlying
/// identifiers instead — the emotion key (e.g. <c>"tense"</c>), the tag keys
/// (e.g. <c>"sceneTag.dialogue"</c>, <c>"emotion.sorrowful"</c>), and
/// <c>"pov.firstPerson"</c> for the first-person fallback — and the renderer
/// localizes. Emotion was already emitted as a key in the DTO contract, so this
/// keeps tags/POV consistent with it.
/// </summary>
public sealed partial class ContextRpc
{
    // Shared with the Dialogue view so both agree on what counts as a quoted line.
    private static readonly Regex DialogueRegex = DialogueScanner.QuoteRegex;

    // Terminators include the CJK forms so Chinese prose splits into sentences too.
    private static readonly Regex SentenceRegex = new(
        @"[^.!?。！？]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public ContextRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("context/analyze")]
    public async Task<SceneContextDto> AnalyzeAsync(string chapterGuid, string sceneId)
    {
        var (targetChapter, targetScene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var projects = _workspace.Projects;

        var characterSources = (await _entities.LoadCharactersAsync())
            .Select(character => new EntitySource(character, character.DisplayName, BuildPatterns(GetCharacterAliases(character))))
            .ToList();
        var locationSources = (await _entities.LoadLocationsAsync())
            .Select(location => new EntitySource(location, location.Name, BuildPatterns([location.Name])))
            .ToList();
        var itemSources = (await _entities.LoadItemsAsync())
            .Select(item => new EntitySource(item, item.Name, BuildPatterns([item.Name])))
            .ToList();
        var loreSources = (await _entities.LoadLoreAsync())
            .Select(lore => new EntitySource(lore, lore.Name, BuildPatterns([lore.Name])))
            .ToList();

        var chapters = projects.GetChaptersOrdered();
        var (aggregateByChapter, currentContent) = await ReadContextContentAsync(chapters, targetChapter, targetScene);

        var matchedCharacters = MatchSources(currentContent, characterSources);
        var matchedLocations = MatchSources(currentContent, locationSources);
        var matchedItems = MatchSources(currentContent, itemSources);
        var matchedLore = MatchSources(currentContent, loreSources);

        var characterCards = matchedCharacters
            .Select(match =>
            {
                var character = (CharacterData)match.Source.Entity;
                var display = ResolveCharacterDisplay(character, targetChapter, targetScene);
                return new EntityCardDto(
                    character.Id, display.Name, display.Role, NullIfEmpty(display.Group), ResolveImage(character.Images),
                    NullIfEmpty(display.Gender), NullIfEmpty(display.Age));
            })
            .ToArray();

        var locationCards = matchedLocations
            .Select(match =>
            {
                var location = (LocationData)match.Source.Entity;
                return new EntityCardDto(
                    location.Id, location.Name, location.Type,
                    NullIfEmpty(NormalizeEntityReference(location.Parent)), ResolveImage(location.Images));
            })
            .ToArray();

        var itemCards = matchedItems
            .Select(match =>
            {
                var item = (ItemData)match.Source.Entity;
                return new EntityCardDto(item.Id, item.Name, item.Type, null, ResolveImage(item.Images));
            })
            .ToArray();

        var loreCards = matchedLore
            .Select(match =>
            {
                var lore = (LoreData)match.Source.Entity;
                return new EntityCardDto(lore.Id, lore.Name, lore.Category, null, ResolveImage(lore.Images));
            })
            .ToArray();

        var mentionRows = BuildMentionRows(matchedCharacters, chapters, aggregateByChapter, targetChapter, targetScene);
        var povOptions = BuildPovOptions(matchedCharacters, characterSources, targetChapter, targetScene);
        var analysis = AnalyzeText(currentContent, matchedCharacters, targetChapter, targetScene, povOptions,
            (matchedLocations.Count, matchedItems.Count, matchedLore.Count));
        return new SceneContextDto(
            characterCards, locationCards, itemCards, loreCards, mentionRows, analysis,
            SuggestResearch(characterCards, locationCards, itemCards, loreCards, analysis.Tags));
    }

    private async Task<(Dictionary<string, string> Chapters, string Current)> ReadContextContentAsync(
        IReadOnlyList<ChapterData> chapters, ChapterData targetChapter, SceneData targetScene)
    {
        var projects = _workspace.Projects;
        var aggregateByChapter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentContent = string.Empty;
        foreach (var chapter in chapters)
        {
            var scenes = projects.GetScenesForChapter(chapter.Guid);
            var contents = new List<string>(scenes.Count);
            foreach (var scene in scenes)
            {
                var content = NormalizeSceneContent(await projects.ReadSceneContentAsync(chapter, scene));
                contents.Add(content);
                if (string.Equals(chapter.Guid, targetChapter.Guid, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(scene.Id, targetScene.Id, StringComparison.OrdinalIgnoreCase))
                    currentContent = content;
            }
            aggregateByChapter[chapter.Guid] = string.Join(Environment.NewLine + Environment.NewLine, contents);
        }
        return (aggregateByChapter, currentContent);
    }

    private SceneAnalysisDto AnalyzeText(string currentContent, IReadOnlyList<MatchedSource> matchedCharacters,
        ChapterData targetChapter, SceneData targetScene, IReadOnlyList<string> povOptions,
        (int Locations, int Items, int Lore) matchedCounts)
    {
        var wordCount = CountWords(currentContent);
        var dialogueRatio = ComputeDialogueRatio(currentContent, wordCount);
        var avgSentenceLength = ComputeAverageSentenceLength(currentContent, wordCount);
        // Emotion, intensity, conflict and the derived tags are keyword-driven, so
        // they need a lexicon for the project's writing language. Every language
        // shipping Resources/Analysis/analysis.<tag>.json gets full analysis; a
        // language with no lexicon is left blank (with a note in the UI) rather than
        // scored against another language's words. Overrides work everywhere.
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var keywordAnalysis = lexicon != null;

        var autoIntensity = lexicon != null ? ComputeIntensity(currentContent, lexicon) : 0;
        var autoEmotion = lexicon != null
            ? DetectEmotion(currentContent, autoIntensity, lexicon)
            : new SceneEmotionSnapshot(string.Empty, string.Empty, 0);
        var autoPov = DetectPov(currentContent, matchedCharacters, targetChapter, targetScene, lexicon);
        var autoConflict = lexicon != null ? ExtractConflictSnippet(currentContent, lexicon) : string.Empty;
        var autoTags = lexicon != null
            ? BuildSceneTags(
                currentContent,
                (matchedCharacters.Count, matchedCounts.Locations, matchedCounts.Items, matchedCounts.Lore),
                (autoIntensity, autoEmotion.Key, dialogueRatio, wordCount, autoConflict),
                lexicon)
            : [];

        var overrides = targetScene.AnalysisOverrides;
        var pov = overrides?.Pov ?? autoPov;
        var emotion = overrides?.Emotion ?? autoEmotion.Key;
        var intensity = overrides?.Intensity ?? autoIntensity;
        var conflict = overrides?.Conflict ?? autoConflict;
        var tags = overrides?.Tags != null ? [.. overrides.Tags] : autoTags.ToArray();

        return new SceneAnalysisDto(
            pov,
            povOptions.ToArray(),
            emotion,
            // The lexicon declares the emotion keys the UI offers; without one, only
            // whatever the writer already set.
            (lexicon?.EmotionKeys ?? (emotion.Length > 0 ? [emotion] : [])).ToArray(),
            intensity,
            conflict,
            tags,
            (int)Math.Round(dialogueRatio * 100d),
            avgSentenceLength,
            wordCount,
            keywordAnalysis,
            VoiceDrift(currentContent, lexicon));

    }

    /// <summary>
    /// The research this scene is about, from what it already names: the entries
    /// the scene involves and its own tags. Nothing is guessed and no model is
    /// asked - a suggestion the writer has to double-check costs more than none.
    /// </summary>
    private ResearchSuggestionDto[] SuggestResearch(
        EntityCardDto[] characters, EntityCardDto[] locations,
        EntityCardDto[] items, EntityCardDto[] lore, string[] tags)
    {
        var cards = characters.Concat(locations).Concat(items).Concat(lore).ToList();
        if (cards.Count == 0 && tags.Length == 0) return [];

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards) names[card.Id] = card.Name;

        var research = new ResearchService(_workspace.Projects, _workspace.FileService);
        return [.. SceneResearch
            .Suggest(research.GetAll(), names.Keys, tags, names)
            .Select(s => new ResearchSuggestionDto(
                s.Item.Id, s.Item.Title, s.Item.Type.ToString(), s.Reason))];
    }
}

public sealed record SceneContextDto(
    EntityCardDto[] Characters,
    EntityCardDto[] Locations,
    EntityCardDto[] Items,
    EntityCardDto[] Lore,
    MentionRowDto[] MentionRows,
    SceneAnalysisDto Analysis,
    ResearchSuggestionDto[] Research);

/// <summary>
/// A research item the open scene is about. <paramref name="Reason"/> is the
/// entity name or tag that matched, shown beside it - a list of titles with no
/// reason has to be opened one by one to find out why it is there.
/// </summary>
public sealed record ResearchSuggestionDto(string Id, string Title, string Type, string Reason);

public sealed record EntityCardDto(
    string Id, string Name, string Detail, string? Secondary, string? ImagePath,
    string? Gender = null, string? Age = null);

public sealed record MentionRowDto(string Name, MentionCellDto[] Cells, int LastSeenChaptersAgo);

public sealed record MentionCellDto(string ChapterLabel, bool Present, bool Current);

public sealed record SceneAnalysisDto(
    string Pov,
    string[] PovOptions,
    string Emotion,
    string[] EmotionKeys,
    int Intensity,
    string Conflict,
    string[] Tags,
    int DialoguePercent,
    double AvgSentenceLength,
    int WordCount,
    /// <summary>False when the project's writing language is not English, in which
    /// case emotion/intensity/conflict/tags are not auto-detected (the keyword
    /// lists are English) and are left for the writer to set.</summary>
    bool KeywordAnalysisSupported,
    /// <summary>How this scene sits against the book's declared voice, or null
    /// when the book declares none.</summary>
    VoiceDriftDto? VoiceDrift);

/// <summary>
/// A scene measured against the book's declaration.
///
/// <c>PersonReading</c> and <c>TenseReading</c> are "unknown" where the prose is
/// too short to be evidence or the language does not mark it, and nothing is
/// flagged in that case.
/// </summary>
public sealed record VoiceDriftDto(
    string DeclaredPerson,
    string DeclaredTense,
    string PersonReading,
    string TenseReading,
    bool PersonDrifts,
    bool TenseDrifts,
    /// <summary>0-100. Below roughly 40 this is a question, not a verdict.</summary>
    int Confidence);
