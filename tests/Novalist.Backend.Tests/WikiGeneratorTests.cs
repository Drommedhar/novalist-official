using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers the AI article-generator seam through the real sample extension:
/// availability, generate + cache, the article's cached summary, and the error
/// path. The deterministic sample generator stands in for a model.
/// </summary>
[Collection("Avalonia")]
public sealed class WikiGeneratorTests
{
    private const string SampleId = "com.novalist.writingtoolkit";

    private static void Deploy(string extRoot)
    {
        var dir = Path.Combine(extRoot, "Sample");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(dir, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(dir, "extension.json"),
            $$"""{ "id": "{{SampleId}}", "name": "Sample", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
    }

    private static Workspace LoadHostWithProject(string root, TempDir ext)
    {
        var ws = new Workspace(Path.Combine(root, "settings"));
        ws.Projects.CreateProjectAsync(root, "N", "B").GetAwaiter().GetResult();
        ws.OpenProjectAsync(ws.Projects.ProjectRoot!).GetAwaiter().GetResult();
        ws.ExtensionsLoaderOverride = new ExtensionLoader(ext.Path);
        ws.ExtensionsHost.LoadAllAsync().GetAwaiter().GetResult();
        return ws;
    }

    [Fact]
    public async Task Regenerate_GeneratesCaches_AndArticleShowsFreshSummary()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path);
        var ws = LoadHostWithProject(root.Path, ext);
        try
        {
            var entities = new EntityService(ws.Projects);
            await entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
            // A custom type + entity so the regenerate path also exercises custom loading.
            await entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition { TypeKey = "spell", DisplayName = "Spell" });
            await entities.SaveCustomEntityAsync(new CustomEntityData { Id = "fb", EntityTypeKey = "spell", Name = "Fireball" });
            var rpc = new WikiRpc(ws);

            Assert.True(rpc.GeneratorAvailable());

            var result = await rpc.RegenerateAsync("character", "hero", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Null(result!.Error);
            Assert.Contains("Aldric", result.Summary!);
            Assert.NotNull(result.GeneratedAt);

            var article = await rpc.ArticleAsync("character", "hero");
            Assert.True(article.GeneratorAvailable);
            Assert.NotNull(article.Generated);
            Assert.Equal(result.Summary, article.Generated!.Summary);
            Assert.False(article.Generated.Stale);   // hash matches the just-generated dossier

            // Custom entity regeneration works too (covers the custom-load path).
            var customResult = await rpc.RegenerateAsync("spell", "fb", CancellationToken.None);
            Assert.Contains("Fireball", customResult!.Summary!);
        }
        finally { ws.Dispose(); }
    }

    [Fact]
    public async Task Regenerate_ErrorPath_ReturnsError_AndCachesNothing()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path);
        var ws = LoadHostWithProject(root.Path, ext);
        try
        {
            await new EntityService(ws.Projects).SaveCharacterAsync(new CharacterData { Id = "bad", Name = "GenFail" });
            var rpc = new WikiRpc(ws);

            var result = await rpc.RegenerateAsync("character", "bad", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Null(result!.Summary);
            Assert.Equal("no model configured", result.Error);

            // Nothing cached, so the article carries no generated summary.
            var article = await rpc.ArticleAsync("character", "bad");
            Assert.Null(article.Generated);
        }
        finally { ws.Dispose(); }
    }
}
