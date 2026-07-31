using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Invented languages and their dictionaries, over the wire.</summary>
public sealed class ConlangRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ConlangRpc _rpc;

    public ConlangRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LangNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ConlangRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task ALanguageAndItsWordsRoundTrip()
    {
        var languages = await _rpc.CreateAsync("Kelmari");
        var id = languages[0].Id;

        await _rpc.SaveWordAsync(id, null, "vael", "river", "noun", "VAY-el", "of the north");
        var after = await _rpc.UpdateAsync(id, "Kelmarin", "Spoken along the river.");

        Assert.Equal("Kelmarin", after[0].Name);
        Assert.Equal("Spoken along the river.", after[0].Description);
        var word = Assert.Single(after[0].Words);
        Assert.Equal("vael", word.Word);
        Assert.Equal("river", word.Meaning);
        Assert.Equal("VAY-el", word.Pronunciation);

        // And it is on disk, not only in the reply.
        Assert.Single(_rpc.List()[0].Words);
    }

    [Fact]
    public async Task AWordIsFoundByWhatItIsAndByWhatItMeans()
    {
        var languages = await _rpc.CreateAsync("Kelmari");
        var id = languages[0].Id;
        await _rpc.SaveWordAsync(id, null, "vael", "river");

        Assert.Single(_rpc.Lookup("vael"));
        // The half that makes it a dictionary rather than a glossary.
        Assert.Single(_rpc.Lookup("river"));
        Assert.Empty(_rpc.Lookup("stone"));
    }

    [Fact]
    public async Task ALookupReachesEveryLanguage()
    {
        // The one question the list in front of the writer cannot answer: did I
        // already coin this somewhere else?
        var first = (await _rpc.CreateAsync("Kelmari"))[0].Id;
        var second = (await _rpc.CreateAsync("Drenn"))[1].Id;
        await _rpc.SaveWordAsync(first, null, "vael", "river");
        await _rpc.SaveWordAsync(second, null, "vael", "hill");

        Assert.Equal(2, _rpc.Lookup("vael").Length);
        var held = Assert.Single(_rpc.Lookup("vael", second));
        Assert.Equal("hill", held.Word.Meaning);
        Assert.Equal("Drenn", held.LanguageName);
    }

    [Fact]
    public async Task AWordCanBeRewrittenAndRemoved()
    {
        var id = (await _rpc.CreateAsync("Kelmari"))[0].Id;
        await _rpc.SaveWordAsync(id, null, "vael", "river");
        var wordId = _rpc.List()[0].Words[0].Id;

        var edited = await _rpc.SaveWordAsync(id, wordId, "vael", "great river");
        Assert.Equal("great river", Assert.Single(edited[0].Words).Meaning);

        var removed = await _rpc.DeleteWordAsync(id, wordId);
        Assert.Empty(removed[0].Words);
    }

    [Fact]
    public async Task ALanguageGoesWithItsWords()
    {
        var id = (await _rpc.CreateAsync("Kelmari"))[0].Id;
        await _rpc.SaveWordAsync(id, null, "vael", "river");

        Assert.Empty(await _rpc.DeleteAsync(id));
        Assert.Empty(_rpc.Lookup("vael"));
    }

    [Fact]
    public async Task CallsAgainstSomethingThatIsNotThereChangeNothing()
    {
        var id = (await _rpc.CreateAsync("Kelmari"))[0].Id;

        Assert.Single(await _rpc.UpdateAsync("no-such-language", "X", null));
        Assert.Single(await _rpc.DeleteAsync("no-such-language"));
        Assert.Empty((await _rpc.SaveWordAsync("no-such-language", null, "x", "y"))[0].Words);
        Assert.Empty((await _rpc.DeleteWordAsync(id, "no-such-word"))[0].Words);
    }
}
