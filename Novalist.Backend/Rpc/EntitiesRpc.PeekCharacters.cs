using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    /// <summary>
    /// Resolves the character chapter/scene override that applies to the given
    /// editor context, matching the chapter by GUID or title. Scene-specific
    /// overrides win over chapter-wide ones. Ported from
    /// <c>FocusPeekExtension.ResolveCharacterOverride</c>.
    /// </summary>
    private static CharacterOverride? ResolveCharacterOverride(
        CharacterData character, string? chapterGuid, string? chapterTitle, string? sceneTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterGuid) && string.IsNullOrWhiteSpace(chapterTitle))
            return null;

        bool ChapterMatches(CharacterOverride o) =>
            (!string.IsNullOrWhiteSpace(chapterGuid)
                && string.Equals(o.Chapter, chapterGuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(chapterTitle)
                && string.Equals(o.Chapter, chapterTitle, StringComparison.OrdinalIgnoreCase));

        var sceneMatch = character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o)
            && !string.IsNullOrWhiteSpace(o.Scene)
            && string.Equals(o.Scene, sceneTitle, StringComparison.OrdinalIgnoreCase));
        if (sceneMatch != null)
            return sceneMatch;

        return character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));
    }

    /// <summary>Builds the "Ch: X → Sc: Y" scope label for an applied override,
    /// preferring the friendly chapter title the client passed over the stored
    /// (often GUID) chapter key.</summary>
    private static string BuildOverrideScopeLabel(
        CharacterOverride ovr, string? chapterTitle)
    {
        var parts = new List<string>();
        var chapter = string.IsNullOrWhiteSpace(chapterTitle) ? ovr.Chapter : chapterTitle;
        if (!string.IsNullOrWhiteSpace(chapter)) parts.Add($"Ch: {chapter}");
        if (!string.IsNullOrWhiteSpace(ovr.Scene)) parts.Add($"Sc: {ovr.Scene}");
        return string.Join(" → ", parts);
    }

    private EntityPeekDto BuildCharacterPeek(
        CharacterData c, Dictionary<string, (string Id, string TypeKey)> resolve, PeekMapPinDto[] pins,
        string? chapterGuid = null, string? chapterTitle = null, string? sceneTitle = null)
    {
        var ovr = ResolveCharacterOverride(c, chapterGuid, chapterTitle, sceneTitle);

        // Each overridden non-blank field wins over the base value (desktop rule).
        string Pick(string? overridden, string @base) =>
            string.IsNullOrWhiteSpace(overridden) ? @base : overridden;

        var name = Pick(ovr?.Name, c.Name);
        var surname = Pick(ovr?.Surname, c.Surname);
        var title = string.IsNullOrWhiteSpace(surname) ? name : $"{name} {surname}";
        var pills = new List<PeekPillDto>();
        AddPill(pills, Pick(ovr?.Role, c.Role), "#3B4466");
        AddPill(pills, Pick(ovr?.Gender, c.Gender), "#314355");
        AddLabelPill(pills, "focusPeek.agePill",
            ResolveCharacterAge(c, ovr, chapterGuid, sceneTitle), "#2E344D", dim: true);
        AddPill(pills, c.Group, "#2A3C38", dim: true);

        var relationships = ovr?.Relationships ?? c.Relationships;
        if (relationships.Count > 0)
            pills.Add(new PeekPillDto(relationships.Count.ToString(), null, null, true, "#2E344D", UsersIconPath));

        var appearance = new List<PeekPropDto>();
        AddProp(appearance, "focusPeek.eyes", Pick(ovr?.EyeColor, c.EyeColor));
        AddProp(appearance, "focusPeek.hair", Pick(ovr?.HairColor, c.HairColor));
        AddProp(appearance, "focusPeek.hairLength", Pick(ovr?.HairLength, c.HairLength));
        AddProp(appearance, "focusPeek.height", Pick(ovr?.Height, c.Height));
        AddProp(appearance, "focusPeek.build", Pick(ovr?.Build, c.Build));
        AddProp(appearance, "focusPeek.skin", Pick(ovr?.SkinTone, c.SkinTone));
        AddProp(appearance, "focusPeek.distinguishing", Pick(ovr?.DistinguishingFeatures, c.DistinguishingFeatures));

        // Overridden custom properties layer over the base set (blank-skipped by CustomProps).
        var customProperties = new Dictionary<string, string>(c.CustomProperties, StringComparer.OrdinalIgnoreCase);
        if (ovr?.CustomProperties != null)
            foreach (var pair in ovr.CustomProperties)
                customProperties[pair.Key] = pair.Value;

        // Override list semantics: null inherits the base list; a non-null list
        // (even empty) replaces it, mirroring the desktop editor's write-back.
        var images = ovr?.Images ?? c.Images;
        var sections = ovr?.Sections ?? c.Sections;
        var scopeLabel = ovr == null ? null : BuildOverrideScopeLabel(ovr, chapterTitle);

        return new EntityPeekDto(
            c.Id, "character", title, null, "#5B3F7A", string.Empty,
            ResolveImages(images), pills.ToArray(),
            appearance.ToArray(),
            CustomProps(customProperties),
            relationships.Select(r => BuildRelationship(r.Role, r.Target, resolve)).ToArray(),
            sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(),
            pins, scopeLabel);
    }

    /// <summary>
    /// Resolves a character's displayed age. When <c>AgeMode == "date"</c> and a
    /// birth date is present, the age is computed from the birth date relative to
    /// the open scene's story date (else the chapter's date, else today) via
    /// <see cref="AgeComputation"/> — the <c>AgeIntervalUnit</c> (default Years)
    /// picks years/months/days. Otherwise the override age wins over the base age.
    /// Ported from <c>FocusPeekExtension.ResolveCharacterAge</c>.
    /// </summary>
    private string ResolveCharacterAge(
        CharacterData c, CharacterOverride? ovr, string? chapterGuid, string? sceneTitle)
    {
        if (string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(c.BirthDate))
        {
            var referenceDate = ResolveStoryReferenceDate(chapterGuid, sceneTitle);
            var computed = AgeComputation.ComputeAge(
                c.BirthDate, referenceDate, c.AgeIntervalUnit ?? IntervalUnit.Years);
            if (!string.IsNullOrWhiteSpace(computed))
                return computed;
        }

        return string.IsNullOrWhiteSpace(ovr?.Age) ? c.Age : ovr.Age;
    }

    /// <summary>Resolves the story date to measure age against: the named scene's
    /// date, else the chapter's date, else null (→ today). Scenes are matched by
    /// title within the chapter, mirroring the peek's chapter/scene scope.</summary>
    private string? ResolveStoryReferenceDate(string? chapterGuid, string? sceneTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterGuid))
            return null;

        var scenes = _workspace.Projects.ScenesManifest?.Chapters.GetValueOrDefault(chapterGuid);
        var scene = scenes?.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(sceneTitle)
            && string.Equals(s.Title, sceneTitle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(scene?.Date))
            return scene.Date;

        var chapter = _workspace.Projects.ActiveBook?.Chapters
            .FirstOrDefault(ch => string.Equals(ch.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(chapter?.Date) ? null : chapter.Date;
    }
}
