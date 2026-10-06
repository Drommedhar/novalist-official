using System.Text.RegularExpressions;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers how a segment's direction is arrived at: the writer's own word first,
/// then the speech verb the prose already put in the tag, then the scene's
/// emotion, then nothing. No model is involved anywhere in here, which is what
/// makes the whole of it assertable.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class EmotionDirectorTests
{
    private static DirectionLanguage English()
        => EmotionDirector.BuildLanguage(SceneAnalysisLexicon.For("en"));

    /// <summary>A language whose matcher fires on a verb its map does not carry.
    /// The shipped files can never be in that state, but the resolver has to
    /// fall through rather than direct a line on a verb it cannot name.</summary>
    private static DirectionLanguage UnmappedVerb()
        => new(new Regex("(said)", RegexOptions.IgnoreCase), new Dictionary<string, string>());

    [Fact]
    public void Resolve_WriterDirectionWinsOverEverything()
    {
        var direction = EmotionDirector.Resolve(
            "joyful",
            new EmotionDirectionContext()
            {
                ContextAfter = " she screamed.",
                SceneEmotion = "sorrowful",
                SceneIntensity = 10
            },
            English());

        Assert.Equal("joyful", direction.Key);
        Assert.Equal(DirectionSource.Writer, direction.Source);
        Assert.Null(direction.Evidence);
    }

    [Fact]
    public void Resolve_BlankWriterDirectionIsReadPlainlyRatherThanFallingBack()
    {
        // A stored blank is the writer saying "no performance on this line".
        // Falling through to the scene's emotion would quietly undo them.
        var direction = EmotionDirector.Resolve(
            "  ",
            new EmotionDirectionContext()
            {
                ContextAfter = " she screamed.",
                SceneEmotion = "sorrowful"
            },
            English());

        Assert.Equal(EmotionDirector.NeutralKey, direction.Key);
        Assert.Equal(DirectionSource.Writer, direction.Source);
    }

    [Fact]
    public void Resolve_TakesTheSpeechVerbFromTheTagAfterTheQuote()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " she snapped, not turning round.",
                SceneEmotion = "peaceful"
            },
            English());

        Assert.Equal("angry", direction.Key);
        Assert.Equal(DirectionSource.Verb, direction.Source);
        Assert.Equal("snapped", direction.Evidence);
    }

    [Fact]
    public void Resolve_ReadsTheTagBeforeTheQuoteWhenNothingFollowsIt()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = "  ",
                ContextBefore = "Aldric whispered, close to her ear. "
            },
            English());

        Assert.Equal("peaceful", direction.Key);
        Assert.Equal("whispered", direction.Evidence);
    }

    [Fact]
    public void Resolve_TheFollowingTagWinsWhenBothCarryAVerb()
    {
        // "she snapped" is attached to this line; the verb in the prose leading
        // up to it belongs to whatever came before.
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " she snapped.",
                ContextBefore = "He had whispered it twice already. "
            },
            English());

        Assert.Equal("angry", direction.Key);
        Assert.Equal("snapped", direction.Evidence);
    }

    [Fact]
    public void Resolve_AVerbTheLanguageDoesNotMapDirectsNothing()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " she said."
            },
            UnmappedVerb());

        Assert.Equal(EmotionDirector.NeutralKey, direction.Key);
        Assert.Equal(DirectionSource.None, direction.Source);
    }

    [Fact]
    public void Resolve_FallsBackToTheScenesOwnEmotion()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " she said, and looked away.",
                SceneEmotion = " tense "
            },
            English());

        Assert.Equal("tense", direction.Key);
        Assert.Equal(DirectionSource.Scene, direction.Source);
        Assert.Null(direction.Evidence);
    }

    [Fact]
    public void Resolve_NothingSaidAnywhereIsNeutralRatherThanAGuess()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                SceneEmotion = "   "
            },
            English());

        Assert.Equal(EmotionDirector.NeutralKey, direction.Key);
        Assert.Equal(DirectionSource.None, direction.Source);
    }

    [Fact]
    public void Resolve_NarrationMagnitudeHoldsTheProseBackFromTheDialogue()
    {
        var spoken = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                SceneEmotion = "angry"
            },
            English());
        var prose = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                SceneEmotion = "angry"
            },
            English(),
            EmotionDirector.NarrationMagnitude);

        Assert.Equal(spoken.Key, prose.Key);
        Assert.True(Sum(prose.Vector) < Sum(spoken.Vector));
    }

    [Fact]
    public void BuildLanguage_NoLexiconMatchesNothing()
    {
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " she snapped."
            },
            EmotionDirector.BuildLanguage(null));

        Assert.Equal(DirectionSource.None, direction.Source);
    }

    [Fact]
    public void BuildLanguage_MatchesTheLongestVerbFirst()
    {
        // "murmured back" must not be read as "murmured" and given the wrong
        // half of its meaning.
        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = " he murmured back."
            },
            English());

        Assert.Equal("murmured back", direction.Evidence);
    }

    [Fact]
    public void BuildLanguage_WorksInALanguageWithoutWordBoundaries()
    {
        var chinese = EmotionDirector.BuildLanguage(SceneAnalysisLexicon.For("zh-CN"));

        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = "她低声说道。"
            },
            chinese);

        Assert.Equal(DirectionSource.Verb, direction.Source);
    }

    [Fact]
    public void BuildLanguage_GermanDirectsOnItsOwnVerbs()
    {
        var german = EmotionDirector.BuildLanguage(SceneAnalysisLexicon.For("de"));

        var direction = EmotionDirector.Resolve(
            null,
            new EmotionDirectionContext()
            {
                ContextAfter = ", flüsterte sie."
            },
            german);

        Assert.Equal(DirectionSource.Verb, direction.Source);
        Assert.Equal("flüsterte", direction.Evidence);
    }

    [Fact]
    public void Vector_EveryShippedEmotionKeyStaysInsideTheEngineBudget()
    {
        var keys = SceneAnalysisLexicon.For("en")!.EmotionKeys;

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            // At the largest scale the scene's intensity can apply. A runtime
            // clamp would rescale a bad entry into a different emotion instead.
            var vector = EmotionDirector.Vector(key, 10);
            Assert.NotEmpty(vector);
            Assert.True(
                Sum(vector) <= EmotionDirector.MaxVectorSum,
                key + " sums to " + Sum(vector));
            Assert.All(vector, pair => Assert.Contains(pair.Key, EmotionDirector.Dimensions));
        }
    }

    [Fact]
    public void Vector_AnUnknownKeyReadsAsNeutral()
    {
        Assert.Equal(
            EmotionDirector.Vector("neutral", null), EmotionDirector.Vector("unheard-of", null));
        Assert.Equal(
            EmotionDirector.Vector("neutral", null), EmotionDirector.Vector(null, null));
    }

    [Fact]
    public void Vector_IntensityScalesTheSameEmotionWithoutChangingIt()
    {
        var calm = EmotionDirector.Vector("angry", -10);
        var unbearable = EmotionDirector.Vector("angry", 10);
        var unrated = EmotionDirector.Vector("angry", null);

        Assert.Equal(calm.Keys, unbearable.Keys);
        Assert.True(Sum(calm) < Sum(unrated));
        Assert.True(Sum(unrated) < Sum(unbearable));
    }

    [Fact]
    public void Vector_IntensityOutsideTheScaleIsClamped()
    {
        Assert.Equal(EmotionDirector.Vector("angry", 10), EmotionDirector.Vector("angry", 400));
        Assert.Equal(EmotionDirector.Vector("angry", -10), EmotionDirector.Vector("angry", -400));
    }

    [Fact]
    public void Vector_MagnitudeIsClampedToTheUnitRange()
    {
        Assert.Equal(
            EmotionDirector.Vector("angry", null, 1.0),
            EmotionDirector.Vector("angry", null, 9.0));
        Assert.All(
            EmotionDirector.Vector("angry", null, -3.0),
            pair => Assert.Equal(0, pair.Value));
    }

    private static double Sum(IReadOnlyDictionary<string, double> vector)
        => vector.Values.Sum();
}
