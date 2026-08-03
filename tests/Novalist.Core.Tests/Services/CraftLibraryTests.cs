using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The craft reference. Most of these guard the content itself: a thesaurus
/// entry that says "frightened" is the sentence it exists to replace.
/// </summary>
public class CraftLibraryTests
{
    [Fact]
    public void EveryPromptIsNamedAndKinded()
    {
        Assert.NotEmpty(CraftLibrary.Prompts);
        Assert.All(CraftLibrary.Prompts, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.Text));
            Assert.Contains(p.Kind, new[]
            {
                CraftLibrary.KindScene, CraftLibrary.KindCharacter,
                CraftLibrary.KindWorld, CraftLibrary.KindStuck
            });
        });
    }

    [Fact]
    public void EveryKindHasSomethingInIt()
    {
        // A writer picking "stuck" and getting nothing has been told the
        // feature works and that it does not.
        foreach (var kind in new[]
        {
            CraftLibrary.KindScene, CraftLibrary.KindCharacter,
            CraftLibrary.KindWorld, CraftLibrary.KindStuck
        })
        {
            Assert.NotNull(CraftLibrary.PromptAt(0, kind));
        }
    }

    [Fact]
    public void EveryIdIsItsOwn()
    {
        Assert.Equal(
            CraftLibrary.Prompts.Count,
            CraftLibrary.Prompts.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            CraftLibrary.Entries.Count,
            CraftLibrary.Entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            CraftLibrary.Articles.Count,
            CraftLibrary.Articles.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void APromptIsChosenByTheNumberTheCallerGives()
    {
        // The caller owns the randomness, so a writer who liked one can get it
        // back and a test can say which one it expects.
        var first = CraftLibrary.PromptAt(3);
        var again = CraftLibrary.PromptAt(3);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, again!.Id);
    }

    [Fact]
    public void TheNumberWrapsAndSurvivesANegative()
    {
        var count = CraftLibrary.Prompts.Count;

        Assert.Equal(CraftLibrary.PromptAt(0)!.Id, CraftLibrary.PromptAt(count)!.Id);
        Assert.NotNull(CraftLibrary.PromptAt(-1));
        Assert.NotNull(CraftLibrary.PromptAt(int.MinValue));
    }

    [Fact]
    public void AKindNothingUsesGivesNothingRatherThanThrowing()
    {
        Assert.Null(CraftLibrary.PromptAt(0, "no-such-kind"));
    }

    [Fact]
    public void EveryThesaurusEntryCarriesSeveralConcreteSignals()
    {
        Assert.NotEmpty(CraftLibrary.Entries);
        Assert.All(CraftLibrary.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            // One signal is a suggestion; several is somewhere to steal from.
            Assert.True(e.Signals.Count >= 4, e.Key);
            Assert.All(e.Signals, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        });
    }

    [Fact]
    public void NoSignalIsJustAnAdjectiveForTheThingItself()
    {
        // "She was frightened" is the sentence the thesaurus exists to replace,
        // so no entry may hand that back as its own suggestion.
        foreach (var entry in CraftLibrary.Entries)
        {
            foreach (var signal in entry.Signals)
            {
                Assert.False(
                    signal.Contains(entry.Name, StringComparison.CurrentCultureIgnoreCase),
                    $"{entry.Key}: \"{signal}\" just names the thing again");
            }
        }
    }

    [Fact]
    public void AllThreeThesaurusGroupsAreRepresented()
    {
        var groups = CraftLibrary.Entries.Select(e => e.Group).Distinct().ToList();

        Assert.Contains(CraftLibrary.GroupEmotion, groups);
        Assert.Contains(CraftLibrary.GroupSetting, groups);
        Assert.Contains(CraftLibrary.GroupSense, groups);
    }

    [Fact]
    public void SearchFindsAnEntryByItsNameAndBySomethingItSays()
    {
        Assert.Contains(CraftLibrary.Search("fear"), e => e.Key == "fear");
        // By a signal, which is how a writer finds the entry they did not know
        // to look for.
        Assert.NotEmpty(CraftLibrary.Search("pulse"));
    }

    [Fact]
    public void SearchDoesNotCareAboutCase()
    {
        Assert.NotEmpty(CraftLibrary.Search("FEAR"));
        Assert.NotEmpty(CraftLibrary.Search("Grief"));
    }

    [Fact]
    public void AnEmptySearchReturnsEverythingRatherThanNothing()
    {
        // The list is short enough to browse, and browsing is how somebody
        // finds the entry they were not looking for.
        Assert.Equal(CraftLibrary.Entries.Count, CraftLibrary.Search("").Count);
        Assert.Equal(CraftLibrary.Entries.Count, CraftLibrary.Search("   ").Count);
        Assert.Equal(CraftLibrary.Entries.Count, CraftLibrary.Search(null).Count);
    }

    [Fact]
    public void SearchThatMatchesNothingIsEmptyRatherThanEverything()
    {
        Assert.Empty(CraftLibrary.Search("zzzznothing"));
    }

    [Fact]
    public void EveryArticleHasATopicATitleAndRealBody()
    {
        Assert.NotEmpty(CraftLibrary.Articles);
        Assert.All(CraftLibrary.Articles, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Topic));
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            // Short enough to read in a panel, long enough to say something.
            Assert.True(a.Body.Trim().Length > 400, a.Id);
            // Asserted rather than assumed: the bodies are literals in a source
            // file, so a Windows checkout gives them CRLF and the blank line
            // between paragraphs stops being "\n\n" - which is how this passed
            // on one machine and failed on the next.
            Assert.DoesNotContain("\r", a.Body);
            Assert.Contains("\n\n", a.Body);
        });
    }

    [Fact]
    public void AnArticleCanBeLookedUpById()
    {
        var first = CraftLibrary.Articles[0];

        Assert.Equal(first.Id, CraftLibrary.Article(first.Id)!.Id);
        Assert.Null(CraftLibrary.Article("no-such-article"));
    }
}
