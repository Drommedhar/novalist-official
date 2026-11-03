using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ContextRpcCancellationTests
{
    [Theory]
    [InlineData("before", null)]
    [InlineData("entities", null)]
    [InlineData("scenes", null)]
    [InlineData("before", "<p>Unsaved scene</p>")]
    [InlineData("entities", "<p>Unsaved scene</p>")]
    [InlineData("scenes", "<p>Unsaved scene</p>")]
    public async Task AnalysisStopsQueuedReadsWhenForegroundWorkCancelsIt(string during, string? currentSceneHtml)
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
            new ContextRpc(workspace).AnalyzeAsync(chapter.Guid, scene.Id, currentSceneHtml, cancellationToken: cancellation.Token));

        Assert.Equal(during == "before" ? 0 : 1, files.Reads.Count);
        if (during == "entities") Assert.DoesNotContain(files.Reads, path => path.EndsWith(".novalist"));
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("<p>Mira arrived.</p>", 2)]
    [InlineData("", 0)]
    public async Task LiveHtmlSubstitutionPreservesBatchedChapterReads(string? currentSceneHtml, int wordCount)
    {
        using var temp = new TempDir();
        var files = new BatchReads();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var first = await workspace.Projects.CreateChapterAsync("First");
        var scene = await workspace.Projects.CreateSceneAsync(first.Guid, "Current");
        var sibling = await workspace.Projects.CreateSceneAsync(first.Guid, "Sibling");
        var second = await workspace.Projects.CreateChapterAsync("Second");
        var other = await workspace.Projects.CreateSceneAsync(second.Guid, "Other");
        await workspace.Projects.WriteSceneContentAsync(first, scene, "Old prose was written.");
        await workspace.Projects.WriteSceneContentAsync(first, sibling, "Mira stayed.");
        await workspace.Projects.WriteSceneContentAsync(second, other, "Mira left.");
        await new EntityService(workspace.Projects).SaveCharacterAsync(new CharacterData { Id = "mira", Name = "Mira" });
        files.Batches.Clear();
        files.DirectSceneReads = 0;
        using var cancellation = new CancellationTokenSource();

        var context = await new ContextRpc(workspace).AnalyzeAsync(first.Guid, scene.Id,
            currentSceneHtml, cancellationToken: cancellation.Token);

        Assert.Equal(wordCount, context.Analysis.WordCount);
        Assert.Equal(2, files.Batches.Count);
        Assert.Equal(2, files.Batches[0].Length);
        Assert.Single(files.Batches[1]);
        Assert.Equal(0, files.DirectSceneReads);
        Assert.Equal(cancellation.Token, files.LastToken);
        if (currentSceneHtml == "<p>Mira arrived.</p>")
        {
            Assert.Equal("mira", Assert.Single(context.Characters).Id);
            Assert.All(Assert.Single(context.MentionRows).Cells, cell => Assert.True(cell.Present));
        }
        else Assert.Empty(context.Characters);
    }

    [Fact]
    public async Task LiveHtmlAnalysisStopsBeforeTheNextChapterWhenABatchIsCancelled()
    {
        using var temp = new TempDir();
        var files = new BatchReads();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("First");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        var other = await workspace.Projects.CreateChapterAsync("Second");
        await workspace.Projects.CreateSceneAsync(other.Guid, "Other scene");
        files.Batches.Clear();
        using var cancellation = new CancellationTokenSource();
        files.AfterBatch = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ContextRpc(workspace).AnalyzeAsync(
            chapter.Guid, scene.Id, "<p>Live draft</p>", cancellationToken: cancellation.Token));

        Assert.Single(files.Batches);
        Assert.Equal(cancellation.Token, files.LastToken);
    }

    private sealed class BatchReads : FileService, IFileService, IBatchTextReader
    {
        public List<string[]> Batches { get; } = [];
        public int DirectSceneReads { get; set; }
        public CancellationToken LastToken { get; private set; }
        public Action? AfterBatch { get; set; }

        Task<string> IFileService.ReadTextAsync(string path)
        {
            if (path.EndsWith(".novalist", StringComparison.Ordinal)) DirectSceneReads++;
            return ReadTextAsync(path);
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            foreach (var path in paths.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                result[path] = await ReadTextAsync(path);
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(paths.ToArray());
            LastToken = cancellationToken;
            var result = await ReadTextsAsync(paths.Where(File.Exists).ToArray(), cancellationToken);
            AfterBatch?.Invoke();
            return result;
        }
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
