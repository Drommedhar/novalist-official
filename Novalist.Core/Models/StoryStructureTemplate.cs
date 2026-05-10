namespace Novalist.Core.Models;

/// <summary>
/// A bundled set of timeline beats (e.g. Save the Cat, Hero's Journey).
/// Applied to <see cref="TimelineData.ManualEvents"/> via
/// <see cref="StoryStructureTemplates"/>.
/// </summary>
public sealed class StoryStructureTemplate
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<StoryStructureBeat> Beats { get; init; } = [];
}

public sealed class StoryStructureBeat
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>"plot" | "character" | "world" — maps to TimelineCategory.</summary>
    public string CategoryId { get; init; } = "plot";
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
                new() { Title = "Opening Image",      Description = "Snapshot of protagonist's status quo." },
                new() { Title = "Inciting Incident",  Description = "Disrupts the status quo." },
                new() { Title = "Plot Point 1",       Description = "Protagonist commits to the journey.", CategoryId = "plot" },
                new() { Title = "Midpoint",           Description = "False victory or false defeat." },
                new() { Title = "Plot Point 2",       Description = "Lowest point — all is lost." },
                new() { Title = "Climax",             Description = "Final confrontation." },
                new() { Title = "Falling Action",     Description = "Aftermath of climax." },
                new() { Title = "Resolution",         Description = "New status quo." }
            ]
        },
        new()
        {
            Id = "save-the-cat",
            DisplayName = "Save the Cat",
            Description = "Blake Snyder's 15-beat structure.",
            Beats =
            [
                new() { Title = "Opening Image" },
                new() { Title = "Theme Stated" },
                new() { Title = "Set-Up" },
                new() { Title = "Catalyst" },
                new() { Title = "Debate" },
                new() { Title = "Break Into Two" },
                new() { Title = "B Story" },
                new() { Title = "Fun and Games" },
                new() { Title = "Midpoint" },
                new() { Title = "Bad Guys Close In" },
                new() { Title = "All Is Lost" },
                new() { Title = "Dark Night of the Soul" },
                new() { Title = "Break Into Three" },
                new() { Title = "Finale" },
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
                new() { Title = "Ordinary World" },
                new() { Title = "Call to Adventure" },
                new() { Title = "Refusal of the Call" },
                new() { Title = "Meeting the Mentor" },
                new() { Title = "Crossing the Threshold" },
                new() { Title = "Tests, Allies, Enemies" },
                new() { Title = "Approach to the Inmost Cave" },
                new() { Title = "Ordeal" },
                new() { Title = "Reward" },
                new() { Title = "The Road Back" },
                new() { Title = "Resurrection" },
                new() { Title = "Return with the Elixir" }
            ]
        },
        new()
        {
            Id = "seven-point",
            DisplayName = "7-Point Story",
            Description = "Dan Wells' 7-point structure.",
            Beats =
            [
                new() { Title = "Hook" },
                new() { Title = "Plot Turn 1" },
                new() { Title = "Pinch Point 1" },
                new() { Title = "Midpoint" },
                new() { Title = "Pinch Point 2" },
                new() { Title = "Plot Turn 2" },
                new() { Title = "Resolution" }
            ]
        }
    ];

    public static StoryStructureTemplate? GetById(string id)
        => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
