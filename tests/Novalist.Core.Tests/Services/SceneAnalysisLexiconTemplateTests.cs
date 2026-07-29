using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The starting lexicon handed to someone adding a language. A template the
/// loader cannot read is worse than none: the contributor ends up debugging our
/// file instead of translating theirs.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class SceneAnalysisLexiconTemplateTests
{
    [Fact]
    public void Template_ParsesBackIntoAUsableLexicon()
    {
        var lexicon = SceneAnalysisLexicon.Parse(SceneAnalysisLexicon.TemplateFor("nl"), "nl");

        Assert.NotNull(lexicon);
        Assert.NotEmpty(lexicon!.Conflict);
        Assert.NotEmpty(lexicon.SpeechVerbs);
        Assert.NotEmpty(lexicon.Emotions);
    }

    [Fact]
    public void Template_NamesTheLanguageItIsFor()
    {
        Assert.Contains("'nl'", SceneAnalysisLexicon.TemplateFor("nl"));
    }

    [Fact]
    public void Template_IsSeededFromEnglishRatherThanBlank()
    {
        // The useful work is translating a real list, not guessing which keys
        // exist, so the English words come along.
        var english = SceneAnalysisLexicon.Parse(SceneAnalysisLexicon.TemplateFor("nl"), "nl")!;

        Assert.Contains("said", english.SpeechVerbs);
    }
}
