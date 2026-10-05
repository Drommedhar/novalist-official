using System.Text.Json;
using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class WorkspaceCoordinationSafetyTests
{
    [Theory]
    [InlineData("snapshots/restore")]
    [InlineData("search/replaceAll")]
    public void OperationsReplacingOpenProseRequireWindowCoordination(string method)
        => Assert.True(WorkspaceCoordinator.ChangesWorkspace(method));

    [Fact]
    public async Task SdkSnapshotRestorePreparesWindowsAndPublishesRestoredContent()
    {
        using var directory = new TempDir();
        using var workspace = new Workspace(directory.Path);
        await workspace.Projects.CreateProjectAsync(directory.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        using var host = new HostServices(workspace.FileService, workspace.Projects,
            new Novalist.Core.Services.EntityService(workspace.Projects), workspace.Settings);
        await host.ProjectService.WriteSceneContentAsync(chapter.Guid, scene.Id, "<p>Original</p>");
        var snapshot = await host.ArchiveService.TakeSnapshotAsync(chapter.Guid, scene.Id, "Before");
        await host.ProjectService.WriteSceneContentAsync(chapter.Guid, scene.Id, "<p>Later</p>");
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        coordinator.Validate(new("1", 0, null), "project/getState");
        host.CoordinateWorkspace = coordinator.RunAsync;
        var notifications = new List<string>();
        coordinator.Notify = async (method, payload) =>
        {
            notifications.Add(method);
            var json = JsonSerializer.SerializeToElement(payload);
            var token = json.GetProperty("token").GetString()!;
            var text = await host.ProjectService.ReadSceneContentAsync(chapter.Guid, scene.Id);
            if (method == "workspace/prepare")
            {
                Assert.Equal("snapshots/restore", json.GetProperty("reason").GetString());
                Assert.Contains("Later", text);
                coordinator.Prepared(token, true, null);
            }
            else
            {
                Assert.Contains("Original", text);
                coordinator.Applied(token);
            }
        };

        Assert.True(await host.ArchiveService.RestoreSnapshotAsync(chapter.Guid, scene.Id, snapshot!));

        Assert.Equal(["workspace/prepare", "workspace/changed"], notifications);
        Assert.Equal(1, coordinator.Epoch);
        Assert.Contains("Original", await host.ProjectService.ReadSceneContentAsync(chapter.Guid, scene.Id));
    }

    [Theory]
    [InlineData("workspace/prepare")]
    [InlineData("workspace/changed")]
    public async Task InheritedAsyncContextCannotRunSdkMutationWhileWindowsAreSynchronizing(string phase)
    {
        using var directory = new TempDir();
        using var workspace = new Workspace(directory.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        coordinator.Validate(new("1", 0, null), "project/getState");
        var nestedRan = false;
        var mutationRan = false;
        coordinator.Notify = async (method, payload) =>
        {
            var token = JsonSerializer.SerializeToElement(payload).GetProperty("token").GetString()!;
            if (method == phase)
                await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
                    coordinator.RunAsync("extension/switch", () =>
                    {
                        nestedRan = true;
                        return Task.CompletedTask;
                    })));
            if (method == "workspace/prepare") coordinator.Prepared(token, true, null);
            if (method == "workspace/changed") coordinator.Applied(token);
        };

        await coordinator.RunAsync("project/switchBook", () =>
        {
            mutationRan = true;
            Assert.Equal(0, gate.CurrentCount);
            return Task.CompletedTask;
        });

        Assert.True(mutationRan);
        Assert.False(nestedRan);
        Assert.Equal(1, coordinator.Epoch);
        Assert.Equal(1, gate.CurrentCount);
        Assert.Null(WorkspaceRequestContext.Lease);
    }

    [Fact]
    public async Task SdkTransactionPublishesItsLeaseForSequentialNestedChanges()
    {
        using var directory = new TempDir();
        using var workspace = new Workspace(directory.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        coordinator.Validate(new("1", 0, null), "project/getState");
        var notifications = 0;
        coordinator.Notify = (method, payload) =>
        {
            notifications++;
            var token = JsonSerializer.SerializeToElement(payload).GetProperty("token").GetString()!;
            if (method == "workspace/prepare") coordinator.Prepared(token, true, null);
            if (method == "workspace/changed") coordinator.Applied(token);
            return Task.CompletedTask;
        };
        WorkspaceGateLease? observed = null;

        var value = await coordinator.RunAsync("extension/import", async () =>
        {
            observed = WorkspaceRequestContext.Lease;
            Assert.NotNull(observed);
            Assert.True(observed.Held);
            return await coordinator.RunAsync("project/switchDraft", () => Task.FromResult(42));
        });

        Assert.Equal(42, value);
        Assert.Equal(2, notifications);
        Assert.False(observed!.Active);
        Assert.False(observed.Held);
        Assert.Null(WorkspaceRequestContext.Lease);
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task SdkCallWithStaleRequestEpochCannotRunAgainstTheNewWorkspace()
    {
        using var directory = new TempDir();
        using var workspace = new Workspace(directory.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        coordinator.Validate(new("1", 0, null), "project/getState");
        var notifications = 0;
        coordinator.Notify = (method, payload) =>
        {
            notifications++;
            var token = JsonSerializer.SerializeToElement(payload).GetProperty("token").GetString()!;
            if (method == "workspace/prepare") coordinator.Prepared(token, true, null);
            if (method == "workspace/changed") coordinator.Applied(token);
            return Task.CompletedTask;
        };
        await coordinator.RunAsync("project/open", () => Task.CompletedTask);
        var staleRan = false;

        using (WorkspaceRequestContext.Enter(new("1", 0, null), null))
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync("extension/archive", () =>
            {
                staleRan = true;
                return Task.CompletedTask;
            }));

        Assert.False(staleRan);
        Assert.Equal(2, notifications);
        Assert.Equal(1, coordinator.Epoch);
        Assert.Equal(1, gate.CurrentCount);
    }
}
