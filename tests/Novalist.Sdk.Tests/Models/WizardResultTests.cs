using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Sdk.Tests.Models;

public class WizardResultTests
{
    private static WizardResult WithAnswer(string stepId, WizardAnswer answer)
    {
        var r = new WizardResult();
        r.Answers[stepId] = answer;
        return r;
    }

    [Fact]
    public void GetText_ReturnsText_WhenPresent()
    {
        var r = WithAnswer("a", new WizardAnswer { Text = "hello" });
        Assert.Equal("hello", r.GetText("a"));
    }

    [Fact]
    public void GetText_ReturnsEmpty_WhenTextNull()
    {
        var r = WithAnswer("a", new WizardAnswer { Text = null });
        Assert.Equal(string.Empty, r.GetText("a"));
    }

    [Fact]
    public void GetText_ReturnsEmpty_WhenAbsent()
    {
        Assert.Equal(string.Empty, new WizardResult().GetText("missing"));
    }

    [Fact]
    public void GetNumber_ReturnsValue_WhenPresent()
    {
        var r = WithAnswer("a", new WizardAnswer { Number = 42 });
        Assert.Equal(42, r.GetNumber("a"));
    }

    [Fact]
    public void GetNumber_ReturnsFallback_WhenNumberNull()
    {
        var r = WithAnswer("a", new WizardAnswer { Number = null });
        Assert.Equal(7, r.GetNumber("a", 7));
    }

    [Fact]
    public void GetNumber_ReturnsFallback_WhenAbsent()
    {
        Assert.Equal(0, new WizardResult().GetNumber("missing"));
    }

    [Fact]
    public void GetMulti_ReturnsList_WhenPresent()
    {
        var r = WithAnswer("a", new WizardAnswer { Multi = new() { "x", "y" } });
        Assert.Equal(new[] { "x", "y" }, r.GetMulti("a"));
    }

    [Fact]
    public void GetMulti_ReturnsEmpty_WhenMultiNull()
    {
        var r = WithAnswer("a", new WizardAnswer { Multi = null });
        Assert.Empty(r.GetMulti("a"));
    }

    [Fact]
    public void GetMulti_ReturnsEmpty_WhenAbsent()
    {
        Assert.Empty(new WizardResult().GetMulti("missing"));
    }

    [Fact]
    public void GetList_ReturnsList_WhenPresent()
    {
        var entry = new Dictionary<string, WizardAnswer> { ["k"] = new WizardAnswer { Text = "v" } };
        var r = WithAnswer("a", new WizardAnswer { List = new() { entry } });
        Assert.Single(r.GetList("a"));
    }

    [Fact]
    public void GetList_ReturnsEmpty_WhenListNull()
    {
        var r = WithAnswer("a", new WizardAnswer { List = null });
        Assert.Empty(r.GetList("a"));
    }

    [Fact]
    public void GetList_ReturnsEmpty_WhenAbsent()
    {
        Assert.Empty(new WizardResult().GetList("missing"));
    }

    [Fact]
    public void Answers_AreCaseInsensitive()
    {
        var r = WithAnswer("Step", new WizardAnswer { Text = "v" });
        Assert.Equal("v", r.GetText("step"));
    }

    [Fact]
    public void Defaults()
    {
        var r = new WizardResult();
        Assert.Equal(string.Empty, r.DefinitionId);
        Assert.Empty(r.Answers);
        Assert.Equal(0, r.CurrentStepIndex);
        Assert.False(r.Completed);
    }
}

public class WizardAnswerTests
{
    [Fact]
    public void IsEmpty_True_WhenAllUnset()
    {
        Assert.True(new WizardAnswer().IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenTextSet()
    {
        Assert.False(new WizardAnswer { Text = "x" }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenNumberSet()
    {
        Assert.False(new WizardAnswer { Number = 1 }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_True_WhenMultiEmptyList()
    {
        Assert.True(new WizardAnswer { Multi = new() }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenMultiHasItems()
    {
        Assert.False(new WizardAnswer { Multi = new() { "x" } }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_True_WhenListEmptyList()
    {
        Assert.True(new WizardAnswer { List = new() }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenListHasItems()
    {
        Assert.False(new WizardAnswer { List = new() { new() } }.IsEmpty);
    }
}
