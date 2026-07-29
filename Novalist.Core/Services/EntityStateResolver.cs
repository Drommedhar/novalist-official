using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>Where a resolved value came from, so the reader can be told the
/// entry is being shown as it is at a point in the story rather than in
/// general.</summary>
public sealed record ResolvedEntityState(
    string? Name,
    string? Description,
    IReadOnlyDictionary<string, string> Fields,
    string? Note,
    string ScopeLabel)
{
    /// <summary>Whether an override applied at all.</summary>
    public bool IsOverridden => ScopeLabel.Length > 0;
}

/// <summary>
/// Resolves what an entry is like at a point in the story.
///
/// Precedence is most-specific-first, matching the character override resolver
/// that has always worked this way: a scene override beats a chapter one, which
/// beats an act one. Restating a value in a narrower scope is how a writer says
/// "and by this scene, it is worse."
/// </summary>
public static class EntityStateResolver
{
    /// <summary>
    /// The state of an entry for the given context, or an unoverridden result
    /// when nothing applies.
    ///
    /// <paramref name="chapterGuid"/> and <paramref name="chapterTitle"/> are
    /// both matched, because an override may have been written against either -
    /// the guid when the app set it, the title when a writer edited the file.
    /// </summary>
    public static ResolvedEntityState Resolve(
        IReadOnlyList<EntityStateOverride> overrides,
        string? act,
        string? chapterGuid,
        string? chapterTitle,
        string? sceneTitle)
    {
        var applicable = overrides.Where(o => o.HasValues).ToList();
        if (applicable.Count == 0) return None();

        bool ChapterMatches(EntityStateOverride o) =>
            (!string.IsNullOrWhiteSpace(chapterGuid)
             && string.Equals(o.Chapter, chapterGuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(chapterTitle)
                && string.Equals(o.Chapter, chapterTitle, StringComparison.OrdinalIgnoreCase));

        // Scene first: the narrowest thing the writer can say.
        var match = applicable.FirstOrDefault(o =>
            ChapterMatches(o)
            && !string.IsNullOrWhiteSpace(o.Scene)
            && string.Equals(o.Scene, sceneTitle, StringComparison.OrdinalIgnoreCase));

        match ??= applicable.FirstOrDefault(o =>
            ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));

        match ??= applicable.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.Act)
            && string.IsNullOrWhiteSpace(o.Chapter)
            && string.Equals(o.Act, act, StringComparison.OrdinalIgnoreCase));

        if (match == null) return None();

        return new ResolvedEntityState(
            match.Name,
            match.Description,
            match.Fields ?? new Dictionary<string, string>(),
            match.Note,
            ScopeLabelFor(match, chapterTitle));
    }

    private static ResolvedEntityState None()
        => new(null, null, new Dictionary<string, string>(), null, string.Empty);

    /// <summary>
    /// The "Ch: Three - Sc: The Fire" label shown beside restated values,
    /// preferring the chapter title the caller passed over the (often GUID)
    /// key stored on the override.
    /// </summary>
    internal static string ScopeLabelFor(EntityStateOverride match, string? chapterTitle)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(match.Act) && string.IsNullOrWhiteSpace(match.Chapter))
            parts.Add($"Act: {match.Act}");

        if (!string.IsNullOrWhiteSpace(match.Chapter))
        {
            var chapter = string.IsNullOrWhiteSpace(chapterTitle) ? match.Chapter : chapterTitle;
            parts.Add($"Ch: {chapter}");
        }

        if (!string.IsNullOrWhiteSpace(match.Scene)) parts.Add($"Sc: {match.Scene}");

        // Every override is scoped to something, so an empty label would make
        // IsOverridden lie.
        return parts.Count > 0 ? string.Join(" - ", parts) : "Everywhere";
    }
}
