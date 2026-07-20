using System.Net;
using System.Text;
using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers merging extension grammar contributors into <see cref="GrammarRpc"/>
/// and registering extension-contributed entity types into the loaded project.
/// </summary>
[Collection("Avalonia")]
public sealed class ExtensionGrammarAndEntityTests
{
    private const string SampleId = "com.novalist.writingtoolkit";
    private const string ThrowingId = "test.throwing";

    private sealed class EmptyMatchesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"matches": []}""", Encoding.UTF8, "application/json")
            });
    }

    private static void Deploy(string extRoot, string folder, string dllName, string id)
    {
        var dir = Path.Combine(extRoot, folder);
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(AppContext.BaseDirectory, dllName);
        File.Copy(dll, Path.Combine(dir, dllName));
        File.WriteAllText(Path.Combine(dir, "extension.json"),
            $$"""{ "id": "{{id}}", "name": "{{folder}}", "entryAssembly": "{{dllName}}" }""");
    }

    private static Workspace LoadHost(string settingsRoot, TempDir extDir)
    {
        var ws = new Workspace(Path.Combine(settingsRoot, "settings"));
        ws.Settings.LoadAsync().GetAwaiter().GetResult();
        ws.ExtensionsLoaderOverride = new ExtensionLoader(extDir.Path);
        ws.ExtensionsHost.LoadAllAsync().GetAwaiter().GetResult();
        return ws;
    }

    // ── Grammar merge ───────────────────────────────────────────────

    [Fact]
    public async Task Grammar_MergesSampleStyleContributorIssues()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Sample", "Novalist.Sdk.Example.dll", SampleId);
        var ws = LoadHost(root.Path, ext);
        try
        {
            ws.Settings.Settings.GrammarCheckEnabled = true;
            var rpc = new GrammarRpc(ws, new HttpClient(new EmptyMatchesHandler()));

            var issues = await rpc.CheckAsync("this text is very unique indeed", CancellationToken.None);

            var issue = Assert.Single(issues);
            Assert.Equal("style", issue.Type);
        }
        finally { ws.Dispose(); }
    }

    [Fact]
    public async Task Grammar_ContributorThrows_IsSwallowed()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Throwing", "Novalist.TestExtension.dll", ThrowingId);
        Environment.SetEnvironmentVariable("NOVALIST_TEST_GRAMMAR", "throw");
        var ws = LoadHost(root.Path, ext);
        try
        {
            ws.Settings.Settings.GrammarCheckEnabled = true;
            var rpc = new GrammarRpc(ws, new HttpClient(new EmptyMatchesHandler()));

            var issues = await rpc.CheckAsync("anything", CancellationToken.None);

            Assert.Empty(issues); // core empty + contributor fault swallowed
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_TEST_GRAMMAR", null);
            ws.Dispose();
        }
    }

    [Fact]
    public async Task Grammar_ContributorCancels_Rethrows()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Throwing", "Novalist.TestExtension.dll", ThrowingId);
        Environment.SetEnvironmentVariable("NOVALIST_TEST_GRAMMAR", "cancel");
        var ws = LoadHost(root.Path, ext);
        try
        {
            ws.Settings.Settings.GrammarCheckEnabled = true;
            var rpc = new GrammarRpc(ws, new HttpClient(new EmptyMatchesHandler()));

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => rpc.CheckAsync("anything", CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_TEST_GRAMMAR", null);
            ws.Dispose();
        }
    }

    [Fact]
    public async Task Grammar_NoEnabledContributors_ReturnsCoreOnly()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Throwing", "Novalist.TestExtension.dll", ThrowingId);
        // env unset -> IsGrammarCheckEnabled false -> contributor filtered out
        var ws = LoadHost(root.Path, ext);
        try
        {
            ws.Settings.Settings.GrammarCheckEnabled = true;
            var rpc = new GrammarRpc(ws, new HttpClient(new EmptyMatchesHandler()));

            var issues = await rpc.CheckAsync("anything", CancellationToken.None);

            Assert.Empty(issues);
        }
        finally { ws.Dispose(); }
    }

    // ── Entity type registration ────────────────────────────────────

    [Fact]
    public async Task RegisterExtensionEntityTypes_MergesFactionIntoProject_Idempotent()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Sample", "Novalist.Sdk.Example.dll", SampleId);

        var ws = new Workspace(Path.Combine(root.Path, "settings"));
        try
        {
            await ws.Projects.CreateProjectAsync(root.Path, "N", "B");
            await ws.OpenProjectAsync(ws.Projects.ProjectRoot!);
            ws.ExtensionsLoaderOverride = new ExtensionLoader(ext.Path);
            await ws.ExtensionsHost.LoadAllAsync();

            await ws.RegisterExtensionEntityTypesAsync();
            await ws.RegisterExtensionEntityTypesAsync(); // idempotent

            var types = new EntityService(ws.Projects).GetCustomEntityTypes();
            var faction = Assert.Single(types, t => t.TypeKey == "ext.writingtoolkit.faction");
            Assert.Equal("extension", faction.Source);
            Assert.NotEmpty(faction.DefaultFields);
        }
        finally { ws.Dispose(); }
    }

    [Fact]
    public async Task RegisterExtensionEntityTypes_NoProject_NoOp()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Sample", "Novalist.Sdk.Example.dll", SampleId);
        var ws = LoadHost(root.Path, ext); // host loaded, no project open
        try
        {
            await ws.RegisterExtensionEntityTypesAsync(); // CurrentProject null -> returns
        }
        finally { ws.Dispose(); }
    }

    [Fact]
    public async Task ExtensionsLoad_WithProjectOpen_RegistersEntityTypes()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path, "Sample", "Novalist.Sdk.Example.dll", SampleId);

        var ws = new Workspace(Path.Combine(root.Path, "settings"));
        try
        {
            await ws.Projects.CreateProjectAsync(root.Path, "N", "B");
            await ws.OpenProjectAsync(ws.Projects.ProjectRoot!);
            ws.ExtensionsLoaderOverride = new ExtensionLoader(ext.Path);

            await new ExtensionsRpc(ws).LoadAsync();

            var types = new EntityService(ws.Projects).GetCustomEntityTypes();
            Assert.Contains(types, t => t.TypeKey == "ext.writingtoolkit.faction");
        }
        finally { ws.Dispose(); }
    }
}
