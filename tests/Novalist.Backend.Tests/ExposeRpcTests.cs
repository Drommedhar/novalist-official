using System.IO.Compression;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ExposeRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ExposeRpc _rpc;

    public ExposeRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-expose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ExposeNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ExposeRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Get_StartsEmpty()
    {
        var state = await _rpc.GetAsync();
        Assert.Equal(string.Empty, state.Html);
        Assert.Equal(0, state.Characters);
        Assert.Equal(0, state.CharLimit);
        Assert.Equal(0, state.PageLimit);
    }

    [Fact]
    public async Task Save_ThenGet_RoundTripsAndCounts()
    {
        var saved = await _rpc.SaveAsync("<p>Eine Heldin bricht auf.</p>");
        Assert.Equal(1, saved.Pages);

        var loaded = await _rpc.GetAsync();
        Assert.Equal("<p>Eine Heldin bricht auf.</p>", loaded.Html);
        Assert.Equal("Eine Heldin bricht auf.".Length, loaded.Characters);
    }

    [Fact]
    public async Task Measure_CountsWithoutSaving()
    {
        var measured = _rpc.Measure("<p>Nur gezaehlt.</p>");
        Assert.Equal("Nur gezaehlt.".Length, measured.Characters);
        Assert.Equal(string.Empty, (await _rpc.GetAsync()).Html);
    }

    [Fact]
    public async Task SetLimits_SurvivesReload()
    {
        await _rpc.SetLimitsAsync(15000, 10);
        await _workspace.Projects.LoadProjectAsync(_workspace.Projects.ProjectRoot!);

        var state = await _rpc.GetAsync();
        Assert.Equal(15000, state.CharLimit);
        Assert.Equal(10, state.PageLimit);
    }

    [Fact]
    public async Task Export_EmptyExposeReportsFailure()
    {
        var output = Path.Combine(_root, "leer.docx");
        var result = await _rpc.ExportAsync(output, "ExposeNovel");
        Assert.False(result.Success);
        Assert.Equal(0, result.SizeBytes);
    }

    [Fact]
    public async Task Export_WritesANormseitenDocx()
    {
        await _rpc.SaveAsync("<p>Eine Heldin bricht auf.</p>");
        var output = Path.Combine(_root, "expose.docx");

        var result = await _rpc.ExportAsync(output, "ExposeNovel");

        Assert.True(result.Success);
        Assert.True(result.SizeBytes > 0);
        Assert.Equal(output, result.OutputPath);

        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("word/header1.xml"));
    }
}
