using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The book's premise ladder: one line, one paragraph, one summary per act.
///
/// Novalist shipped a Snowflake-shaped setup wizard in the codebase that
/// nothing called, and nowhere for its answers to live. This is that home.
/// </summary>
public sealed class PremiseRpc
{
    private readonly Workspace _workspace;

    public PremiseRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("premise/get")]
    public PremiseDto Get()
    {
        var book = _workspace.Projects.ActiveBook;
        var premise = book?.Premise ?? new StoryPremise();
        // Acts come from the chapters rather than from the premise, so an act
        // the writer added later still gets a box to summarise it in.
        var acts = (book?.Chapters ?? [])
            .Select(c => c.Act)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var act in premise.Acts.Keys)
            if (!acts.Contains(act, StringComparer.OrdinalIgnoreCase))
                acts.Add(act);

        return new PremiseDto(
            premise.Logline,
            premise.Paragraph,
            [.. acts.Select(a => new PremiseActDto(
                a, premise.Acts.TryGetValue(a, out var text) ? text : string.Empty))],
            new PitchDto(
                premise.Genre,
                premise.Audience,
                premise.Comparables,
                premise.Setting,
                premise.Blurb,
                premise.Synopsis),
            new VoiceDto(book?.NarrativePerson ?? string.Empty, book?.Tense ?? string.Empty));
    }

    /// <summary>
    /// The pitch: what a query letter, a submission form and a retailer page ask
    /// for by name. Every one of these lived in a document outside Novalist,
    /// which is how a comparable title ends up quoted from memory.
    /// </summary>
    [JsonRpcMethod("premise/savePitch")]
    public async Task<PremiseDto> SavePitchAsync(PitchDto pitch)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.Premise.Genre = Clean(pitch.Genre);
        book.Premise.Audience = Clean(pitch.Audience);
        book.Premise.Comparables = Clean(pitch.Comparables);
        book.Premise.Setting = Clean(pitch.Setting);
        book.Premise.Blurb = Clean(pitch.Blurb);
        book.Premise.Synopsis = Clean(pitch.Synopsis);
        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }

    /// <summary>
    /// What the book is written in. Declared rather than derived: the answer is
    /// the writer's intention, and reading it off the majority of scenes would
    /// make the one that drifted look normal.
    /// </summary>
    [JsonRpcMethod("premise/saveVoice")]
    public async Task<PremiseDto> SaveVoiceAsync(string? narrativePerson, string? tense)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.NarrativePerson = Clean(narrativePerson);
        book.Tense = Clean(tense);
        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    [JsonRpcMethod("premise/save")]
    public async Task<PremiseDto> SaveAsync(string? logline, string? paragraph, PremiseActDto[]? acts)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.Premise = new StoryPremise
        {
            Logline = (logline ?? string.Empty).Trim(),
            Paragraph = (paragraph ?? string.Empty).Trim(),
            Acts = (acts ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Act)
                            && !string.IsNullOrWhiteSpace(a.Summary))
                .GroupBy(a => a.Act.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Summary!.Trim())
        };
        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }

    /// <summary>
    /// Lays out a book from a premise: an act per summary, and the requested
    /// number of placeholder chapters under each. The chapters are empty on
    /// purpose - the point is a shape to write into, not text to delete.
    /// </summary>
    [JsonRpcMethod("premise/scaffold")]
    public async Task<int> ScaffoldAsync(PremiseActDto[] acts, int chaptersPerAct)
    {
        _ = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var perAct = Math.Clamp(chaptersPerAct, 1, 30);
        var created = 0;
        foreach (var act in acts ?? [])
        {
            if (string.IsNullOrWhiteSpace(act.Act)) continue;
            for (var i = 1; i <= perAct; i++)
            {
                var chapter = await _workspace.Projects.CreateChapterAsync(
                    $"{act.Act.Trim()} - {i}");
                chapter.Act = act.Act.Trim();
                created++;
            }
        }

        if (created > 0) await _workspace.Projects.SaveProjectAsync();
        return created;
    }
}

/// <summary>One act and what happens in it.</summary>
public sealed record PremiseActDto(string Act, string? Summary);

public sealed record PremiseDto(
    string Logline,
    string Paragraph,
    PremiseActDto[] Acts,
    PitchDto Pitch,
    VoiceDto Voice);

/// <summary>How the book is sold, in the words the forms ask for.</summary>
public sealed record PitchDto(
    string Genre,
    string Audience,
    string Comparables,
    string Setting,
    /// <summary>Back-cover copy, which withholds the ending on purpose.</summary>
    string Blurb,
    /// <summary>The one-page synopsis, ending included.</summary>
    string Synopsis);

/// <summary>
/// What the book is written in. Empty means the writer has not said, and
/// nothing is checked against it.
/// </summary>
public sealed record VoiceDto(string NarrativePerson, string Tense);
