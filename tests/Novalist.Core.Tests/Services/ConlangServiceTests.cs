using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// An invented language and its dictionary. The lookup cases matter most: the
/// thing a lexicon is for is finding out mid-sentence whether a word has
/// already been coined.
/// </summary>
public class ConlangServiceTests
{
    private static (ConlangService Sut, ProjectMetadata Project) Build()
    {
        var project = new ProjectMetadata();
        var projects = Substitute.For<IProjectService>();
        projects.CurrentProject.Returns(project);
        projects.SaveProjectAsync().Returns(Task.CompletedTask);
        return (new ConlangService(projects), project);
    }

    private static async Task<(ConlangService Sut, string LanguageId)> WithLanguageAsync(
        params (string Word, string Meaning)[] words)
    {
        var (sut, _) = Build();
        var language = await sut.CreateAsync("Kelmari");
        foreach (var (word, meaning) in words)
            await sut.SaveWordAsync(language!.Id, new ConlangWord { Word = word, Meaning = meaning });
        return (sut, language!.Id);
    }

    [Fact]
    public async Task ALanguageCanBeMadeRenamedAndDescribed()
    {
        var (sut, _) = Build();

        var language = await sut.CreateAsync("Kelmari");
        Assert.NotNull(language);
        Assert.Equal("Kelmari", language!.Name);

        Assert.True(await sut.UpdateAsync(language.Id, "Kelmarin", "Spoken along the river."));
        var stored = Assert.Single(sut.GetAll());
        Assert.Equal("Kelmarin", stored.Name);
        Assert.Equal("Spoken along the river.", stored.Description);
    }

    [Fact]
    public async Task ALanguageWithNoNameStillHasOne()
    {
        var (sut, _) = Build();

        var language = await sut.CreateAsync("   ");

        Assert.Equal("Language", language!.Name);
    }

    [Fact]
    public async Task WithNoProjectOpenNothingIsMadeAndNothingThrows()
    {
        var projects = Substitute.For<IProjectService>();
        projects.CurrentProject.Returns((ProjectMetadata?)null);
        var sut = new ConlangService(projects);

        Assert.Null(await sut.CreateAsync("Kelmari"));
        Assert.Empty(sut.GetAll());
        Assert.Empty(sut.Lookup("anything"));
        Assert.False(await sut.UpdateAsync("x", "y", null));
        Assert.False(await sut.DeleteAsync("x"));
        Assert.Null(await sut.SaveWordAsync("x", new ConlangWord()));
        Assert.False(await sut.DeleteWordAsync("x", "y"));
    }

    [Fact]
    public async Task AWordIsStoredTrimmedAndCanBeRewritten()
    {
        var (sut, languageId) = await WithLanguageAsync();

        var word = await sut.SaveWordAsync(languageId, new ConlangWord
        {
            Word = "  vael  ",
            Meaning = "  river  ",
            PartOfSpeech = " noun ",
            Pronunciation = " VAY-el "
        });

        Assert.Equal("vael", word!.Word);
        Assert.Equal("river", word.Meaning);
        Assert.Equal("noun", word.PartOfSpeech);
        Assert.Equal("VAY-el", word.Pronunciation);

        // Rewriting the same word rather than adding a second one.
        await sut.SaveWordAsync(languageId, new ConlangWord
        {
            Id = word.Id,
            Word = "vael",
            Meaning = "great river"
        });

        var stored = Assert.Single(sut.GetAll()[0].Words);
        Assert.Equal("great river", stored.Meaning);
    }

    [Fact]
    public async Task AWordInALanguageThatIsNotThereIsRefused()
    {
        var (sut, _) = await WithLanguageAsync();

        Assert.Null(await sut.SaveWordAsync("no-such-language", new ConlangWord { Word = "x" }));
        Assert.False(await sut.DeleteWordAsync("no-such-language", "x"));
    }

    [Fact]
    public async Task AWordCanBeRemoved()
    {
        var (sut, languageId) = await WithLanguageAsync(("vael", "river"));
        var wordId = sut.GetAll()[0].Words[0].Id;

        Assert.True(await sut.DeleteWordAsync(languageId, wordId));
        Assert.Empty(sut.GetAll()[0].Words);
        Assert.False(await sut.DeleteWordAsync(languageId, wordId));
    }

    [Fact]
    public async Task ALanguageCanBeRemovedWithItsWords()
    {
        var (sut, languageId) = await WithLanguageAsync(("vael", "river"));

        Assert.True(await sut.DeleteAsync(languageId));
        Assert.Empty(sut.GetAll());
        Assert.False(await sut.DeleteAsync(languageId));
    }

    // ── Looking a word up ──

    [Fact]
    public async Task AWordIsFoundByWhatItIs()
    {
        var (sut, _) = await WithLanguageAsync(("vael", "river"), ("thonn", "stone"));

        var hits = sut.Lookup("vael");

        var hit = Assert.Single(hits);
        Assert.Equal("river", hit.Word.Meaning);
        Assert.Equal("Kelmari", hit.LanguageName);
    }

    [Fact]
    public async Task AWordIsAlsoFoundByWhatItMeans()
    {
        // The half that makes it a dictionary rather than a glossary: a writer
        // mid-sentence wants to know whether they already coined a word.
        var (sut, _) = await WithLanguageAsync(("vael", "river"), ("thonn", "stone"));

        var hits = sut.Lookup("river");

        Assert.Single(hits, h => h.Word.Word == "vael");
    }

    [Fact]
    public async Task LookupDoesNotCareAboutCase()
    {
        var (sut, _) = await WithLanguageAsync(("Vael", "River"));

        Assert.NotEmpty(sut.Lookup("vAeL"));
        Assert.NotEmpty(sut.Lookup("RIVER"));
    }

    [Fact]
    public async Task AnExactWordComesFirst()
    {
        // Somebody typing a coined word in full wants that word, not the six
        // entries whose meanings mention it.
        var (sut, languageId) = await WithLanguageAsync(("vael", "river"));
        await sut.SaveWordAsync(languageId,
            new ConlangWord { Word = "aval", Meaning = "the vael in flood" });

        var hits = sut.Lookup("vael");

        Assert.Equal(2, hits.Count);
        Assert.Equal("vael", hits[0].Word.Word);
    }

    [Fact]
    public async Task ALookupCanBeHeldToOneLanguage()
    {
        var (sut, first) = await WithLanguageAsync(("vael", "river"));
        var second = await sut.CreateAsync("Drenn");
        await sut.SaveWordAsync(second!.Id, new ConlangWord { Word = "vael", Meaning = "hill" });

        Assert.Equal(2, sut.Lookup("vael").Count);
        var held = Assert.Single(sut.Lookup("vael", first));
        Assert.Equal("river", held.Word.Meaning);
    }

    [Fact]
    public async Task AnEmptyLookupFindsNothingRatherThanEverything()
    {
        var (sut, _) = await WithLanguageAsync(("vael", "river"));

        Assert.Empty(sut.Lookup("   "));
        Assert.Empty(sut.Lookup(""));
    }
}
