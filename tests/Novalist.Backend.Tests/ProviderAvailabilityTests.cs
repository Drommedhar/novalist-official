using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class ProviderAvailabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArticleProviderDisabledWhileLoadingContextReturnsUnavailable(bool section)
    {
        using var root = new TempDir();
        using var workspace = new Workspace(Path.Combine(root.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(root.Path, "Project", "Book");
        await new EntityService(workspace.Projects).SaveCharacterAsync(new CharacterData
        {
            Id = "hero", Name = "Hero"
        });
        var provider = new DisablingProvider();
        workspace.ExtensionsHost.ArticleGenerators.Add(provider);
        var rpc = new WikiRpc(workspace);

        var result = section
            ? await rpc.GenerateSectionAsync("character", "hero", "History", "", CancellationToken.None)
            : await rpc.RegenerateAsync("character", "hero", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(2, provider.AvailabilityChecks);
        Assert.Equal(0, provider.Invocations);
    }

    [Fact]
    public async Task ExtractorDisabledWhileLoadingContextReturnsNoProposals()
    {
        using var root = new TempDir();
        using var workspace = new Workspace(Path.Combine(root.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(root.Path, "Project", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>A visitor arrived.</p>", "A visitor arrived.");
        var provider = new DisablingProvider();
        workspace.ExtensionsHost.EntityExtractors.Add(provider);

        var result = await new EntitiesRpc(workspace).ExtractFromSceneAsync(
            chapter.Guid, scene.Id, CancellationToken.None);

        Assert.Empty(result.Proposals);
        Assert.Null(result.Error);
        Assert.Equal(2, provider.AvailabilityChecks);
        Assert.Equal(0, provider.Invocations);
    }

    private sealed class DisablingProvider : IArticleGeneratorContributor, IEntityExtractionContributor
    {
        public int AvailabilityChecks { get; private set; }
        public int Invocations { get; private set; }
        public string ArticleGeneratorName => "availability test";
        public string EntityExtractorName => "availability test";
        public bool IsArticleGeneratorEnabled => ++AvailabilityChecks == 1;
        public bool IsEntityExtractorEnabled => ++AvailabilityChecks == 1;

        public Task<ArticleGenerationResult> GenerateAsync(
            ArticleGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult(new ArticleGenerationResult { Summary = "Unexpected generation" });
        }

        public Task<EntityExtractionResult> ExtractAsync(
            EntityExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult(new EntityExtractionResult());
        }
    }
}
