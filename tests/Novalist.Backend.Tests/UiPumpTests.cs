using System.Collections.Concurrent;
using Novalist.Backend.Extensions;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Behavior of the backend-owned single-threaded UI pump. Plain [Fact] tests
/// (no Avalonia collection): the pump is a raw <see cref="System.Threading.Thread"/>.
/// </summary>
public class UiPumpTests
{
    [Fact]
    public void Post_RunsAction_OnASingleDedicatedThread()
    {
        using var pump = new UiPump();
        using var done = new ManualResetEventSlim();
        int runThread = 0;
        pump.Post(() => { runThread = Environment.CurrentManagedThreadId; done.Set(); });

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, runThread);
    }

    [Fact]
    public void Post_PreservesOrder()
    {
        using var pump = new UiPump();
        var order = new ConcurrentQueue<int>();
        using var done = new ManualResetEventSlim();
        for (var i = 0; i < 50; i++)
        {
            var n = i;
            pump.Post(() => order.Enqueue(n));
        }
        pump.Post(done.Set);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(Enumerable.Range(0, 50), order.ToArray());
    }

    [Fact]
    public void CheckAccess_TrueOnPumpThread_FalseElsewhere()
    {
        using var pump = new UiPump();
        Assert.False(pump.CheckAccess());

        using var done = new ManualResetEventSlim();
        var onPump = false;
        pump.Post(() => { onPump = pump.CheckAccess(); done.Set(); });
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(onPump);
    }

    [Fact]
    public async Task InvokeAsync_RunsAndCompletes()
    {
        using var pump = new UiPump();
        var ran = false;
        await pump.InvokeAsync(() => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public async Task InvokeAsync_OnPumpThread_RunsInline()
    {
        using var pump = new UiPump();
        var innerRan = false;
        await pump.InvokeAsync(async () =>
        {
            // Already on the pump thread — a nested InvokeAsync must run inline
            // (no deadlock waiting on the same thread).
            await pump.InvokeAsync(() => innerRan = true);
        });
        Assert.True(innerRan);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesException()
    {
        using var pump = new UiPump();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.InvokeAsync(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public void Invoke_ReturnsValue()
    {
        using var pump = new UiPump();
        Assert.Equal(42, pump.Invoke(() => 42));
    }

    [Fact]
    public void Invoke_OnPumpThread_RunsInline()
    {
        using var pump = new UiPump();
        var result = pump.Invoke(() => pump.Invoke(() => 7));
        Assert.Equal(7, result);
    }

    [Fact]
    public void Invoke_PropagatesException()
    {
        using var pump = new UiPump();
        Assert.Throws<InvalidOperationException>(
            () => pump.Invoke<int>(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public void RunOne_SwallowsExceptions_AndKeepsPumping()
    {
        using var pump = new UiPump();
        using var done = new ManualResetEventSlim();
        pump.Post(() => throw new InvalidOperationException("first action throws"));
        pump.Post(done.Set); // must still run after the faulted one
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Post_AfterDispose_IsDropped()
    {
        var pump = new UiPump();
        pump.Dispose();
        var ran = false;
        pump.Post(() => ran = true); // no throw, silently dropped
        Assert.False(ran);
    }

    [Fact]
    public void Post_Null_Throws()
    {
        using var pump = new UiPump();
        Assert.Throws<ArgumentNullException>(() => pump.Post(null!));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var pump = new UiPump();
        pump.Dispose();
        pump.Dispose();
    }
}
