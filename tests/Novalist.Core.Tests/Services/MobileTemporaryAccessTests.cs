using System.Collections.Concurrent;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class MobileTemporaryAccessTests
{
    private static TemporaryAccessRegistry Registry() => new(error => Assert.Fail(error.ToString()));

    [Fact]
    public void PageDisposalReleasesAllSelectionsAndDuplicateCleanupDoesNothing()
    {
        using var grants = Registry();
        var released = new List<string>();
        Assert.False(grants.IsDisposed);
        Assert.True(grants.TryRetain("first", () => released.Add("first")));
        Assert.True(grants.TryRetain("second", () => released.Add("second")));
        Assert.Empty(released);

        grants.Dispose();
        grants.Release("first");
        grants.Release("second");
        grants.Dispose();

        Assert.True(grants.IsDisposed);
        Assert.Equal(["first", "second"], released);
    }

    [Fact]
    public void ReselectingTheSamePathReleasesOnlyThePreviousAcquisition()
    {
        using var grants = Registry();
        var oldCount = 0;
        var newCount = 0;
        Assert.True(grants.TryRetain("same", () => oldCount++));
        Assert.True(grants.TryRetain("same", () => newCount++));
        Assert.Equal(1, oldCount);
        Assert.Equal(0, newCount);

        grants.Release("same");
        grants.Release("same");
        grants.Dispose();

        Assert.Equal(1, oldCount);
        Assert.Equal(1, newCount);
    }

    [Fact]
    public void LatePickerResultIsRejectedAndReleasedWithoutTouchingTheNewPage()
    {
        using var oldPage = Registry();
        using var newPage = Registry();
        var lateCount = 0;
        var currentCount = 0;
        oldPage.Dispose();
        Assert.True(newPage.TryRetain("same", () => currentCount++));

        Assert.False(oldPage.TryRetain("same", () => lateCount++));
        oldPage.Release("same");
        oldPage.Dispose();

        Assert.Equal(1, lateCount);
        Assert.Equal(0, currentCount);
        newPage.Dispose();
        Assert.Equal(1, currentCount);
    }

    [Fact]
    public void ConcurrentReplacementReleaseAndDisposalBalanceEachAcquisitionOnce()
    {
        using var grants = Registry();
        var releases = new int[256];
        Parallel.Invoke(
            () => Parallel.For(0, releases.Length, index =>
                grants.TryRetain("same", () => Interlocked.Increment(ref releases[index]))),
            () => Parallel.For(0, releases.Length, _ => grants.Release("same")),
            grants.Dispose);
        grants.Dispose();
        Assert.All(releases, count => Assert.Equal(1, count));
    }

    [Fact]
    public void OneNativeStopFailureIsReportedWithoutSkippingOtherReleases()
    {
        var errors = new List<Exception>();
        using var grants = new TemporaryAccessRegistry(errors.Add);
        var attempts = 0;
        var completed = 0;
        grants.TryRetain("failing", () => { attempts++; throw new IOException("native stop failed"); });
        grants.TryRetain("other", () => completed++);

        grants.Dispose();
        grants.Release("failing");
        grants.Dispose();

        Assert.IsType<IOException>(Assert.Single(errors));
        Assert.Equal(1, attempts);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void FailedStopDuringReplacementOrLateCompletionIsStillAttemptedOnce()
    {
        var errors = new ConcurrentBag<Exception>();
        using var grants = new TemporaryAccessRegistry(errors.Add);
        var attempts = 0;
        Action failing = () => { Interlocked.Increment(ref attempts); throw new IOException("native stop failed"); };
        grants.TryRetain("same", failing);
        Assert.True(grants.TryRetain("same", failing));
        grants.Release("same");
        grants.Dispose();
        Assert.False(grants.TryRetain("late", failing));
        grants.Dispose();

        Assert.Equal(3, attempts);
        Assert.Equal(3, errors.Count);
    }
}
