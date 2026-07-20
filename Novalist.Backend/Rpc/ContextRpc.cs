using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Scene context and analysis for the Inspector. Ported from the Avalonia
/// <c>ContextSidebarViewModel</c>: the regexes, keyword lists, EmotionProfiles,
/// and math are byte-faithful copies so headless results match the desktop app.
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
    private static readonly Regex DialogueRegex = new(
        "(?:\"[^\"]*\"|“[^”]*”|„[^“]*“|«[^»]*»|»[^«]*«|‹[^›]*›|‚[^‘]*‘)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex SentenceRegex = new(
        @"[^.!?]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FirstPersonRegex = new(
        @"\b(i|me|my|mine|myself|we|us|our|ours|ourselves)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] PositiveWords =
    [
        "hope",
        "joy",
        "warm",
        "smile",
        "relief",
        "victory",
        "triumph",
        "laugh",
        "laughing",
        "love",
        "gentle",
        "bright",
        "calm",
        "peace",
        "safe"
    ];

    private static readonly string[] NegativeWords =
    [
        "fear",
        "panic",
        "anger",
        "angry",
        "blood",
        "hurt",
        "threat",
        "danger",
        "despair",
        "sad",
        "grief",
        "dark",
        "cold",
        "cry",
        "scream"
    ];

    private static readonly string[] ConflictKeywords =
    [
        "argue",
        "battle",
        "chase",
        "clash",
        "conflict",
        "demand",
        "fight",
        "flee",
        "force",
        "hide",
        "refuse",
        "secret",
        "struggle",
        "threat",
        "warn"
    ];

    private static readonly IReadOnlyList<EmotionProfile> EmotionProfiles =
    [
        new("neutral", "Neutral", ["steady", "plain", "quiet", "routine", "settled"]),
        new("tense", "Tense", ["tense", "edge", "pressure", "alarm", "strain", "uneasy"]),
        new("joyful", "Joyful", ["joy", "glad", "celebrate", "delight", "smile", "laugh"]),
        new("melancholic", "Melancholic", ["melancholy", "lonely", "empty", "wistful", "faded"]),
        new("angry", "Angry", ["anger", "furious", "rage", "snap", "resent", "spite"]),
        new("fearful", "Fearful", ["fear", "panic", "terror", "dread", "afraid", "shiver"]),
        new("romantic", "Romantic", ["kiss", "touch", "beloved", "heart", "desire", "tender"]),
        new("mysterious", "Mysterious", ["shadow", "mystery", "secret", "strange", "unknown", "whisper"]),
        new("humorous", "Humorous", ["joke", "laugh", "grin", "tease", "comic", "amused"]),
        new("hopeful", "Hopeful", ["hope", "promise", "rise", "chance", "believe", "future"]),
        new("desperate", "Desperate", ["desperate", "last", "plead", "beg", "hopeless", "breaking"]),
        new("peaceful", "Peaceful", ["peace", "calm", "still", "soft", "rest", "gentle"]),
        new("chaotic", "Chaotic", ["chaos", "riot", "wild", "fracture", "spiral", "rattle"]),
        new("sorrowful", "Sorrowful", ["sorrow", "grief", "weep", "mourning", "ache", "loss"]),
        new("triumphant", "Triumphant", ["triumph", "victory", "won", "conquer", "defiant", "surge"]),
        new("somber", "Somber", ["somber", "grim", "mournful", "bleak", "subdued", "solemn", "heavy"])
    ];

    private static readonly IReadOnlyList<string> EmotionKeys = EmotionProfiles
        .Select(profile => profile.Key)
        .ToList();

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
        var autoIntensity = ComputeIntensity(currentContent);
        var autoEmotion = DetectEmotion(currentContent, autoIntensity);
        var autoPov = DetectPov(currentContent, matchedCharacters, targetChapter, targetScene);
        var autoConflict = ExtractConflictSnippet(currentContent);
        var autoTags = BuildSceneTags(
            currentContent,
            matchedCharacters.Count,
            matchedLocations.Count,
            matchedItems.Count,
            matchedLore.Count,
            autoIntensity,
            autoEmotion.Key,
            dialogueRatio,
            wordCount,
            autoConflict);

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
            EmotionKeys.ToArray(),
            intensity,
            conflict,
            tags,
            (int)Math.Round(dialogueRatio * 100d),
            avgSentenceLength,
            wordCount);

        return new SceneContextDto(characterCards, locationCards, itemCards, loreCards, mentionRows, analysis);
    }

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
        SceneData scene)
    {
        if (currentSceneCharacters.Count == 0)
        {
            return FirstPersonRegex.Matches(content).Count >= 4
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

    private static SceneEmotionSnapshot DetectEmotion(string content, int intensity)
    {
        var normalized = content.ToLowerInvariant();

        var best = EmotionProfiles
            .Select(profile => new SceneEmotionSnapshot(
                profile.Key,
                profile.Label,
                profile.Keywords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal))))
            .OrderByDescending(entry => entry.Score)
            .First();

        if (best.Score <= 0)
        {
            return intensity switch
            {
                <= -6 => new SceneEmotionSnapshot("tense", "tense", 1),
                >= 6 => new SceneEmotionSnapshot("triumphant", "triumphant", 1),
                _ => new SceneEmotionSnapshot("neutral", "neutral", 1)
            };
        }

        return best;
    }

    private static int ComputeIntensity(string content)
    {
        var normalized = content.ToLowerInvariant();
        var positiveCount = PositiveWords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var negativeCount = NegativeWords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var conflictCount = ConflictKeywords.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var exclamations = content.Count(character => character == '!');

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

    private static string ExtractConflictSnippet(string content)
    {
        foreach (var sentence in ExtractSentences(content))
        {
            var normalized = sentence.ToLowerInvariant();
            if (!ConflictKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
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
        string conflict)
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

        if (FirstPersonRegex.Matches(content).Count >= 4)
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
    int WordCount);
