using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers the AI entity-extraction seam through the real sample extension:
/// availability, the proposal round-trip, the host-side filtering of names the
/// Codex already knows, and the error path. The deterministic sample extractor
/// stands in for a model. Nothing here writes entities — extraction proposes,
/// the writer accepts.
/// </summary>
[Collection("Avalonia")]
public sealed class EntityExtractionTests
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

    private static async Task<(string ChapterGuid, string SceneId)> AddSceneAsync(
        Workspace ws, string text)
    {
        var chapter = await ws.Projects.CreateChapterAsync("C");
        var scene = await ws.Projects.CreateSceneAsync(chapter.Guid, "S");
        await ws.WriteSceneAsync(chapter.Guid, scene.Id, $"<p>{text}</p>", text);
        return (chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task Extract_ProposesUnknownNames_AndSkipsOnesTheCodexKnows()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path);
        var ws = LoadHostWithProject(root.Path, ext);
        try
        {
            // One of every kind, so the "names the Codex already knows" sweep covers
            // characters, locations, items, lore, and custom entities — including
            // their aliases.
            var entities = new EntityService(ws.Projects);
            await entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
            await entities.SaveLocationAsync(new LocationData { Id = "port", Name = "Harbour" });
            await entities.SaveItemAsync(new ItemData
            {
                Id = "blade", Name = "Frostbite", Aliases = { "Icefang" }
            });
            await entities.SaveLoreAsync(new LoreData { Id = "oath", Name = "Oathbinding" });
            await entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
            {
                TypeKey = "faction", DisplayName = "Faction"
            });
            await entities.SaveCustomEntityAsync(new CustomEntityData
            {
                Id = "order", EntityTypeKey = "faction", Name = "Greyguard", Aliases = { "Greys" }
            });

            var (chapterGuid, sceneId) = await AddSceneAsync(
                ws, "Aldric met Mordre near Harbour, carrying Frostbite for the Greyguard. Icefang. Oathbinding. Greys.");
            var rpc = new EntitiesRpc(ws);

            Assert.True(rpc.ExtractorAvailable());

            var result = await rpc.ExtractFromSceneAsync(chapterGuid, sceneId, CancellationToken.None);

            Assert.Null(result.Error);
            // Known names are filtered out by the host; the new one survives.
            Assert.Contains(result.Proposals, p => p.Name == "Mordre" && p.TypeKey == "character");
            foreach (var known in new[]
                     { "Aldric", "Harbour", "Frostbite", "Icefang", "Oathbinding", "Greyguard", "Greys" })
                Assert.DoesNotContain(result.Proposals, p => p.Name == known);
        }
        finally
        {
            ws.Dispose();
        }
    }

    [Fact]
    public async Task Extract_ExtractorError_IsReported()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path);
        var ws = LoadHostWithProject(root.Path, ext);
        try
        {
            var (chapterGuid, sceneId) = await AddSceneAsync(ws, "ExtractFail happens here.");
            var rpc = new EntitiesRpc(ws);

            var result = await rpc.ExtractFromSceneAsync(chapterGuid, sceneId, CancellationToken.None);
            Assert.Empty(result.Proposals);
            Assert.Equal("no model configured", result.Error);
        }
        finally
        {
            ws.Dispose();
        }
    }

    [Fact]
    public async Task Extract_EmptyScene_ReturnsNothingWithoutCallingTheExtractor()
    {
        using var root = new TempDir();
        using var ext = new TempDir();
        Deploy(ext.Path);
        var ws = LoadHostWithProject(root.Path, ext);
        try
        {
            var (chapterGuid, sceneId) = await AddSceneAsync(ws, "   ");
            var rpc = new EntitiesRpc(ws);

            var result = await rpc.ExtractFromSceneAsync(chapterGuid, sceneId, CancellationToken.None);
            Assert.Empty(result.Proposals);
            Assert.Null(result.Error);
        }
        finally
        {
            ws.Dispose();
        }
    }

    [Fact]
    public async Task Extract_NoExtensionsLoaded_IsUnavailableAndYieldsNothing()
    {
        using var root = new TempDir();
        var ws = new Workspace(Path.Combine(root.Path, "settings"));
        await ws.Projects.CreateProjectAsync(root.Path, "N", "B");
        await ws.OpenProjectAsync(ws.Projects.ProjectRoot!);
        try
        {
            var (chapterGuid, sceneId) = await AddSceneAsync(ws, "Anything at all.");
            var rpc = new EntitiesRpc(ws);

            Assert.False(rpc.ExtractorAvailable());
            var result = await rpc.ExtractFromSceneAsync(chapterGuid, sceneId, CancellationToken.None);
            Assert.Empty(result.Proposals);
            Assert.Null(result.Error);
        }
        finally
        {
            ws.Dispose();
        }
    }
}
