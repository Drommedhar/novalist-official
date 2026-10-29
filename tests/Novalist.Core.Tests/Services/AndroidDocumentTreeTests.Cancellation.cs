using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed partial class AndroidDocumentTreeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingBackupInterruptsProviderReadsAndFolderLists(bool listing)
    {
        using var fixture = new DocumentFixture();
        using var cancellation = new CancellationTokenSource();
        using var fallback = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "original");
        void Block()
        {
            entered.TrySetResult();
            if (!fallback.Wait(TimeSpan.FromSeconds(5), DocumentTreeReadCancellation.Current)) throw new TimeoutException();
        }
        if (listing) fixture.Tree.GetChildrenOverride = _ => { Block(); return []; };
        else fixture.Tree.ReadOverride = path => { Block(); return File.ReadAllBytes(path); };
        var output = fixture.Local.Combine("backup.zip");
        var staging = fixture.Local.Combine("staging");
        var archives = new DocumentTreeArchiveService(fixture.Files, fixture.Mounts.IsExternalPath, staging);
        var backup = archives.CreateFromDirectoryAsync(fixture.Mount, output, [], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var foreground = fixture.Files.GetFilesAsync(fixture.Mount);
            Assert.False(foreground.IsCompleted);
            fixture.Tree.GetChildrenOverride = null;
            fixture.Tree.ReadOverride = null;
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Single(await foreground.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetDirectories(staging));
            Assert.Equal(1, await archives.CreateFromDirectoryAsync(fixture.Mount, output, [], CancellationToken.None));
        }
        finally { fallback.Set(); await ObserveCompletionAsync(backup); }
    }

    [Fact]
    public async Task CancelledGateWaitDoesNotRunOrReleaseTheActiveProviderOperation()
    {
        using var fixture = new DocumentFixture();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "original");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        var calls = 0;
        fixture.Tree.ReadOverride = source =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return File.ReadAllBytes(source);
        };
        var active = fixture.Files.ReadTextAsync(path);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task<string> queued;
            using (DocumentTreeReadCancellation.Enter(cancellation.Token)) queued = fixture.Files.ReadTextAsync(path);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(active.IsCompleted);
            Assert.Equal(1, calls);
            release.Set();
            Assert.Equal("original", await active);
            Assert.Equal("original", await fixture.Files.ReadTextAsync(path));
            Assert.Equal(2, calls);
        }
        finally { release.Set(); await ObserveCompletionAsync(active); }
    }

    [Fact]
    public async Task CancelledReadCannotPublishLateBytesOrReleaseTheGateEarly()
    {
        using var fixture = new DocumentFixture();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        var timestamp = File.GetLastWriteTimeUtc(source);
        var path = Path.Combine(fixture.Mount, "scene.txt");
        fixture.Tree.ReadOverride = _ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return System.Text.Encoding.UTF8.GetBytes("before");
        };
        Task<string> reading;
        using (DocumentTreeReadCancellation.Enter(cancellation.Token)) reading = fixture.Files.ReadTextAsync(path);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            using var cached = fixture.Files.UseCachedReads();
            var next = fixture.Files.ReadTextAsync(path);
            await Task.WhenAny(next, Task.Delay(100));
            Assert.False(reading.IsCompleted);
            Assert.False(next.IsCompleted);
            File.WriteAllText(source, "after!");
            File.SetLastWriteTimeUtc(source, timestamp);
            fixture.Tree.ReadOverride = null;
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            Assert.Equal("after!", await next.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { release.Set(); await ObserveCompletionAsync(reading); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchCancellationReachesAnInFlightProviderRead(bool existingOnly)
    {
        using var fixture = new DocumentFixture();
        using var cancellation = new CancellationTokenSource();
        using var fallback = new ManualResetEventSlim();
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "text");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Tree.ReadOverride = _ =>
        {
            entered.TrySetResult();
            if (!fallback.Wait(TimeSpan.FromSeconds(5), DocumentTreeReadCancellation.Current)) throw new TimeoutException();
            return [];
        };
        var paths = new[] { Path.Combine(fixture.Mount, "scene.txt") };
        var reading = existingOnly ? fixture.Files.ReadExistingTextsAsync(paths, cancellation.Token)
            : fixture.Files.ReadTextsAsync(paths, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(2)));
            fixture.Tree.ReadOverride = null;
            Assert.Equal("text", await fixture.Files.ReadTextAsync(paths[0]));
        }
        finally { fallback.Set(); await ObserveCompletionAsync(reading); }
    }

    [Fact]
    public async Task CancellationDuringRecoveryFinishesRepairBeforeRejectingTheRead()
    {
        using var fixture = new DocumentFixture();
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.PendingSave();
        File.WriteAllText(fixture.Provider.Combine(pending.BackupName), "original");
        File.WriteAllText(fixture.Provider.Combine(pending.TemporaryName), "incomplete");
        fixture.Tree.GetChildrenOverride = parent =>
        {
            Assert.False(DocumentTreeReadCancellation.Current.CanBeCanceled);
            cancellation.Cancel();
            fixture.Tree.GetChildrenOverride = null;
            return fixture.Tree.GetChildren(parent);
        };
        var path = Path.Combine(fixture.Mount, "draft.txt");

        using (DocumentTreeReadCancellation.Enter(cancellation.Token))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Files.ReadTextAsync(path));

        Assert.Equal("original", File.ReadAllText(fixture.Provider.Combine("draft.txt")));
        Assert.False(File.Exists(fixture.Provider.Combine(pending.BackupName)));
        Assert.False(File.Exists(fixture.Provider.Combine(pending.TemporaryName)));
        Assert.Equal("original", await fixture.Files.ReadTextAsync(path));
    }

    [Fact]
    public async Task ReadCancellationScopesRestoreAndStayWithinTheirAsyncFlow()
    {
        using var outer = new CancellationTokenSource();
        using var inner = new CancellationTokenSource();
        using (DocumentTreeReadCancellation.Enter(outer.Token))
        {
            var nested = DocumentTreeReadCancellation.Enter(inner.Token);
            await Task.Yield();
            Assert.Equal(inner.Token, DocumentTreeReadCancellation.Current);
            nested.Dispose();
            nested.Dispose();
            Assert.Equal(outer.Token, DocumentTreeReadCancellation.Current);
            await Task.Run(() =>
            {
                using var isolated = DocumentTreeReadCancellation.Enter(inner.Token);
                Assert.Equal(inner.Token, DocumentTreeReadCancellation.Current);
            });
            Assert.Equal(outer.Token, DocumentTreeReadCancellation.Current);
        }
        Assert.Equal(CancellationToken.None, DocumentTreeReadCancellation.Current);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error is null or OperationCanceledException or TimeoutException);
    }
}
