using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class SceneEditingOwnersTests
{
    [Fact]
    public void WindowsAndPanesKeepIndependentBusyClaims()
    {
        var editing = new SceneEditingState();
        editing.SetOwner("main", [("chapter", "first", true), ("chapter", "second", true)]);
        editing.SetOwner("detached", [("other", "third", true), ("other", null, true)]);
        Assert.True(editing.IsBusy("chapter", "first"));
        Assert.True(editing.IsBusy("chapter", "second"));
        Assert.True(editing.IsBusy("other", "third"));
        Assert.False(editing.IsBusy("other", "first"));
        editing.SetOwner("main", [("chapter", "first", false)]);
        Assert.False(editing.IsBusy("chapter", "second"));
        Assert.Equal(("other", "third", true), editing.Current);
        editing.RemoveOwner("detached");
        Assert.False(editing.Current.Dirty);
        Assert.False(editing.IsBusy("other", "third"));
        editing.Set("legacy", "old", true);
        editing.SetOwner("main", [("chapter", "new", true)]);
        editing.ClearOwners();
        Assert.Equal((null, null, false), editing.Current);
        Assert.False(editing.IsBusy("legacy", "old"));
        Assert.False(editing.IsBusy("chapter", "new"));
    }
}
