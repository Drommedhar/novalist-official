using Nerdbank.Streams;
using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class BackupCancellationTests
{
    [Fact]
    public async Task CancellingCloudBackupReleasesQueuedSceneRead()
    {
        using var temp = new TempDir();
        var archives = Substitute.For<ICancellableArchiveService>();
        using var workspace = new Workspace(Path.Combine(temp.Path, "settings"), archiveService: archives);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        await workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Preserved text</p>");
        workspace.Settings.Settings.BackupFolder = Path.Combine(temp.Path, "backups");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        archives.CreateFromDirectoryAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(3));
                return 0;
            });
        var streams = FullDuplexStream.CreatePair();
        using var server = new SerialDispatchJsonRpc(new HeaderDelimitedMessageHandler(streams.Item1, streams.Item1));
        server.AddLocalRpcTarget(new BackupRpc(workspace));
        server.AddLocalRpcTarget(new ScenesRpc(workspace));
        server.StartListening();
        using var client = new JsonRpc(new HeaderDelimitedMessageHandler(streams.Item2, streams.Item2));
        client.StartListening();
        using var cancellation = new CancellationTokenSource();
        var backup = client.InvokeWithCancellationAsync<BackupDto?>("backup/create", ["open"], cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reading = client.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id);
        Assert.False(reading.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backup);
        var content = await reading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("<p>Preserved text</p>", content.Html);
        Assert.Empty(Directory.GetFiles(workspace.Settings.Settings.BackupFolder, "*.zip", SearchOption.AllDirectories));
    }
}
