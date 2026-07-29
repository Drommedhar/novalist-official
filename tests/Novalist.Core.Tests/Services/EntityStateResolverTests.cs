using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What an entry is like at a point in the story.
///
/// Only characters had this. A city razed in act two could only be described as
/// it is at the end, which meant reading the Codex in chapter three told you the
/// ending.
/// </summary>
public class EntityStateResolverTests
{
    private static ResolvedEntityState Resolve(
        IReadOnlyList<EntityStateOverride> overrides,
        string? act = null, string? chapterGuid = null,
        string? chapterTitle = null, string? sceneTitle = null)
        => EntityStateResolver.Resolve(overrides, act, chapterGuid, chapterTitle, sceneTitle);

    [Fact]
    public void AnEntryWithNoOverridesReadsAsItself()
    {
        var resolved = Resolve([]);

        Assert.False(resolved.IsOverridden);
        Assert.Null(resolved.Description);
        Assert.Empty(resolved.ScopeLabel);
    }

    [Fact]
    public void AChapterOverrideAppliesInThatChapter()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Description = "Razed." }],
            chapterGuid: "ch-2");

        Assert.True(resolved.IsOverridden);
        Assert.Equal("Razed.", resolved.Description);
    }

    [Fact]
    public void AChapterOverrideDoesNotApplyElsewhere()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Description = "Razed." }],
            chapterGuid: "ch-1");

        Assert.False(resolved.IsOverridden);
    }

    [Fact]
    public void AChapterMatchesByTitleTooBecauseAWriterMayHaveEditedTheFile()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "The Fall", Description = "Razed." }],
            chapterTitle: "The Fall");

        Assert.True(resolved.IsOverridden);
    }

    [Fact]
    public void ASceneOverrideBeatsTheChapterOne()
    {
        // Restating in a narrower scope is how a writer says "and by this
        // scene, it is worse".
        var resolved = Resolve(
            [
                new EntityStateOverride { Chapter = "ch-2", Description = "Burning." },
                new EntityStateOverride { Chapter = "ch-2", Scene = "The Fire", Description = "Ash." }
            ],
            chapterGuid: "ch-2", sceneTitle: "The Fire");

        Assert.Equal("Ash.", resolved.Description);
    }

    [Fact]
    public void AChapterOverrideStillAppliesInItsOtherScenes()
    {
        var resolved = Resolve(
            [
                new EntityStateOverride { Chapter = "ch-2", Description = "Burning." },
                new EntityStateOverride { Chapter = "ch-2", Scene = "The Fire", Description = "Ash." }
            ],
            chapterGuid: "ch-2", sceneTitle: "Somewhere Else");

        Assert.Equal("Burning.", resolved.Description);
    }

    [Fact]
    public void AnActOverrideAppliesWhenNoChapterOneDoes()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Act = "Two", Description = "Falling." }],
            act: "Two", chapterGuid: "ch-9");

        Assert.Equal("Falling.", resolved.Description);
    }

    [Fact]
    public void AChapterOverrideBeatsAnActOne()
    {
        var resolved = Resolve(
            [
                new EntityStateOverride { Act = "Two", Description = "Falling." },
                new EntityStateOverride { Chapter = "ch-2", Description = "Fallen." }
            ],
            act: "Two", chapterGuid: "ch-2");

        Assert.Equal("Fallen.", resolved.Description);
    }

    [Fact]
    public void AnOverrideThatRestatesNothingIsIgnored()
    {
        // It would otherwise claim the entry differs here while saying nothing
        // about how.
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Note = "just a note" }],
            chapterGuid: "ch-2");

        Assert.False(resolved.IsOverridden);
    }

    [Fact]
    public void AnyFieldCanBeRestated()
    {
        var resolved = Resolve(
            [
                new EntityStateOverride
                {
                    Chapter = "ch-2",
                    Fields = new Dictionary<string, string> { ["Owner"] = "The thief" }
                }
            ],
            chapterGuid: "ch-2");

        Assert.Equal("The thief", resolved.Fields["Owner"]);
    }

    [Fact]
    public void ANameCanBeRestated()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Name = "The Ruins" }],
            chapterGuid: "ch-2");

        Assert.Equal("The Ruins", resolved.Name);
    }

    [Fact]
    public void TheNoteComesAlongSoTheWriterSeesWhy()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Description = "Razed.", Note = "The siege." }],
            chapterGuid: "ch-2");

        Assert.Equal("The siege.", resolved.Note);
    }

    // ── The scope label ──

    [Fact]
    public void TheScopeLabelPrefersTheChapterTitleOverAStoredGuid()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Description = "Razed." }],
            chapterGuid: "ch-2", chapterTitle: "The Fall");

        Assert.Equal("Ch: The Fall", resolved.ScopeLabel);
    }

    [Fact]
    public void TheScopeLabelNamesTheSceneWhenThereIsOne()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Chapter = "ch-2", Scene = "The Fire", Description = "Ash." }],
            chapterGuid: "ch-2", chapterTitle: "The Fall", sceneTitle: "The Fire");

        Assert.Equal("Ch: The Fall - Sc: The Fire", resolved.ScopeLabel);
    }

    [Fact]
    public void AnActScopedOverrideSaysSo()
    {
        var resolved = Resolve(
            [new EntityStateOverride { Act = "Two", Description = "Falling." }],
            act: "Two");

        Assert.Equal("Act: Two", resolved.ScopeLabel);
    }

    [Fact]
    public void AnOverrideScopedToNothingStillLabelsItself()
    {
        // Otherwise IsOverridden would be true with an empty label, which reads
        // as "not overridden" everywhere it is shown.
        var resolved = EntityStateResolver.Resolve(
            [new EntityStateOverride { Chapter = "", Description = "Always like this." }],
            null, "", null, null);

        Assert.False(resolved.IsOverridden);
    }

    [Fact]
    public void ScopeLabelForAnUnscopedOverrideReadsAsEverywhere()
    {
        Assert.Equal(
            "Everywhere",
            EntityStateResolver.ScopeLabelFor(new EntityStateOverride(), null));
    }

    // ── The model ──

    [Fact]
    public void EveryEntityTypeCarriesTheCollection()
    {
        Assert.Empty(new LocationData().StateOverrides);
        Assert.Empty(new ItemData().StateOverrides);
        Assert.Empty(new LoreData().StateOverrides);
        Assert.Empty(new CustomEntityData().StateOverrides);
        Assert.Empty(new CharacterData().StateOverrides);
    }

    [Fact]
    public void HasValuesIsFalseForAnEmptyOverride()
    {
        Assert.False(new EntityStateOverride().HasValues);
        Assert.True(new EntityStateOverride { Description = "x" }.HasValues);
        Assert.True(new EntityStateOverride { Name = "x" }.HasValues);
        Assert.True(new EntityStateOverride
        {
            Fields = new Dictionary<string, string> { ["k"] = "v" }
        }.HasValues);
    }
}
