using Novalist.Core.Models;
using Novalist.Desktop.Localization;
using Xunit;

namespace Novalist.Desktop.Tests.Localization;

public class AgeComputationTests
{
    [Fact]
    public void ComputeInterval_Years_Months_Days()
    {
        var from = new DateTime(2000, 6, 15);
        Assert.Equal("20", AgeComputation.ComputeInterval(from, new DateTime(2020, 6, 15), IntervalUnit.Years));
        Assert.Equal("19", AgeComputation.ComputeInterval(from, new DateTime(2020, 6, 14), IntervalUnit.Years)); // day before birthday
        Assert.Equal("12", AgeComputation.ComputeInterval(from, new DateTime(2001, 6, 15), IntervalUnit.Months));
        Assert.Equal("11", AgeComputation.ComputeInterval(from, new DateTime(2001, 6, 14), IntervalUnit.Months)); // day not yet reached
        Assert.Equal("31", AgeComputation.ComputeInterval(from, new DateTime(2000, 7, 16), IntervalUnit.Days));
    }

    [Fact]
    public void ComputeInterval_FromAfterTo_Empty()
    {
        Assert.Equal(string.Empty, AgeComputation.ComputeInterval(new DateTime(2020, 1, 1), new DateTime(2019, 1, 1), IntervalUnit.Years));
    }

    [Fact]
    public void ComputeInterval_UnknownUnit_Empty()
    {
        Assert.Equal(string.Empty, AgeComputation.ComputeInterval(new DateTime(2000, 1, 1), new DateTime(2001, 1, 1), (IntervalUnit)99));
    }

    [Fact]
    public void ComputeAge_NullOrBlankOrUnparseable_Empty()
    {
        Assert.Equal(string.Empty, AgeComputation.ComputeAge(null, null, IntervalUnit.Years));
        Assert.Equal(string.Empty, AgeComputation.ComputeAge("   ", null, IntervalUnit.Years));
        Assert.Equal(string.Empty, AgeComputation.ComputeAge("not-a-date", null, IntervalUnit.Years));
    }

    [Fact]
    public void ComputeAge_WithReference()
    {
        Assert.Equal("25", AgeComputation.ComputeAge("1990-03-04", "2015-03-04", IntervalUnit.Years));
    }

    [Fact]
    public void ComputeAge_BadReference_FallsBackToToday()
    {
        var age = AgeComputation.ComputeAge("2000-01-01", "garbage", IntervalUnit.Years);
        Assert.True(int.Parse(age) >= 20); // computed against today
    }

    [Fact]
    public void ComputeAge_NoReference_UsesToday()
    {
        var age = AgeComputation.ComputeAge("2000-01-01", null, IntervalUnit.Years);
        Assert.True(int.Parse(age) >= 20);
    }

    [Fact]
    public void ComputeAge_BirthAfterReference_Empty()
    {
        Assert.Equal(string.Empty, AgeComputation.ComputeAge("2030-01-01", "2020-01-01", IntervalUnit.Years));
    }
}
