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
public sealed class ContextRpc
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

        // Snapshot every chapter's normalized scene text (aggregate per chapter for
        // the mention matrix; the current scene's own text for entity + analysis).
        var chapters = projects.GetChaptersOrdered();
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
                {
                    currentContent = content;
                }
            }

            aggregateByChapter[chapter.Guid] = string.Join(
                Environment.NewLine + Environment.NewLine, contents);
        }

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
                matchedCharacters.Count,
                matchedLocations.Count,
                matchedItems.Count,
                matchedLore.Count,
                autoIntensity,
                autoEmotion.Key,
                dialogueRatio,
                wordCount,
                autoConflict,
                lexicon)
            : [];

        var overrides = targetScene.AnalysisOverrides;
        var pov = overrides?.Pov ?? autoPov;
        var emotion = overrides?.Emotion ?? autoEmotion.Key;
        var intensity = overrides?.Intensity ?? autoIntensity;
        var conflict = overrides?.Conflict ?? autoConflict;
        var tags = overrides?.Tags != null ? [.. overrides.Tags] : autoTags.ToArray();

        var analysis = new SceneAnalysisDto(
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
            // The book says what it is written in; this scene either agrees or
            // it does not. Silent when the book declares nothing, when the
            // scene is too short to be evidence, or when the language does not
            // mark tense with verb forms - being told a four-sentence scene is
            // broken is worse than not being told anything.
            VoiceDrift(currentContent, lexicon));

        return new SceneContextDto(characterCards, locationCards, itemCards, loreCards, mentionRows, analysis);
    }

    /// <summary>
    /// Where this scene reads differently from what the book declares, or null
    /// when there is nothing to say. Confidence rides along so a weak reading
    /// can be shown as a question rather than a verdict.
    /// </summary>
    private VoiceDriftDto? VoiceDrift(string content, SceneAnalysisLexicon? lexicon)
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return null;

        var person = NarrativeVoiceService.CheckPerson(book.NarrativePerson, content, lexicon);
        var tense = NarrativeVoiceService.CheckTense(book.Tense, content, lexicon);
        if (person == null && tense == null) return null;

        return new VoiceDriftDto(
            book.NarrativePerson,
            book.Tense,
            person?.Reading.ToString().ToLowerInvariant() ?? string.Empty,
            tense?.Reading.ToString().ToLowerInvariant() ?? string.Empty,
            person is { Agrees: false },
            tense is { Agrees: false },
            Math.Max(person?.Confidence ?? 0, tense?.Confidence ?? 0));
    }

    /// <summary>The project's writing language (the same setting that drives
    /// auto-replacements and the readability score).</summary>
    private string WritingLanguage()
    {
        var overrides = _workspace.Projects.ProjectRoot == null
            ? null
            : _workspace.Projects.ProjectSettings.Overrides;
        return overrides?.AutoReplacementLanguage
               ?? _workspace.Settings.Settings.AutoReplacementLanguage
               ?? "en";
    }

    /// <summary>Whether keyword-driven analysis (emotion, intensity, conflict, tags)
    /// is available for a language — that is, whether a lexicon ships for it.</summary>
    internal static bool SupportsKeywordAnalysis(string? language)
        => SceneAnalysisLexicon.Supports(language);

    private static MentionRowDto[] BuildMentionRows(
        IReadOnlyList<MatchedSource> matchedCharacters,
        IReadOnlyList<ChapterData> chapters,
        IReadOnlyDictionary<string, string> aggregateByChapter,
        ChapterData chapter,
        SceneData scene)
    {
        if (matchedCharacters.Count == 0)
        {
            return [];
        }

        var rows = new List<MentionRowDto>(matchedCharacters.Count);
        foreach (var matched in matchedCharacters)
        {
            var character = (CharacterData)matched.Source.Entity;
            var display = ResolveCharacterDisplay(character, chapter, scene);
            var cells = new List<MentionCellDto>(chapters.Count);
            var mentions = new bool[chapters.Count];

            for (var chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                var candidateChapter = chapters[chapterIndex];
                var present = aggregateByChapter.TryGetValue(candidateChapter.Guid, out var aggregate)
                    && matched.Source.IsMatch(aggregate);
                mentions[chapterIndex] = present;

                cells.Add(new MentionCellDto(
                    (chapterIndex + 1).ToString(),
                    present,
                    string.Equals(candidateChapter.Guid, chapter.Guid, StringComparison.OrdinalIgnoreCase)));
            }

            var gap = 0;
            for (var chapterIndex = mentions.Length - 1; chapterIndex >= 0; chapterIndex--)
            {
                if (mentions[chapterIndex])
                {
                    break;
                }

                gap++;
            }

            rows.Add(new MentionRowDto(display.Name, cells.ToArray(), gap));
        }

        return rows.ToArray();
    }

    private static IReadOnlyList<string> BuildPovOptions(
        IReadOnlyList<MatchedSource> matchedCharacters,
        IReadOnlyList<EntitySource> characterSources,
        ChapterData chapter,
        SceneData scene)
    {
        var chapterNames = matchedCharacters
            .Select(match => (CharacterData)match.Source.Entity)
            .Select(character => ResolveCharacterDisplay(character, chapter, scene).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chapterNames.Count > 0)
        {
            return chapterNames;
        }

        return characterSources
            .Select(source => (CharacterData)source.Entity)
            .Select(character => ResolveCharacterDisplay(character, chapter, scene).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DetectPov(
        string content,
        IReadOnlyList<MatchedSource> currentSceneCharacters,
        ChapterData chapter,
        SceneData scene,
        SceneAnalysisLexicon? lexicon)
    {
        if (currentSceneCharacters.Count == 0)
        {
            // Without a lexicon there are no pronouns to count, so no first-person
            // guess; the character-name path below works in every language.
            return IsFirstPerson(content, lexicon)
                ? "pov.firstPerson"
                : string.Empty;
        }

        var bestMatch = currentSceneCharacters
            .Select(match => new
            {
                Match = match,
                Count = match.Source.FindMentionCount(content)
            })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Match.MatchIndex)
            .First();

        var character = (CharacterData)bestMatch.Match.Source.Entity;
        return ResolveCharacterDisplay(character, chapter, scene).Name;
    }

    private static SceneEmotionSnapshot DetectEmotion(
        string content, int intensity, SceneAnalysisLexicon lexicon)
    {
        var normalized = content.ToLowerInvariant();

        var best = lexicon.Emotions
            .Select(profile => new SceneEmotionSnapshot(
                profile.Key,
                profile.Key,
                profile.Words.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal))))
            .OrderByDescending(entry => entry.Score)
            .FirstOrDefault() ?? new SceneEmotionSnapshot(string.Empty, string.Empty, 0);

        if (best.Score <= 0)
        {
            // Fall back to a mood implied by the intensity, but only to a key the
            // lexicon actually declares.
            var fallback = intensity switch
            {
                <= -6 => "tense",
                >= 6 => "triumphant",
                _ => "neutral"
            };
            return lexicon.EmotionKeys.Contains(fallback, StringComparer.Ordinal)
                ? new SceneEmotionSnapshot(fallback, fallback, 1)
                : new SceneEmotionSnapshot(string.Empty, string.Empty, 0);
        }

        return best;
    }

    private static int ComputeIntensity(string content, SceneAnalysisLexicon lexicon)
    {
        var normalized = content.ToLowerInvariant();
        var positiveCount = lexicon.Positive.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var negativeCount = lexicon.Negative.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var conflictCount = lexicon.Conflict.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        // Count the fullwidth exclamation mark too, for CJK prose.
        var exclamations = content.Count(character => character is '!' or '！');

        var score = ((positiveCount - negativeCount) * 2) - conflictCount;
        if (score > 0)
        {
            score += Math.Min(2, exclamations);
        }
        else if (score < 0)
        {
            score -= Math.Min(2, exclamations);
        }
        else if (conflictCount > 0)
        {
            score = -Math.Min(6, conflictCount + exclamations);
        }

        return Math.Clamp(score, -10, 10);
    }

    /// <summary>Four or more first-person pronouns reads as a first-person scene.</summary>
    private static bool IsFirstPerson(string content, SceneAnalysisLexicon? lexicon)
        => lexicon != null && lexicon.FirstPerson.Matches(content).Count >= 4;

    private static string ExtractConflictSnippet(string content, SceneAnalysisLexicon lexicon)
    {
        foreach (var sentence in ExtractSentences(content))
        {
            var normalized = sentence.ToLowerInvariant();
            if (!lexicon.Conflict.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
            {
                continue;
            }

            return TrimExcerpt(sentence, 92);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildSceneTags(
        string content,
        int characterCount,
        int locationCount,
        int itemCount,
        int loreCount,
        int intensity,
        string emotionLabel,
        double dialogueRatio,
        int wordCount,
        string conflict,
        SceneAnalysisLexicon? lexicon)
    {
        var tags = new List<string>();

        if (dialogueRatio >= 0.35)
        {
            tags.Add("sceneTag.dialogue");
        }

        if (Math.Abs(intensity) >= 6)
        {
            tags.Add("sceneTag.highTension");
        }

        if (!string.IsNullOrWhiteSpace(conflict))
        {
            tags.Add("sceneTag.conflict");
        }

        if (characterCount >= 3)
        {
            tags.Add("sceneTag.ensemble");
        }

        if (locationCount >= 2)
        {
            tags.Add("sceneTag.travel");
        }

        if (itemCount + loreCount >= 2)
        {
            tags.Add("sceneTag.worldbuilding");
        }

        if (IsFirstPerson(content, lexicon))
        {
            tags.Add("sceneTag.interior");
        }

        if (wordCount >= 1200)
        {
            tags.Add("sceneTag.longScene");
        }

        if (!string.Equals(emotionLabel, "neutral", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add($"emotion.{emotionLabel}");
        }

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static double ComputeDialogueRatio(string content, int wordCount)
    {
        if (wordCount <= 0)
        {
            return 0;
        }

        var dialogueChars = DialogueRegex.Matches(content)
            .Select(match => match.Length)
            .Sum();

        // Whitespace-collapsed length is always positive when wordCount > 0, so the
        // Avalonia VM's extra totalChars<=0 guard is dead here and intentionally dropped.
        var totalChars = Regex.Replace(content, "\\s+", " ").Length;
        return Math.Clamp(dialogueChars / (double)totalChars, 0, 1);
    }

    private static double ComputeAverageSentenceLength(string content, int wordCount)
    {
        if (wordCount <= 0)
        {
            return 0;
        }

        var sentenceCount = Math.Max(1, SentenceRegex.Matches(content).Count);
        return Math.Round(wordCount / (double)sentenceCount, 1);
    }

    private static IEnumerable<string> ExtractSentences(string content)
        => SentenceRegex.Matches(content)
            .Select(match => match.Value.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence));

    private static int CountWords(string content)
        => WordRegex.Matches(content).Count;

    private static string NormalizeSceneContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (!content.TrimStart().StartsWith('<'))
        {
            return content;
        }

        var text = Regex.Replace(content, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    private static string TrimExcerpt(string value, int maxLength)
    {
        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static CharacterDisplay ResolveCharacterDisplay(CharacterData character, ChapterData chapter, SceneData scene)
    {
        var match = character.ChapterOverrides.FirstOrDefault(overrideEntry =>
            (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
             || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
            && string.Equals(overrideEntry.Scene, scene.Title, StringComparison.OrdinalIgnoreCase))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                (string.Equals(overrideEntry.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(overrideEntry.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(overrideEntry.Scene)
                && string.IsNullOrWhiteSpace(overrideEntry.Act))
            ?? character.ChapterOverrides.FirstOrDefault(overrideEntry =>
                string.Equals(overrideEntry.Act, chapter.Act, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(overrideEntry.Chapter)
                && string.IsNullOrWhiteSpace(overrideEntry.Scene));

        var displayName = string.IsNullOrWhiteSpace(match?.Name) ? character.Name : match.Name!;
        var displaySurname = string.IsNullOrWhiteSpace(match?.Surname) ? character.Surname : match.Surname!;
        var name = string.IsNullOrWhiteSpace(displaySurname) ? displayName : $"{displayName} {displaySurname}".Trim();
        var role = string.IsNullOrWhiteSpace(match?.Role) ? character.Role : match.Role!;
        var gender = string.IsNullOrWhiteSpace(match?.Gender) ? character.Gender : match.Gender!;
        var age = ResolveDisplayAge(character, match, chapter, scene);
        return new CharacterDisplay(name, role, character.Group, gender, age);
    }

    /// <summary>Resolves a character's displayed age: when age is stored as a birth
    /// date (<c>AgeMode == "date"</c>), it is computed relative to the scene's story
    /// date (else the chapter's, else today) via <see cref="AgeComputation"/>;
    /// otherwise the override age wins over the base. Ported from
    /// <c>ContextSidebarViewModel.ResolveDisplayAge</c>.</summary>
    private static string ResolveDisplayAge(
        CharacterData character, CharacterOverride? match, ChapterData chapter, SceneData scene)
    {
        if (string.Equals(character.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(character.BirthDate))
        {
            var referenceDate = !string.IsNullOrWhiteSpace(scene.Date) ? scene.Date
                : !string.IsNullOrWhiteSpace(chapter.Date) ? chapter.Date
                : null;
            var computed = AgeComputation.ComputeAge(character.BirthDate, referenceDate,
                character.AgeIntervalUnit ?? IntervalUnit.Years);
            if (!string.IsNullOrWhiteSpace(computed))
                return computed;
        }

        return string.IsNullOrWhiteSpace(match?.Age) ? character.Age : match.Age!;
    }

    private static IEnumerable<string> GetCharacterAliases(CharacterData character)
    {
        yield return character.DisplayName;

        if (!string.Equals(character.Name, character.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            yield return character.Name;
        }
    }

    private static IReadOnlyList<Regex> BuildPatterns(IEnumerable<string> aliases)
        => aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(NormalizeEntityReference)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(alias => new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
            .ToList();

    private static string NormalizeEntityReference(string? value)
        => (value ?? string.Empty)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static IReadOnlyList<MatchedSource> MatchSources(string content, IEnumerable<EntitySource> sources)
        => sources
            .Select(source => new MatchedSource(source, source.FindFirstMatchIndex(content)))
            .Where(entry => entry.MatchIndex.HasValue)
            .OrderBy(entry => entry.MatchIndex)
            .ThenBy(entry => entry.Source.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string? ResolveImage(IReadOnlyList<EntityImage> images)
    {
        var image = images.FirstOrDefault();
        return image == null ? null : _entities.ResolveProjectRelativeImage(image.Path);
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class EntitySource
    {
        public EntitySource(object entity, string sortKey, IReadOnlyList<Regex> patterns)
        {
            Entity = entity;
            SortKey = sortKey;
            Patterns = patterns;
        }

        public object Entity { get; }
        public string SortKey { get; }
        public IReadOnlyList<Regex> Patterns { get; }

        public bool IsMatch(string content) => FindFirstMatchIndex(content).HasValue;

        public int? FindFirstMatchIndex(string content)
        {
            int? bestIndex = null;
            foreach (var pattern in Patterns)
            {
                var match = pattern.Match(content);
                if (!match.Success)
                {
                    continue;
                }

                if (!bestIndex.HasValue || match.Index < bestIndex.Value)
                {
                    bestIndex = match.Index;
                }
            }

            return bestIndex;
        }

        // Only ever called on matched sources, which by construction have >=1 pattern.
        public int FindMentionCount(string content)
            => Patterns.Max(pattern => pattern.Matches(content).Count);
    }

    private sealed record MatchedSource(EntitySource Source, int? MatchIndex);

    private sealed record EmotionProfile(string Key, string Label, IReadOnlyList<string> Keywords);

    private sealed record SceneEmotionSnapshot(string Key, string Label, int Score);

    private sealed record CharacterDisplay(string Name, string Role, string Group, string Gender, string Age);
}

public sealed record SceneContextDto(
    EntityCardDto[] Characters,
    EntityCardDto[] Locations,
    EntityCardDto[] Items,
    EntityCardDto[] Lore,
    MentionRowDto[] MentionRows,
    SceneAnalysisDto Analysis);

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
