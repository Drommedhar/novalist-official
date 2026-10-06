using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ContextRpc
{
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
                    // Number and title. A tooltip reading "7" tells the writer
                    // nothing they could not count, and counting is the thing
                    // the strip is meant to save them.
                    string.IsNullOrWhiteSpace(candidateChapter.Title)
                        ? (chapterIndex + 1).ToString()
                        : $"{chapterIndex + 1}. {candidateChapter.Title}",
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

        var displayName = string.IsNullOrWhiteSpace(match?.Name) ? character.Name : match.Name;
        var displaySurname = string.IsNullOrWhiteSpace(match?.Surname) ? character.Surname : match.Surname;
        var name = string.IsNullOrWhiteSpace(displaySurname) ? displayName : $"{displayName} {displaySurname}".Trim();
        var role = string.IsNullOrWhiteSpace(match?.Role) ? character.Role : match.Role;
        var gender = string.IsNullOrWhiteSpace(match?.Gender) ? character.Gender : match.Gender;
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

        return string.IsNullOrWhiteSpace(match?.Age) ? character.Age : match.Age;
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

    private sealed record CharacterDisplay(string Name, string Role, string Group, string Gender, string Age);
}
