using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class ExportConcurrencyTests
{
    [Fact]
    public async Task BookSwitchDuringCompilationCannotChangeLaterSceneReads()
    {
        using var fixture = new ExportFixture();
        var client = fixture.Client;
        var original = await client.InvokeAsync<ProjectStateDto>("project/create", fixture.Root, "Series", "Original");
        var originalChapter = await CreateScenesAsync(client, "Original chapter", ["Original one", "Original two", "Original three"]);
        var other = await client.InvokeAsync<ProjectStateDto>("project/createBook", "Other");
        await client.InvokeAsync<ProjectStateDto>("project/switchBook", other.Books.Single(book => book.Name == "Other").Id);
        await CreateScenesAsync(client, "Foreign chapter", ["Foreign prose"]);
        await client.InvokeAsync<ProjectStateDto>("project/switchBook", original.ActiveBookId);

        var projects = fixture.Host.Workspace.Projects;
        var chapter = projects.GetChaptersOrdered().Single();
        var scenes = projects.GetScenesForChapter(chapter.Guid);
        fixture.Files.PausePath = projects.GetSceneFilePath(chapter, scenes[1]);
        var outputPath = Path.Combine(fixture.Root, "export.md");
        var export = client.InvokeAsync<ExportResultDto>("export/run", "Markdown", outputPath,
            "Original export", "Writer", false, new[] { originalChapter });
        try
        {
            await fixture.Files.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var switching = client.InvokeAsync<ProjectStateDto>("project/switchBook", other.Books.Single(book => book.Name == "Other").Id);
            await client.InvokeAsync<PingResult>("system/ping").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(switching.IsCompleted);
            Assert.Equal(original.ActiveBookId, projects.ActiveBook!.Id);

            fixture.Files.Release.TrySetResult();
            var exported = await export.WaitAsync(TimeSpan.FromSeconds(10));
            var switched = await switching.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(exported.Success);
            Assert.NotEqual(original.ActiveBookId, switched.ActiveBookId);
            var output = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("Original one", output);
            Assert.Contains("Original two", output);
            Assert.Contains("Original three", output);
            Assert.DoesNotContain("Foreign prose", output);
            Assert.DoesNotContain("Foreign chapter", output);
        }
        finally { fixture.Files.Release.TrySetResult(); }
    }

    private static async Task<string> CreateScenesAsync(JsonRpc client, string title, string[] paragraphs)
    {
        var state = await client.InvokeAsync<ProjectStateDto>("project/createChapter", title);
        var chapter = state.Chapters.Single();
        foreach (var paragraph in paragraphs)
        {
            state = await client.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, paragraph);
            var scene = state.Chapters.Single().Scenes.Last();
            await client.InvokeAsync<SceneWriteResultDto>("scenes/write", chapter.Guid, scene.Id,
                $"<p>{paragraph}</p>", paragraph);
        }
        return chapter.Guid;
    }

    private sealed class ExportFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "nl-export-race-" + Guid.NewGuid().ToString("N"));
        public DelayedSceneRead Files { get; } = new();
        public BackendHost Host { get; }
        public JsonRpc Client { get; }

        public ExportFixture()
        {
            Directory.CreateDirectory(Root);
            var (serverStream, clientStream) = FullDuplexStream.CreatePair();
            Host = new BackendHost(Path.Combine(Root, "settings"), fileService: new CoordinatedFileService(Files));
            Host.Attach(serverStream, serverStream);
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            Client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter));
            Client.StartListening();
        }

        public void Dispose()
        {
            Files.Release.TrySetResult();
            Client.Dispose();
            Host.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class DelayedSceneRead : IFileAccessCoordinator
    {
        public string? PausePath { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> ReadAsync<T>(string path, Func<string, T> access)
        {
            if (typeof(T) == typeof(string) && path == PausePath)
            {
                Paused.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            return access(path);
        }

        public Task<T> WriteAsync<T>(string path, bool deleting, Func<string, T> access)
            => Task.FromResult(access(path));

        public Task<T> MoveAsync<T>(string source, string destination, Func<string, string, T> access)
            => Task.FromResult(access(source, destination));
    }
}
