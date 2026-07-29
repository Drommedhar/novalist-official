using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What a Codex entry is allowed to show an AI model.
///
/// The load-bearing case is the negative one: an entry the writer marked Never
/// must not reach a model however relevant it looks, because there is no taking
/// it back afterwards.
/// </summary>
public class AiContextPolicyTests
{
    private static AiContextEntry Entry(
        string id, AiInclusion inclusion, params EntitySection[] sections)
        => new(id, "character", id, inclusion, sections);

    private static EntitySection Section(string title, bool hidden = false)
        => new() { Title = title, Content = title + " body", AiHidden = hidden };

    private static IReadOnlySet<string> Mentions(params string[] ids)
        => ids.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void AMentionedEntryGoesThroughByDefault()
    {
        var allowed = AiContextPolicy.Allowed([Entry("a", AiInclusion.WhenMentioned)], Mentions("a"));

        Assert.Equal(["a"], allowed.Select(e => e.Id));
    }

    [Fact]
    public void AnUnmentionedEntryStaysOutByDefault()
    {
        Assert.Empty(AiContextPolicy.Allowed([Entry("a", AiInclusion.WhenMentioned)], Mentions()));
    }

    [Fact]
    public void AlwaysGoesThroughEvenWhenTheSceneNeverNamesIt()
    {
        var allowed = AiContextPolicy.Allowed([Entry("a", AiInclusion.Always)], Mentions());

        Assert.Equal(["a"], allowed.Select(e => e.Id));
    }

    [Fact]
    public void NeverStaysOutEvenWhenTheSceneIsAboutIt()
    {
        // The whole point of the setting: relevance must not override the
        // writer's decision.
        Assert.Empty(AiContextPolicy.Allowed([Entry("a", AiInclusion.Never)], Mentions("a")));
    }

    [Fact]
    public void EachEntryIsJudgedOnItsOwn()
    {
        var allowed = AiContextPolicy.Allowed(
            [
                Entry("mentioned", AiInclusion.WhenMentioned),
                Entry("absent", AiInclusion.WhenMentioned),
                Entry("pinned", AiInclusion.Always),
                Entry("secret", AiInclusion.Never)
            ],
            Mentions("mentioned", "secret"));

        Assert.Equal(["mentioned", "pinned"], allowed.Select(e => e.Id));
    }

    // ── Withheld sections ──

    [Fact]
    public void AWithheldSectionIsStrippedFromAnEntryThatGoesThrough()
    {
        var allowed = AiContextPolicy.Allowed(
            [Entry("a", AiInclusion.Always, Section("Public"), Section("The twist", hidden: true))],
            Mentions());

        Assert.Equal(["Public"], allowed.Single().Sections.Select(s => s.Title));
    }

    [Fact]
    public void WithholdingEverySectionStillSendsTheEntryItself()
    {
        // The name and the fact that the character exists are not secret; the
        // sections are. Dropping the whole entry would be a different decision.
        var allowed = AiContextPolicy.Allowed(
            [Entry("a", AiInclusion.Always, Section("Only one", hidden: true))],
            Mentions());

        Assert.Single(allowed);
        Assert.Empty(allowed.Single().Sections);
    }

    [Fact]
    public void RedactLeavesAnEntryWithNothingHiddenAlone()
    {
        var entry = Entry("a", AiInclusion.Always, Section("One"), Section("Two"));

        Assert.Equal(2, AiContextPolicy.Redact(entry).Sections.Count);
    }

    [Fact]
    public void RedactDoesNotMutateTheEntryItWasGiven()
    {
        // The caller may still be showing this entry in the Codex, where the
        // hidden section is perfectly visible.
        var entry = Entry("a", AiInclusion.Always, Section("Kept"), Section("Hidden", hidden: true));

        AiContextPolicy.Redact(entry);

        Assert.Equal(2, entry.Sections.Count);
    }

    // ── The plain question ──

    [Theory]
    [InlineData(AiInclusion.WhenMentioned, true)]
    [InlineData(AiInclusion.Always, true)]
    [InlineData(AiInclusion.Never, false)]
    public void MaySendAnswersWithoutNeedingAScene(AiInclusion inclusion, bool expected)
    {
        Assert.Equal(expected, AiContextPolicy.MaySend(inclusion));
    }

    [Fact]
    public void EveryEntryTypeDefaultsToTheOldBehaviour()
    {
        // An existing project must read exactly as it did before this setting
        // existed: entries reach a model when the scene mentions them.
        Assert.Equal(AiInclusion.WhenMentioned, new CharacterData().Ai);
        Assert.Equal(AiInclusion.WhenMentioned, new LocationData().Ai);
        Assert.Equal(AiInclusion.WhenMentioned, new ItemData().Ai);
        Assert.Equal(AiInclusion.WhenMentioned, new LoreData().Ai);
        Assert.Equal(AiInclusion.WhenMentioned, new CustomEntityData().Ai);
    }

    [Fact]
    public void ASectionIsVisibleToAiUnlessTheWriterSaysOtherwise()
    {
        Assert.False(new EntitySection().AiHidden);
    }
}
