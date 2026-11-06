using System.Text.Json;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests;

public class MobileDocumentRequestsTests
{
    [Fact]
    public async Task RepliesCanArriveOutOfOrderAndSurviveJsonDisposal()
    {
        var sent = new List<int>();
        using var requests = new MobileDocumentRequests((id, _, _) => { sent.Add(id); return Task.CompletedTask; });
        var first = requests.RequestAsync(new { action = "capture" }, CancellationToken.None);
        var second = requests.RequestAsync(new { action = "capture" }, CancellationToken.None);
        using (var reply = JsonDocument.Parse("{\"html\":\"second\"}")) requests.Complete(sent[1], true, reply.RootElement);
        Assert.False(first.IsCompleted);
        Assert.Equal("second", (await second).GetProperty("html").GetString());
        requests.Complete(sent[0], true, JsonSerializer.SerializeToElement("first"));
        Assert.Equal("first", (await first).GetString());
    }

    [Fact]
    public async Task CancellingOneRequestDoesNotCancelAnotherOrAcceptItsLateReply()
    {
        var sent = new List<int>();
        using var requests = new MobileDocumentRequests((id, _, _) => { sent.Add(id); return Task.CompletedTask; });
        using var cancelled = new CancellationTokenSource();
        var first = requests.RequestAsync(new { }, cancelled.Token);
        var second = requests.RequestAsync(new { }, CancellationToken.None);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        requests.Complete(sent[0], true, JsonSerializer.SerializeToElement("late"));
        Assert.False(second.IsCompleted);
        requests.Complete(sent[1], false, JsonSerializer.SerializeToElement<string?>(null));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
    }

    [Fact]
    public async Task ClosingThePageCancelsOutstandingDocumentWork()
    {
        var requests = new MobileDocumentRequests((_, _, _) => Task.CompletedTask);
        var pending = requests.RequestAsync(new { }, CancellationToken.None);
        requests.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => requests.RequestAsync(new { }, CancellationToken.None));
    }
}
