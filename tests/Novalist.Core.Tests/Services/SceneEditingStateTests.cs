using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The guard that stops an extension writing over the scene the writer has
/// open. Every case here is a way somebody's words could go missing.
/// </summary>
public class SceneEditingStateTests
{
    [Fact]
    public void NothingIsBusyBeforeTheEditorSaysAnything()
    {
        var state = new SceneEditingState();

        Assert.False(state.IsBusy("c1", "s1"));
        Assert.Equal((null, null, false), state.Current);
    }

    [Fact]
    public void AnOpenSceneWithUnsavedChangesIsBusy()
    {
        var state = new SceneEditingState();
        state.Set("c1", "s1", dirty: true);

        Assert.True(state.IsBusy("c1", "s1"));
        Assert.Equal(("c1", "s1", true), state.Current);
    }

    [Fact]
    public void AnOpenSceneWithNothingUnsavedIsNotBusy()
    {
        // The file on disk is what the editor has, so a write cannot lose
        // anything. Refusing here would block a pass for no reason.
        var state = new SceneEditingState();
        state.Set("c1", "s1", dirty: false);

        Assert.False(state.IsBusy("c1", "s1"));
    }

    [Fact]
    public void OnlyTheOpenSceneIsBusy()
    {
        var state = new SceneEditingState();
        state.Set("c1", "s1", dirty: true);

        Assert.False(state.IsBusy("c1", "s2"));
    }

    [Fact]
    public void TheChapterHasToMatchToo()
    {
        // Scene ids are unique per chapter, so an id alone is not an identity.
        var state = new SceneEditingState();
        state.Set("c1", "s1", dirty: true);

        Assert.False(state.IsBusy("c2", "s1"));
    }

    [Fact]
    public void ClosingTheEditorReleasesTheScene()
    {
        var state = new SceneEditingState();
        state.Set("c1", "s1", dirty: true);
        state.Set(null, null, dirty: false);

        Assert.False(state.IsBusy("c1", "s1"));
    }

    [Fact]
    public void NothingOpenCannotBeDirty()
    {
        // A caller reporting dirty with no scene would otherwise leave a stale
        // chapter marked busy forever.
        var state = new SceneEditingState();
        state.Set("c1", null, dirty: true);

        Assert.False(state.Current.Dirty);
        Assert.False(state.IsBusy("c1", "s1"));
    }
}
