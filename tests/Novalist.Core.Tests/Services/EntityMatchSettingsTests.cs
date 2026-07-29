using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Controls over how an entry's name is recognised in prose. The defaults must
/// reproduce the old behaviour exactly, so an existing project reads the same
/// until the writer changes something.
/// </summary>
public class EntityMatchSettingsTests
{
    [Fact]
    public void Defaults_ReproduceTheOldBehaviour()
    {
        var match = new EntityMatchSettings();

        Assert.False(match.CaseSensitive);
        Assert.False(match.MatchPlurals);
        Assert.Empty(match.Exclusions);
        Assert.Empty(match.IgnoredSceneIds);
        // Case-insensitive, nothing excluded, nothing ignored.
        Assert.True(match.Allows("Will", "will", "she will go", "s1"));
    }

    // ── Case sensitivity ──

    [Fact]
    public void CaseSensitive_RejectsADifferentlyCasedHit()
    {
        var match = new EntityMatchSettings { CaseSensitive = true };

        Assert.True(match.Allows("Will", "Will", "Will arrived", null));
        // "she will go" is the verb, not the character.
        Assert.False(match.Allows("Will", "will", "she will go", null));
    }

    [Fact]
    public void CaseInsensitiveByDefault_AcceptsEitherCasing()
    {
        var match = new EntityMatchSettings();

        Assert.True(match.Allows("Raven", "raven", null, null));
        Assert.True(match.Allows("Raven", "RAVEN", null, null));
    }

    // ── Exclusions ──

    [Fact]
    public void Exclusion_SuppressesAMatchInThatPhrase()
    {
        var match = new EntityMatchSettings { Exclusions = ["rose garden", "she rose"] };

        Assert.False(match.Allows("Rose", "Rose", "they walked in the rose garden", null));
        Assert.False(match.Allows("Rose", "Rose", "She rose from the chair", null));
        Assert.True(match.Allows("Rose", "Rose", "Rose opened the door", null));
    }

    [Fact]
    public void Exclusion_IsCaseInsensitive()
    {
        var match = new EntityMatchSettings { Exclusions = ["Rose Garden"] };

        Assert.False(match.Allows("Rose", "Rose", "the rose garden was empty", null));
    }

    [Fact]
    public void Exclusion_WithoutContext_CannotBeChecked()
    {
        // Nothing to test the phrase against, so the match stands rather than
        // being suppressed on a guess.
        var match = new EntityMatchSettings { Exclusions = ["rose garden"] };

        Assert.True(match.Allows("Rose", "Rose", null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Exclusion_BlankEntriesAreIgnored(string exclusion)
    {
        // A blank exclusion matches every context; treating it as real would
        // silently suppress every detection for that entry.
        var match = new EntityMatchSettings { Exclusions = [exclusion] };

        Assert.True(match.Allows("Rose", "Rose", "Rose opened the door", null));
    }

    // ── Per-scene ignore ──

    [Fact]
    public void IgnoredScene_SuppressesEverythingInThatScene()
    {
        var match = new EntityMatchSettings { IgnoredSceneIds = ["scene-7"] };

        Assert.False(match.Allows("Rose", "Rose", "Rose opened the door", "scene-7"));
        Assert.True(match.Allows("Rose", "Rose", "Rose opened the door", "scene-8"));
    }

    [Fact]
    public void IgnoredScene_DoesNotApplyWhenNoSceneIsGiven()
    {
        var match = new EntityMatchSettings { IgnoredSceneIds = ["scene-7"] };

        Assert.True(match.Allows("Rose", "Rose", null, null));
    }

    // ── Plurals ──

    [Fact]
    public void PluralForms_AreEmptyWhenTheFeatureIsOff()
    {
        Assert.Empty(new EntityMatchSettings().PluralFormsOf("Raven"));
    }

    [Fact]
    public void PluralForms_AddASimpleS()
    {
        var match = new EntityMatchSettings { MatchPlurals = true };

        Assert.Contains("Ravens", match.PluralFormsOf("Raven"));
    }

    [Theory]
    [InlineData("Ash", "Ashes")]
    [InlineData("Fox", "Foxes")]
    [InlineData("Church", "Churches")]
    [InlineData("Glass", "Glasses")]
    [InlineData("Blitz", "Blitzes")]
    public void PluralForms_AddEsWhereEnglishRequiresIt(string name, string expected)
    {
        var match = new EntityMatchSettings { MatchPlurals = true };

        Assert.Contains(expected, match.PluralFormsOf(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PluralForms_BlankNameYieldsNothing(string name)
    {
        var match = new EntityMatchSettings { MatchPlurals = true };

        Assert.Empty(match.PluralFormsOf(name));
    }

    // ── Reaching the resolver ──

    private static Dictionary<string, (string Id, string TypeKey)> BuildIndex(params LoreData[] lore) =>
        EntityResolveIndex.Build([], [], [], lore, []);

    [Fact]
    public void Resolver_PluralMatchingAddsThePluralAsItsOwnKey()
    {
        var faction = new LoreData
        {
            Id = "l1",
            Name = "Raven",
            Match = new EntityMatchSettings { MatchPlurals = true }
        };

        var index = BuildIndex(faction);

        Assert.True(index.ContainsKey("Raven"));
        Assert.True(index.ContainsKey("Ravens"));
        Assert.Equal("l1", index["Ravens"].Id);
    }

    [Fact]
    public void Resolver_WithoutPluralMatching_OnlyTheNameResolves()
    {
        var index = BuildIndex(new LoreData { Id = "l1", Name = "Raven" });

        Assert.True(index.ContainsKey("Raven"));
        Assert.False(index.ContainsKey("Ravens"));
    }

    [Fact]
    public void Resolver_PluralCollidingWithAnotherEntryIsDroppedAsAmbiguous()
    {
        // Two entries claiming "Ravens" resolve to neither, exactly as two
        // entries claiming the same name already did.
        var index = BuildIndex(
            new LoreData { Id = "l1", Name = "Raven", Match = new EntityMatchSettings { MatchPlurals = true } },
            new LoreData { Id = "l2", Name = "Ravens" });

        Assert.False(index.ContainsKey("Ravens"));
        Assert.True(index.ContainsKey("Raven"));
    }

    [Fact]
    public void Resolver_AliasesAlsoGetPlurals()
    {
        var faction = new LoreData
        {
            Id = "l1",
            Name = "Raven",
            Aliases = ["Corvid"],
            Match = new EntityMatchSettings { MatchPlurals = true }
        };

        var index = BuildIndex(faction);

        Assert.True(index.ContainsKey("Corvids"));
    }
}
