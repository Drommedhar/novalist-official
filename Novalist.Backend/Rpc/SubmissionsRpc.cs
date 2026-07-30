using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>One submission of this book to one recipient.</summary>
public sealed record SubmissionDto(
    string Id, string Recipient, string Material, string SentOn,
    string Status, string RespondedOn, string Notes,
    /// <summary>True while this one is still out.</summary>
    bool IsOpen);

/// <summary>
/// Where this book has been sent and what came back.
///
/// Novalist produced submission-ready material - the Exposé, the Shunn layout -
/// and recorded nothing about where any of it went. So the one thing a writer
/// must not do, send the same manuscript to the same agent twice, was the one
/// thing the app could not help with.
/// </summary>
public class SubmissionsRpc(Workspace workspace)
{
    private readonly Workspace _workspace = workspace;

    private BookData Book => _workspace.Projects.ActiveBook
        ?? throw new InvalidOperationException("No active book.");

    private static SubmissionDto ToDto(Submission s)
        => new(s.Id, s.Recipient, s.Material, s.SentOn, s.Status, s.RespondedOn, s.Notes,
            SubmissionStatuses.IsOpen(s.Status));

    /// <summary>Every submission, the ones still out first.</summary>
    [JsonRpcMethod("submissions/list")]
    public SubmissionDto[] List()
        => [.. (_workspace.Projects.ActiveBook?.Submissions ?? [])
            // Still out first: those are the ones a writer is waiting on, and
            // a list ordered by date buries them under a year of rejections.
            .OrderByDescending(s => SubmissionStatuses.IsOpen(s.Status))
            .ThenByDescending(s => s.SentOn, StringComparer.Ordinal)
            .Select(ToDto)];

    /// <summary>
    /// Recipients this book is already out with.
    ///
    /// Reported rather than enforced: a writer who queries the same agency
    /// twice on purpose - a different agent there, a re-query after a rewrite -
    /// is doing something normal, and an app that refuses is an app they work
    /// around.
    /// </summary>
    [JsonRpcMethod("submissions/openWith")]
    public string[] OpenWith(string recipient)
    {
        var name = (recipient ?? string.Empty).Trim();
        if (name.Length == 0) return [];

        return [.. (_workspace.Projects.ActiveBook?.Submissions ?? [])
            .Where(s => SubmissionStatuses.IsOpen(s.Status))
            .Where(s => s.Recipient.Trim().Equals(name, StringComparison.CurrentCultureIgnoreCase))
            .Select(s => s.SentOn)];
    }

    /// <summary>Records a submission, or updates one already recorded.</summary>
    [JsonRpcMethod("submissions/save")]
    public async Task<SubmissionDto[]> SaveAsync(
        string? id, string recipient, string? material = null, string? sentOn = null,
        string? status = null, string? respondedOn = null, string? notes = null)
    {
        var name = (recipient ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("A submission needs somebody it went to.");

        var book = Book;
        var submission = book.Submissions.FirstOrDefault(s => s.Id == id);
        if (submission == null)
        {
            submission = new Submission
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id
            };
            book.Submissions.Add(submission);
        }

        submission.Recipient = name;
        submission.Material = (material ?? string.Empty).Trim();
        submission.SentOn = (sentOn ?? string.Empty).Trim();
        submission.Status = SubmissionStatuses.Normalise(status);
        submission.RespondedOn = (respondedOn ?? string.Empty).Trim();
        submission.Notes = (notes ?? string.Empty).Trim();

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>Removes a record. The submission still happened; this is a typo fix.</summary>
    [JsonRpcMethod("submissions/remove")]
    public async Task<SubmissionDto[]> RemoveAsync(string id)
    {
        var book = Book;
        if (book.Submissions.RemoveAll(s => s.Id == id) > 0)
            await _workspace.Projects.SaveProjectAsync();
        return List();
    }
}
