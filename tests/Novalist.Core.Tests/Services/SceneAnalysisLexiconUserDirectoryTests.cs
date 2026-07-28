using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the user lexicon directory — dropping an <c>analysis.&lt;tag&gt;.json</c>
/// into a folder adds a writing language the app does not ship, and overrides a
/// shipped one of the same tag. Registration is process-wide static state, so
/// every test here restores the shipped-only set before it returns.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public sealed class SceneAnalysisLexiconUserDirectoryTests : IDisposable
{
    private readonly string _dir;

    /// <summary>A minimal but complete lexicon; the emotion key set matches the
    /// shipped files so the cross-language key assertions still hold while this
    /// language is registered.</summary>
    private static string LexiconJson(string positiveWord) => $$"""
        {
          "positive": ["{{positiveWord}}"],
          "negative": ["malbona"],
          "conflict": ["batalo"],
          "speechVerbs": ["diris"],
          "firstPerson": ["mi"],
          "pronounsMale": ["li"],
          "pronounsFemale": ["ŝi"],
          "genderMale": ["viro"],
          "genderFemale": ["virino"],
          "wordBoundaries": true,
          "emotions": [{ "key": "joy", "words": ["ĝojo"] }]
        }
        """;

    public SceneAnalysisLexiconUserDirectoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nl-lexicon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SceneAnalysisLexicon.RegisterUserDirectory(null);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Write(string tag, string json)
        => File.WriteAllText(Path.Combine(_dir, $"analysis.{tag}.json"), json);

    [Fact]
    public void RegisterUserDirectory_AddsALanguageNovalistDoesNotShip()
    {
        Write("eo", LexiconJson("bona"));

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Contains("eo", SceneAnalysisLexicon.AvailableLanguages);
        Assert.True(SceneAnalysisLexicon.Supports("eo"));

        var lexicon = SceneAnalysisLexicon.For("eo");
        Assert.NotNull(lexicon);
        Assert.Equal("eo", lexicon!.Language);
        Assert.Contains("bona", lexicon.Positive);
        Assert.Contains("diris", lexicon.SpeechVerbs);
        Assert.Equal(["joy"], lexicon.EmotionKeys);
    }

    [Fact]
    public void RegisterUserDirectory_KeepsTheShippedLanguages()
    {
        Write("eo", LexiconJson("bona"));

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Contains("en", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Contains("de", SceneAnalysisLexicon.AvailableLanguages);
        Assert.NotNull(SceneAnalysisLexicon.For("en"));
    }

    [Fact]
    public void AUserFile_OverridesTheShippedLexiconOfTheSameTag()
    {
        Write("de", LexiconJson("wunderbar"));

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        var german = SceneAnalysisLexicon.For("de");
        Assert.NotNull(german);
        Assert.Equal(["wunderbar"], german!.Positive);
        // The tag is listed once, not twice, now that two sources supply it.
        Assert.Single(SceneAnalysisLexicon.AvailableLanguages, tag => tag == "de");
    }

    [Fact]
    public void RegisteringNull_GoesBackToTheShippedSet()
    {
        Write("eo", LexiconJson("bona"));
        SceneAnalysisLexicon.RegisterUserDirectory(_dir);
        Assert.Contains("eo", SceneAnalysisLexicon.AvailableLanguages);

        SceneAnalysisLexicon.RegisterUserDirectory(null);

        Assert.DoesNotContain("eo", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Null(SceneAnalysisLexicon.For("eo"));
    }

    [Fact]
    public void RegisteringBlank_IsTreatedAsNoDirectory()
    {
        SceneAnalysisLexicon.RegisterUserDirectory("   ");

        Assert.Equal(
            SceneAnalysisLexicon.AvailableLanguages,
            SceneAnalysisLexicon.AvailableLanguages.Distinct().ToList());
        Assert.DoesNotContain("eo", SceneAnalysisLexicon.AvailableLanguages);
    }

    [Fact]
    public void RegisteringAMissingDirectory_DegradesToTheShippedSet()
    {
        SceneAnalysisLexicon.RegisterUserDirectory(Path.Combine(_dir, "nope"));

        Assert.Contains("en", SceneAnalysisLexicon.AvailableLanguages);
        Assert.DoesNotContain("eo", SceneAnalysisLexicon.AvailableLanguages);
    }

    [Fact]
    public void AnUnusableUserFile_LeavesTheLanguageUnsupported()
    {
        // The tag is discovered from the file name, so it lists; the file itself
        // is unparseable, so loading it yields nothing rather than throwing.
        Write("eo", "{ not json");

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Contains("eo", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Null(SceneAnalysisLexicon.For("eo"));
    }

    [Fact]
    public void AFileWithNoTag_IsIgnored()
    {
        File.WriteAllText(Path.Combine(_dir, "analysis..json"), LexiconJson("bona"));
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "ignored");

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Equal(
            SceneAnalysisLexicon.AvailableLanguages.Count,
            SceneAnalysisLexicon.AvailableLanguages.Distinct().Count());
        Assert.All(SceneAnalysisLexicon.AvailableLanguages, tag => Assert.NotEqual(string.Empty, tag));
    }

    [Fact]
    public void AUserFileThatCannotBeRead_LeavesTheLanguageUnsupported()
    {
        Write("eo", LexiconJson("bona"));
        var path = Path.Combine(_dir, "analysis.eo.json");

        // Held open exclusively: the tag still lists (that only reads names) but
        // loading it throws, and must degrade to "no lexicon" rather than fail.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Contains("eo", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Null(SceneAnalysisLexicon.For("eo"));
    }

    [Fact]
    public void ARegionalUserTag_IsReachableFromItsBaseLanguage()
    {
        Write("eo-XX", LexiconJson("bona"));

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.True(SceneAnalysisLexicon.Supports("eo"));
        Assert.Equal("eo-XX", SceneAnalysisLexicon.For("eo")!.Language);
    }

    [Fact]
    public void ClassifyGender_ConsultsAUserLexicon()
    {
        Write("eo", LexiconJson("bona"));

        SceneAnalysisLexicon.RegisterUserDirectory(_dir);

        Assert.Equal(DialogueGender.Male, SceneAnalysisLexicon.ClassifyGender("viro"));
        Assert.Equal(DialogueGender.Female, SceneAnalysisLexicon.ClassifyGender("virino"));
    }
}
