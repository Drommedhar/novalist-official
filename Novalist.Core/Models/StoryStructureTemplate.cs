namespace Novalist.Core.Models;

/// <summary>
/// A bundled set of timeline beats (e.g. Save the Cat, Hero's Journey).
/// Applied to <see cref="TimelineData.ManualEvents"/> via
/// <see cref="StoryStructureTemplates"/>.
/// </summary>
public sealed class StoryStructureTemplate
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("beats")]
    public IReadOnlyList<StoryStructureBeat> Beats { get; set; } = [];
}

public static class StoryStructureBeatKeys
{
    /// <summary>
    /// The key a scene stores for a beat: its own when it has one, otherwise a
    /// slug of its title. Kept in one place so the reader and the writer of a
    /// binding can never disagree about it.
    /// </summary>
    public static string For(StoryStructureBeat beat)
    {
        if (!string.IsNullOrWhiteSpace(beat.Key)) return beat.Key.Trim();
        var slug = new string([.. beat.Title
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}

public sealed class StoryStructureBeat
{
    /// <summary>
    /// Stable identifier a scene points at. Derived from the title when not set,
    /// so a template author does not have to invent one - but a beat that is
    /// ever renamed should carry its own, or every scene bound to it comes
    /// loose.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Where this beat belongs, as a percentage through the manuscript.
    ///
    /// This is what makes a structure template more than a checklist: Save the
    /// Cat says the midpoint is at 50%, so a midpoint scene sitting at 30% is
    /// worth telling the writer about. Zero means the template does not say.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("targetPercent")]
    public int TargetPercent { get; set; }

    /// <summary>"plot" | "character" | "world" — maps to TimelineCategory.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("categoryId")]
    public string CategoryId { get; set; } = "plot";
}

public static class StoryStructureTemplates
{
    public static IReadOnlyList<StoryStructureTemplate> All { get; } =
    [
        new()
        {
            Id = "three-act",
            DisplayName = "Three-Act",
            Description = "Setup → Confrontation → Resolution. 8 beats.",
            Beats =
            [
                new() { Title = "Opening Image",      Description = "Snapshot of protagonist's status quo.", TargetPercent = 1 },
                new() { Title = "Inciting Incident",  Description = "Disrupts the status quo.", TargetPercent = 10 },
                new() { Title = "Plot Point 1",       Description = "Protagonist commits to the journey.", CategoryId = "plot", TargetPercent = 25 },
                new() { Title = "Midpoint",           Description = "False victory or false defeat.", TargetPercent = 50 },
                new() { Title = "Plot Point 2",       Description = "Lowest point — all is lost.", TargetPercent = 75 },
                new() { Title = "Climax",             Description = "Final confrontation.", TargetPercent = 88 },
                new() { Title = "Falling Action",     Description = "Aftermath of climax.", TargetPercent = 95 },
                new() { Title = "Resolution",         Description = "New status quo.", TargetPercent = 99 }
            ]
        },
        new()
        {
            Id = "save-the-cat",
            DisplayName = "Save the Cat",
            Description = "Blake Snyder's 15-beat structure.",
            Beats =
            [
                new() { Title = "Opening Image", TargetPercent = 1 },
                new() { Title = "Theme Stated", TargetPercent = 5 },
                new() { Title = "Set-Up", TargetPercent = 10 },
                new() { Title = "Catalyst", TargetPercent = 10 },
                new() { Title = "Debate", TargetPercent = 12 },
                new() { Title = "Break Into Two", TargetPercent = 20 },
                new() { Title = "B Story", TargetPercent = 20 },
                new() { Title = "Fun and Games", TargetPercent = 50 },
                new() { Title = "Midpoint", TargetPercent = 55 },
                new() { Title = "Bad Guys Close In", TargetPercent = 75 },
                new() { Title = "All Is Lost", TargetPercent = 80 },
                new() { Title = "Dark Night of the Soul", TargetPercent = 85 },
                new() { Title = "Break Into Three", TargetPercent = 90 },
                new() { Title = "Finale", TargetPercent = 99 },
                new() { Title = "Final Image" }
            ]
        },
        new()
        {
            Id = "hero-journey",
            DisplayName = "Hero's Journey",
            Description = "Campbell-style 12-stage monomyth.",
            Beats =
            [
                new() { Title = "Ordinary World", TargetPercent = 5 },
                new() { Title = "Call to Adventure", TargetPercent = 10 },
                new() { Title = "Refusal of the Call", TargetPercent = 12 },
                new() { Title = "Meeting the Mentor", TargetPercent = 15 },
                new() { Title = "Crossing the Threshold", TargetPercent = 25 },
                new() { Title = "Tests, Allies, Enemies", TargetPercent = 35 },
                new() { Title = "Approach to the Inmost Cave", TargetPercent = 45 },
                new() { Title = "Ordeal", TargetPercent = 50 },
                new() { Title = "Reward", TargetPercent = 60 },
                new() { Title = "The Road Back", TargetPercent = 68 },
                new() { Title = "Resurrection", TargetPercent = 75 },
                new() { Title = "Return with the Elixir", TargetPercent = 85 }
            ]
        },
        new()
        {
            Id = "seven-point",
            DisplayName = "7-Point Story",
            Description = "Dan Wells' 7-point structure.",
            Beats =
            [
                new() { Title = "Hook", TargetPercent = 1 },
                new() { Title = "Plot Turn 1", TargetPercent = 15 },
                new() { Title = "Pinch Point 1", TargetPercent = 35 },
                new() { Title = "Midpoint", TargetPercent = 50 },
                new() { Title = "Pinch Point 2", TargetPercent = 65 },
                new() { Title = "Plot Turn 2", TargetPercent = 85 },
                new() { Title = "Resolution", TargetPercent = 99 }
            ]
        }
    ];

    public static StoryStructureTemplate? GetById(string id)
        => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
