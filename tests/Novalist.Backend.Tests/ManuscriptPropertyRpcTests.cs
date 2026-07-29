using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Scene and chapter fields from the RPC surface Settings, the scene dock and
/// the outliner call.
/// </summary>
public sealed class ManuscriptPropertyRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ManuscriptPropertyRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public ManuscriptPropertyRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-props-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PropNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult().Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _rpc = new ManuscriptPropertyRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task DefinitionsRoundTripThroughTheDto()
    {
        Assert.Empty(_rpc.Definitions());

        var saved = await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("tension", "Tension", "Int", [], "Scene", true),
            new ManuscriptPropertyDto("mood", "Mood", "Enum", ["Calm", "Tense"], "Chapter", false)
        ]);

        Assert.Equal(2, saved.Length);
        Assert.Equal("Int", saved[0].Type);
        Assert.True(saved[0].ShowInOutliner);
        Assert.Equal("Chapter", saved[1].Scope);
        Assert.Equal(["Calm", "Tense"], saved[1].EnumOptions);
        Assert.Equal(2, _rpc.Definitions().Length);
    }

    [Fact]
    public async Task AnUnknownTypeOrScopeFallsBackRatherThanThrowing()
    {
        // The renderer is not the only caller - an extension or an older
        // client sending nonsense should get a text scene field, not a crash.
        var saved = await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("k", "K", "Rhubarb", [], "Elsewhere", false)
        ]);

        Assert.Equal("String", Assert.Single(saved).Type);
        Assert.Equal("Scene", saved[0].Scope);
    }

    [Fact]
    public async Task SceneValuesSetReadAndClear()
    {
        await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("tension", "Tension", "Int", [], "Scene", true)
        ]);

        Assert.Equal("8", (await _rpc.SetSceneValueAsync(_scene, "tension", "8"))["tension"]);
        Assert.Equal("8", _rpc.AllSceneValues()[_scene]["tension"]);
        Assert.Empty(await _rpc.SetSceneValueAsync(_scene, "tension", null));
        Assert.DoesNotContain(_scene, _rpc.AllSceneValues().Keys);
    }

    [Fact]
    public async Task ChapterValuesSetAndRead()
    {
        await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("pov", "POV", "String", [], "Chapter", false)
        ]);

        Assert.Equal("Mira", (await _rpc.SetChapterValueAsync(_chapter, "pov", "Mira"))["pov"]);
        Assert.Equal("Mira", _rpc.ChapterValues(_chapter)["pov"]);
    }

    [Fact]
    public async Task AllSceneValuesCarriesTheWholeBookInOneCall()
    {
        var second = _workspace.Projects.CreateSceneAsync(_chapter, "B").GetAwaiter().GetResult();
        await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("mood", "Mood", "String", [], "Scene", true)
        ]);
        await _rpc.SetSceneValueAsync(_scene, "mood", "grim");

        var all = _rpc.AllSceneValues();

        // Only scenes that have values, so the outliner is not sent a row of
        // empty dictionaries for every scene in a long book.
        Assert.Equal("grim", all[_scene]["mood"]);
        Assert.DoesNotContain(second.Id, all.Keys);
    }

    [Fact]
    public void AllSceneValues_WithNoProjectOpen_IsEmpty()
    {
        var bare = new Workspace(Path.Combine(_root, "settings2"));
        Assert.Empty(new ManuscriptPropertyRpc(bare).AllSceneValues());
    }

    [Fact]
    public async Task SetDefinitions_WithNull_ClearsTheList()
    {
        await _rpc.SetDefinitionsAsync([
            new ManuscriptPropertyDto("mood", "Mood", "String", [], "Scene", false)
        ]);

        Assert.Empty(await _rpc.SetDefinitionsAsync(null!));
    }
}
