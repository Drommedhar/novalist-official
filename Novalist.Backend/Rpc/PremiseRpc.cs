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
                a, premise.Acts.TryGetValue(a, out var text) ? text : string.Empty))]);
    }

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

public sealed record PremiseDto(string Logline, string Paragraph, PremiseActDto[] Acts);
