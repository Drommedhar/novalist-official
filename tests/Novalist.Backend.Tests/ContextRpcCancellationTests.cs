using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ContextRpcCancellationTests
{
    [Theory]
    [InlineData("before")]
    [InlineData("entities")]
    [InlineData("scenes")]
    public async Task AnalysisStopsQueuedReadsWhenForegroundWorkCancelsIt(string during)
    {
        using var temp = new TempDir();
        using var cancellation = new CancellationTokenSource();
        var files = new CancellingReads();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "First");
        await workspace.Projects.CreateSceneAsync(chapter.Guid, "Second");
        if (during == "entities")
        {
            var entities = new EntityService(workspace.Projects);
            await entities.SaveCharacterAsync(new CharacterData { Id = "one", Name = "One" });
            await entities.SaveCharacterAsync(new CharacterData { Id = "two", Name = "Two" });
        }
        files.AfterRead = path =>
        {
            if (during == "entities" || path.EndsWith(".novalist", StringComparison.Ordinal)) cancellation.Cancel();
        };
        if (during == "before") cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ContextRpc(workspace).AnalyzeAsync(chapter.Guid, scene.Id, cancellation.Token));

        Assert.Equal(during == "before" ? 0 : 1, files.Reads.Count);
        if (during == "entities") Assert.DoesNotContain(files.Reads, path => path.EndsWith(".novalist"));
    }

    private sealed class CancellingReads : FileService, IFileService
    {
        public Action<string>? AfterRead { get; set; }
        public List<string> Reads { get; } = [];

        async Task<string> IFileService.ReadTextAsync(string path)
        {
            var text = await ReadTextAsync(path);
            if (AfterRead != null)
            {
                Reads.Add(path);
                AfterRead(path);
            }
            return text;
        }
    }
}
