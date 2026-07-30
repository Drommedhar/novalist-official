using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// Where a book has been sent.
///
/// Novalist produced submission-ready material and recorded nothing about
/// where it went, so the one thing a writer must not do - send the same
/// manuscript to the same agent twice - was the one thing it could not help
/// with.
/// </summary>
public class SubmissionTests
{
    [Theory]
    [InlineData("sent")]
    [InlineData("requested")]
    [InlineData("Sent")]
    [InlineData("")]
    [InlineData(null)]
    public void StillOut(string? status) => Assert.True(SubmissionStatuses.IsOpen(status));

    [Theory]
    [InlineData("rejected")]
    [InlineData("accepted")]
    [InlineData("withdrawn")]
    [InlineData("noReply")]
    public void Answered(string status) => Assert.False(SubmissionStatuses.IsOpen(status));

    [Fact]
    public void AnAskForMoreIsStillOut()
    {
        // A partial request is not an answer. Treating it as one drops a live
        // submission out of the list the writer is watching.
        Assert.True(SubmissionStatuses.IsOpen(SubmissionStatuses.Requested));
    }

    [Theory]
    [InlineData("REJECTED", "rejected")]
    [InlineData("  accepted  ", "accepted")]
    [InlineData("on the moon", "sent")]
    [InlineData(null, "sent")]
    public void AnUnknownStatusReadsAsStillOut(string? given, string expected)
        => Assert.Equal(expected, SubmissionStatuses.Normalise(given));

    [Fact]
    public void EveryStatusIsOfferedToAPicker()
        => Assert.Equal(
            ["sent", "requested", "accepted", "rejected", "withdrawn", "noReply"],
            SubmissionStatuses.All);

    [Fact]
    public void TwoSubmissionsMadeAtOnceAreTellableApart()
    {
        var first = new Submission { Recipient = "Vane Literary" };
        var second = new Submission { Recipient = "Vane Literary" };

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ASubmissionStartsOut()
    {
        var submission = new Submission();

        Assert.Equal(SubmissionStatuses.Sent, submission.Status);
        Assert.Equal(string.Empty, submission.RespondedOn);
    }

    [Fact]
    public void ABookStartsHavingBeenNowhere()
        => Assert.Empty(new BookData().Submissions);
}
