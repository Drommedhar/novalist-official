using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>How a project arranges each entry type's sheet.</summary>
public sealed class EntitySheetRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitySheetRpc _rpc;

    public EntitySheetRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-sheet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SheetNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new EntitySheetRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ATypeNobodyArrangedReportsTheDefault()
    {
        var sheet = _rpc.Get("character");

        Assert.Empty(sheet.Hidden);
        Assert.Empty(sheet.Order);
    }

    [Fact]
    public async Task AnArrangementSurvivesAReadBack()
    {
        await _rpc.SaveAsync("character", ["eyeColor", "hairColor"], ["name", "role", "age"]);

        var sheet = _rpc.Get("character");

        Assert.Equal(["eyeColor", "hairColor"], sheet.Hidden);
        Assert.Equal(["name", "role", "age"], sheet.Order);
    }

    [Fact]
    public async Task SavingTwiceReplacesRatherThanAccumulating()
    {
        await _rpc.SaveAsync("character", ["eyeColor"], ["name"]);

        var sheet = await _rpc.SaveAsync("character", ["age"], ["name", "age"]);

        Assert.Equal(["age"], sheet.Hidden);
        Assert.Single(_workspace.Projects.CurrentProject!.EntitySheets);
    }

    [Fact]
    public async Task BlanksAndRepeatsAreDropped()
    {
        var sheet = await _rpc.SaveAsync(
            "location", ["type", "  ", "type"], ["name", "name", ""]);

        Assert.Equal(["type"], sheet.Hidden);
        Assert.Equal(["name"], sheet.Order);
    }

    [Fact]
    public async Task EachTypeIsArrangedSeparately()
    {
        await _rpc.SaveAsync("character", ["eyeColor"], []);
        await _rpc.SaveAsync("location", ["climate"], []);

        Assert.Equal(["eyeColor"], _rpc.Get("character").Hidden);
        Assert.Equal(["climate"], _rpc.Get("location").Hidden);
    }

    [Fact]
    public async Task NothingSavedIsAnEmptyArrangementRatherThanAThrow()
    {
        var sheet = await _rpc.SaveAsync("character", null, null);

        Assert.Empty(sheet.Hidden);
        Assert.Empty(sheet.Order);
    }

    [Fact]
    public void WithNoProjectOpenThereIsNoSheet()
    {
        var bare = new Workspace(Path.Combine(_root, "bare-settings"));

        Assert.Throws<InvalidOperationException>(() => new EntitySheetRpc(bare).Get("character"));
    }
}
