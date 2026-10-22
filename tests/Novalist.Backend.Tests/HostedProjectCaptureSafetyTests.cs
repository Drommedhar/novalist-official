using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectCaptureSafetyTests
{
    [Theory]
    [InlineData("missingIdentity", "supported project identity")]
    [InlineData("rendererDeletion", "initial shared scene cannot be deleted")]
    [InlineData("concurrentEdit", "scene changed while sharing was starting")]
    public async Task AnInvalidOrChangingInitialSceneCannotStartAnIncompleteSharedProject(string fault, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await desktop.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Original writing.</p>", "Original writing.");
        var path = Assert.Single(Directory.GetFiles(project.ProjectPath ?? throw new InvalidOperationException("Missing project."), "*.novalist", SearchOption.AllDirectories));
        var original = await File.ReadAllTextAsync(path, ct);
        if (fault == "missingIdentity") await File.WriteAllTextAsync(path, "<p>Writing without its marker.</p>", ct);
        if (fault == "rendererDeletion") desktop.Documents.RemoveSeededScenes = true;
        if (fault == "concurrentEdit") server.ProjectCreated = () => File.WriteAllText(path, FileFrontMatter.Stamp("<p>Changed during connection.</p>", scene.Id));
        await desktop.SignInAsync(server.Address);

        var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address));

        Assert.Contains(expected, error.Message);
        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        var retained = await File.ReadAllTextAsync(path, ct);
        Assert.Contains(fault == "concurrentEdit" ? "Changed during connection." : fault == "missingIdentity" ? "Writing without its marker." : "Original writing.", retained);
        desktop.Documents.RemoveSeededScenes = false;
        server.ProjectCreated = null;
        if (fault == "missingIdentity") await File.WriteAllTextAsync(path, original, ct);
        Assert.Equal("Connected", (await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address)).Connection);
    }
}
