using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed partial class AndroidDocumentTreeTests
{
    [Fact]
    public async Task IndependentFolderListingsOverlapAndSameFolderRequestsShareOneQuery()
    {
        using var fixture = new DocumentFixture();
        using var release = new ManualResetEventSlim();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fixture.Tree.GetChildrenOverride = _ =>
        {
            if (Interlocked.Increment(ref calls) == 2) bothStarted.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return [];
        };
        var cache = new CachedDocumentTree(fixture.Tree, fixture.Clock);
        var first = Task.Run(() => cache.GetChildren("first"));
        var same = Task.Run(() => cache.GetChildren("first"));
        var second = Task.Run(() => cache.GetChildren("second"));
        try { await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { release.Set(); }
        await Task.WhenAll(first, same, second);
        Assert.Equal(2, calls);
    }
}
