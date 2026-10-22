using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class RetainedProjectCopyTests
{
    [Fact]
    public async Task AnIndependentCopyKeepsAllWritingWithANewIdentityAndLeavesTheOriginalOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        using var workspace = new Workspace(Path.Combine(directory.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(directory.Path, "Original", "Book");
        var originalId = workspace.Projects.CurrentProject!.Id;
        var originalRoot = workspace.Projects.ProjectRoot!;
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        await workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Retained writing.</p>");
        var destination = Path.Combine(directory.Path, "Independent");

        await new CollaborationRpc(workspace).CreateIndependentCopyAsync(destination, ct);

        var copy = new ProjectService(new FileService());
        var project = await copy.LoadProjectAsync(destination);
        Assert.NotEqual(originalId, project.Id);
        Assert.Equal("Original", project.Name);
        Assert.Equal(originalRoot, workspace.Projects.ProjectRoot);
        Assert.Contains("Retained writing.", await copy.ReadSceneContentAsync(chapter, scene));
        Assert.Null(new CollaborationRpc(workspace).Status());
        await Assert.ThrowsAsync<IOException>(() => new CollaborationRpc(workspace).CreateIndependentCopyAsync(destination, ct));
    }
}
