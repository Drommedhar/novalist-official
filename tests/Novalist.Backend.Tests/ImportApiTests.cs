using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nerdbank.Streams;
using Novalist.Backend.ImportApi;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class ImportApiTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    private Workspace Workspace() => new(Path.Combine(_dir.Path, "settings"));
    private async Task OpenAsync(Workspace workspace) => await workspace.Projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
    private static HttpClient Client(ImportApiStatus status)
    {
        var client = new HttpClient { BaseAddress = new Uri(status.Url! + "/"), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", status.Token);
        return client;
    }
    private static string Batch(string source = "source/ada.md", string target = "character", string data = "\"name\":\"Ada\",\"surname\":\"Lovelace\"")
        => $$"""{"entries":[{"sourceId":"{{source}}","document":{"novalistImport":1,"target":"{{target}}","data":{ {{data}} },"content":"Preserved **writing**."} } ] }""";
    private static async Task<JsonNode> PostAsync(HttpClient client, string body, string endpoint = "import")
    {
        using var response = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task ImagesAreDiscoverableUploadedImportedAndAttachedWithoutOverwritingExistingEntries()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        using var client = Client(await api.StartAsync("Source writing"));
        var discovery = JsonNode.Parse(await client.GetStringAsync(""))!;
        Assert.Equal("/v1/images", discovery["endpoints"]!["images"]!.GetValue<string>());
        Assert.Equal("/v1/images/attach", discovery["endpoints"]!["attachImages"]!.GetValue<string>());
        Assert.Equal(StructuredImportService.MaxImageBytes, discovery["limits"]!["maxImageBytes"]!.GetValue<int>());
        Assert.Contains(discovery["imageUpload"]!["contentTypes"]!.AsArray(), item => item!.GetValue<string>() == "image/svg+xml");
        var schema = JsonNode.Parse(await client.GetStringAsync("types/character/schema"))!;
        Assert.Equal("imageId", schema["properties"]!["data"]!["properties"]!["images"]!["items"]!["required"]![0]!.GetValue<string>());
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvIoAAAAASUVORK5CYII=");
        using var bytes = new ByteArrayContent(png);
        bytes.Headers.ContentType = new("image/png");
        using var uploaded = await client.PostAsync("images", bytes);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var upload = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!;
        Assert.False(upload["reused"]!.GetValue<bool>());
        Assert.Equal(png.Length, upload["bytes"]!.GetValue<int>());
        var imageId = upload["imageId"]!.GetValue<string>();
        Assert.Equal(64, imageId.Length);
        Assert.Null(upload["path"]);
        using var reused = await client.PostAsync("images", bytes);
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.True(JsonNode.Parse(await reused.Content.ReadAsStringAsync())!["reused"]!.GetValue<bool>());
        var data = JsonSerializer.Serialize(new { name = "Ada", surname = "Lovelace", images = new[] { new { imageId, name = "Portrait", alt = "Source description" } } });
        var body = Batch(data: data[1..^1]);
        Assert.Equal(1, (await PostAsync(client, body, "import/validate"))["validated"]!.GetValue<int>());
        var imported = await PostAsync(client, body);
        Assert.Equal(1, imported["imported"]!.GetValue<int>());
        var entity = Assert.Single(await new EntityService(workspace.Projects).LoadCharactersAsync());
        var image = Assert.Single(entity.Images);
        Assert.Equal("Source description", image.Alt);
        Assert.Equal(png, await File.ReadAllBytesAsync(Path.Combine(workspace.Projects.ActiveBookRoot!, image.Path)));

        // A previous text-only import can receive its missing photos on a retry.
        var old = await PostAsync(client, Batch("older", "location", "\"name\":\"Port\",\"description\":\"Original text\""));
        var id = old["results"]![0]!["id"]!.GetValue<string>();
        var references = new[] { new { imageId, name = "Harbour", alt = "A harbour" } };
        var attach = JsonSerializer.Serialize(new { target = "location", id, images = references });
        Assert.Equal(1, (await PostAsync(client, attach, "images/attach"))["added"]!.GetValue<int>());
        var port = Assert.Single(await new EntityService(workspace.Projects).LoadLocationsAsync());
        Assert.Equal("Original text", port.Description);
        Assert.Equal("Harbour", Assert.Single(port.Images).Name);
        Assert.Equal(0, (await PostAsync(client, attach, "images/attach"))["added"]!.GetValue<int>());
        Assert.Equal(2, (await api.StatusAsync()).Imported);
        await api.StopAsync();
        using var restarted = Client(await api.StartAsync("Source writing"));
        using var nextUpload = await restarted.PostAsync("images", bytes);
        Assert.Equal(HttpStatusCode.OK, nextUpload.StatusCode);
        Assert.Equal(imageId, JsonNode.Parse(await nextUpload.Content.ReadAsStringAsync())!["imageId"]!.GetValue<string>());
        Assert.Equal(0, (await PostAsync(restarted, attach, "images/attach"))["added"]!.GetValue<int>());
    }

    [Fact]
    public async Task ImageEndpointsRejectWrongBodiesMissingAssetsAndLockedEntries()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        using var client = Client(await api.StartAsync("Source writing"));
        foreach (var endpoint in new[] { "images", "images/attach" })
        {
            using var wrongMethod = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            Assert.Contains("POST", wrongMethod.Content.Headers.Allow);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(endpoint, new StringContent("{}"))).StatusCode);
        }
        foreach (var bytes in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("not a PNG") })
        {
            using var body = new ByteArrayContent(bytes);
            body.Headers.ContentType = new("image/png");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("images", body)).StatusCode);
        }
        foreach (var body in new[] { "null", "{}", "{\"target\":\"character\",\"id\":\"bad\",\"images\":[]}", "{\"target\":\"character\",\"id\":null,\"images\":[]}", "{\"target\":\"character\",\"id\":\"x\",\"images\":null}", "{\"target\":\"character\",\"id\":\"x\",\"images\":[],\"path\":\"other\"}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("images/attach", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        var id = Guid.NewGuid().ToString();
        var missing = JsonSerializer.Serialize(new { target = "character", id, images = Array.Empty<object>() });
        using var notFound = await client.PostAsync("images/attach", new StringContent(missing, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("entry_not_found", await notFound.Content.ReadAsStringAsync());
        var imported = await PostAsync(client, Batch());
        id = imported["results"]![0]!["id"]!.GetValue<string>();
        var badAsset = JsonSerializer.Serialize(new { target = "character", id, images = new[] { new { imageId = new string('0', 64), name = "Missing" } } });
        using var unknownImage = await client.PostAsync("images/attach", new StringContent(badAsset, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, unknownImage.StatusCode);
        Assert.Contains("image_not_found", await unknownImage.Content.ReadAsStringAsync());
        var invalidReference = JsonSerializer.Serialize(new { target = "character", id, images = new[] { new { imageId = "../secret.png", name = "Bad" } } });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("images/attach", new StringContent(invalidReference, Encoding.UTF8, "application/json"))).StatusCode);
        var entity = Assert.Single(await new EntityService(workspace.Projects).LoadCharactersAsync());
        entity.Locked = true;
        await new EntityService(workspace.Projects).SaveCharacterAsync(entity);
        using var locked = await client.PostAsync("images/attach", new StringContent(badAsset, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Contains("entry_locked", await locked.Content.ReadAsStringAsync());
        Assert.Empty(entity.Images);
    }

    [Fact]
    public async Task ImageUploadsHaveTheirOwnBoundedLimitIncludingChunkedTransfers()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        using var client = Client(await api.StartAsync("Source writing"));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvIoAAAAASUVORK5CYII=");
        var padded = new byte[LocalImportApi.MaxBodyBytes + 1];
        png.CopyTo(padded, 0);
        using var allowed = new ByteArrayContent(padded);
        allowed.Headers.ContentType = new("image/png");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("images", allowed)).StatusCode);
        using var oversized = new ByteArrayContent(new byte[StructuredImportService.MaxImageBytes + 1]);
        oversized.Headers.ContentType = new("image/png");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("images", oversized)).StatusCode);
        using var chunked = new HttpRequestMessage(HttpMethod.Post, "images");
        chunked.Headers.TransferEncodingChunked = true;
        chunked.Content = oversized;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(chunked)).StatusCode);
        Assert.Single(Directory.GetFiles(Path.Combine(workspace.Projects.ActiveBookRoot!, workspace.Projects.ActiveBook!.ImageFolder), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DiscoverySchemasValidationAndImportsRoundTripWithCurrentCustomFieldsAndRetryProtection()
    {
        using var workspace = Workspace();
        using var api = new LocalImportApi(workspace, new(1, 1));
        var rpc = new ImportApiRpc(api);
        Assert.False((await rpc.StatusAsync()).Running);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.StartAsync("Source writing"));
        await OpenAsync(workspace);
        workspace.Projects.ActiveBook!.CharacterTemplates = [new() { Id = "people", Name = "People", CustomPropertyDefs = [new() { Key = "Skill", Prompt = "What can they do?" }] }];
        workspace.Projects.ActiveBook.ActiveCharacterTemplateId = "people";
        workspace.Projects.CurrentProject!.CustomEntityTypes.Add(new() { TypeKey = "faction", DisplayName = "Faction", FolderName = "Factions", DefaultFields = [new() { Key = "strength", Type = CustomPropertyType.Int, Required = true }] });
        var status = await rpc.StartAsync("Source writing");
        Assert.Equal("Book", status.Book);
        Assert.NotNull(status.Draft);
        Assert.StartsWith("http://127.0.0.1:", status.Url);
        Assert.Equal(64, status.Token!.Length);
        Assert.Equal(status, await rpc.StartAsync("Ignored"));
        using var client = Client(status);
        var discovery = JsonNode.Parse(await client.GetStringAsync(""))!;
        Assert.Equal(40, discovery["limits"]!["maxEntries"]!.GetValue<int>());
        Assert.Equal(LocalImportApi.MaxBodyBytes, discovery["limits"]!["maxBodyBytes"]!.GetValue<int>());
        Assert.Equal(200, (int)(await client.GetAsync(status.Url)).StatusCode);
        var types = JsonNode.Parse(await client.GetStringAsync("types"))!["types"]!.AsArray();
        Assert.Equal(7, types.Count);
        Assert.Contains(types, type => type!["key"]!.GetValue<string>() == "faction" && type["name"]!.GetValue<string>() == "Faction");
        using var schemaResponse = await client.GetAsync("types/character/schema");
        Assert.Equal("application/schema+json", schemaResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", schemaResponse.Headers.CacheControl!.ToString());
        var schema = JsonNode.Parse(await schemaResponse.Content.ReadAsStringAsync())!;
        Assert.Equal("people", schema["properties"]!["data"]!["properties"]!["templateId"]!["const"]!.GetValue<string>());
        Assert.Equal("string", schema["properties"]!["data"]!["properties"]!["customProperties"]!["properties"]!["Skill"]!["type"]!.GetValue<string>());
        workspace.Projects.ActiveBook.CharacterTemplates[0].CustomPropertyDefs.Add(new() { Key = "Alive", Type = CustomPropertyType.Bool });
        Assert.Contains("Alive", await client.GetStringAsync("schema"));
        var body = Batch(data: "\"name\":\"Ada\",\"surname\":\"Lovelace\",\"customProperties\":{\"Skill\":\"Mathematics\",\"Alive\":\"true\"}");
        var validation = await PostAsync(client, body, "import/validate");
        Assert.Equal("valid", validation["results"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(1, validation["validated"]!.GetValue<int>());
        Assert.Empty(await new Novalist.Core.Services.EntityService(workspace.Projects).LoadCharactersAsync());
        Assert.Equal(0, (await rpc.StatusAsync()).Imported);
        var result = await PostAsync(client, body);
        Assert.Equal(1, result["imported"]!.GetValue<int>());
        Assert.Equal(1, (await rpc.StatusAsync()).Imported);
        var ada = Assert.Single(await new Novalist.Core.Services.EntityService(workspace.Projects).LoadCharactersAsync());
        Assert.Equal("Lovelace", ada.Surname);
        Assert.Equal("Mathematics", ada.CustomProperties!["Skill"]);
        Assert.Equal("true", ada.CustomProperties["Alive"]);
        var mixed = JsonNode.Parse(Batch("guild", "faction", "\"name\":\"Guild\",\"fields\":{\"strength\":\"42\"}"))!;
        mixed["entries"]!.AsArray().Add(JsonNode.Parse(Batch("invalid", "character", "\"surname\":\"Only\""))!["entries"]![0]!.DeepClone());
        mixed["entries"]!.AsArray().Add(JsonNode.Parse(body)!["entries"]![0]!.DeepClone());
        var partial = await PostAsync(client, mixed.ToJsonString());
        Assert.Equal(1, partial["imported"]!.GetValue<int>());
        Assert.Equal(1, partial["skipped"]!.GetValue<int>());
        Assert.Equal(1, partial["failed"]!.GetValue<int>());
        var counts = JsonNode.Parse(await client.GetStringAsync("status"))!;
        Assert.Equal(2, counts["imported"]!.GetValue<int>());
        Assert.Equal(1, counts["failed"]!.GetValue<int>());
        Assert.Null(counts["token"]);
        await rpc.StopAsync();
        await rpc.StopAsync();
        Assert.False((await rpc.StatusAsync()).Running);
        var next = await rpc.StartAsync("Source writing");
        Assert.NotEqual(status.Token, next.Token);
        using var nextClient = Client(next);
        Assert.Equal(1, (await PostAsync(nextClient, body))["skipped"]!.GetValue<int>());
        using var wrongToken = Client(next with { Token = status.Token });
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongToken.GetAsync("types")).StatusCode);
    }

    [Fact]
    public async Task AuthenticationAndProtocolErrorsHaveUsefulStatusesAndDoNotImportAnything()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        var status = await api.StartAsync("Source writing");
        using var anonymous = new HttpClient();
        using var unauthorized = await anonymous.GetAsync(status.Url + "/types");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("Bearer", unauthorized.Headers.WwwAuthenticate.Single().Scheme);
        Assert.DoesNotContain(status.Token!, await unauthorized.Content.ReadAsStringAsync());
        using var client = Client(status);
        using var origin = new HttpRequestMessage(HttpMethod.Get, "types");
        origin.Headers.Add("Origin", "https://example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(origin)).StatusCode);
        using var host = new HttpRequestMessage(HttpMethod.Get, "types");
        host.Headers.Host = "example.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(host)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("types/unknown/schema")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("types//schema")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("missing")).StatusCode);
        using var wrongMethod = await client.GetAsync("import");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        Assert.Contains("POST", wrongMethod.Content.Headers.Allow);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("types", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("import", new StringContent(Batch()))).StatusCode);
        foreach (var text in new[] { "not JSON", "null", "{}", "{\"entries\":[null]}", "{\"entries\":[]}", "{\"Entries\":[]}", "{\"entries\":[],\"unknown\":true}" })
        {
            using var response = await client.PostAsync("import", new StringContent(text, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var tooLarge = await client.PostAsync("import", new StringContent(new string('x', LocalImportApi.MaxBodyBytes + 1), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Empty(workspace.Projects.CurrentProject!.ResearchItems);
        Assert.Empty(await new Novalist.Core.Services.EntityService(workspace.Projects).LoadCharactersAsync());
    }

    [Fact]
    public async Task ChunkedBodiesAreAlsoBoundedBeforeAnyDataIsImported()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        using var client = Client(await api.StartAsync("Source writing"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "import");
        request.Headers.TransferEncodingChunked = true;
        request.Content = new StringContent(Batch().Replace("Preserved **writing**.", new string('x', LocalImportApi.MaxBodyBytes)), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await new EntityService(workspace.Projects).LoadCharactersAsync());
    }

    [Fact]
    public async Task ScopeChangesRejectOldRequestsAndStopTheListener()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        var status = await api.StartAsync("Source writing");
        using var client = Client(status);
        var other = await workspace.Projects.CreateBookAsync("Other");
        await workspace.Projects.SwitchBookAsync(other.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("types")).StatusCode);
        Assert.False((await api.StatusAsync()).Running);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("types"));
        await api.StartAsync("Source writing");
        workspace.CloseProject();
        await api.StopIfScopeChangedAsync();
        Assert.False((await api.StatusAsync()).Running);
    }

    [Fact]
    public async Task FailedStartupIsCleanedUpAndCanBeRetried()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        api.StartServer = _ => Task.FromException(new IOException("startup failed"));
        await Assert.ThrowsAsync<IOException>(() => api.StartAsync("Source writing"));
        Assert.False((await api.StatusAsync()).Running);
        api.StartServer = app => app.StartAsync();
        Assert.True((await api.StartAsync("Source writing")).Running);
    }

    [Fact]
    public async Task FailedManifestSaveReturnsRetryableErrorAndARepeatFlushesWithoutDuplicatingEntries()
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        using var api = new LocalImportApi(workspace, new(1, 1));
        using var client = Client(await api.StartAsync("Source writing"));
        var chapter = await workspace.Projects.CreateChapterAsync("Existing");
        var projectFile = Path.Combine(workspace.Projects.ProjectRoot!, ".novalist", "project.json");
        File.Move(projectFile, projectFile + ".backup");
        Directory.CreateDirectory(projectFile);
        using var response = await client.PostAsync("import", new StringContent(Batch("note", "research", "\"title\":\"Note\""), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(chapter.Guid, Assert.Single(workspace.Projects.ActiveBook!.Chapters).Guid);
        Directory.Delete(projectFile);
        File.Move(projectFile + ".backup", projectFile);
        Assert.Equal(1, (await PostAsync(client, Batch("note", "research", "\"title\":\"Note\"")))["skipped"]!.GetValue<int>());
        await workspace.Projects.LoadProjectAsync(workspace.Projects.ProjectRoot!);
        Assert.Single(workspace.Projects.CurrentProject!.ResearchItems);
    }

    private sealed class HoldingCoordinator : IFileAccessCoordinator
    {
        public bool HoldProjectSave;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> ReadAsync<T>(string path, Func<string, T> access) => Task.FromResult(access(path));
        public async Task<T> WriteAsync<T>(string path, bool deleting, Func<string, T> access)
        {
            if (HoldProjectSave && path.EndsWith("project.json", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return access(path);
        }
        public Task<T> MoveAsync<T>(string source, string destination, Func<string, string, T> access) => Task.FromResult(access(source, destination));
    }

    [Fact]
    public async Task HttpImportsShareTheEditorsQueueAndProjectCloseImmediatelyStopsTheApi()
    {
        var hold = new HoldingCoordinator();
        using var host = new BackendHost(Path.Combine(_dir.Path, "settings"), fileService: new CoordinatedFileService(hold));
        var streams = FullDuplexStream.CreatePair();
        host.Attach(streams.Item1, streams.Item1);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(streams.Item2, streams.Item2, formatter));
        rpc.StartListening();
        await rpc.InvokeAsync<ProjectStateDto>("project/create", _dir.Path, "Novel", "Book");
        using var client = Client(await rpc.InvokeAsync<ImportApiStatus>("importApi/start", "Source writing"));
        hold.HoldProjectSave = true;
        var held = rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Written in editor");
        await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pending = PostAsync(client, Batch("note", "research", "\"title\":\"Note\""));
        try
        {
            await rpc.InvokeAsync<PingResult>("system/ping");
            await Task.Delay(60);
            Assert.False(pending.IsCompleted);
            Assert.Empty(host.Workspace.Projects.CurrentProject!.ResearchItems);
        }
        finally { hold.Release.TrySetResult(); }
        await held;
        Assert.Equal(1, (await pending)["imported"]!.GetValue<int>());
        await rpc.InvokeAsync<ProjectStateDto>("project/close");
        Assert.False((await rpc.InvokeAsync<ImportApiStatus>("importApi/status")).Running);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("types"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopCancelsRequestsQueuedBehindEditorWork(bool disposeAsync)
    {
        using var workspace = Workspace();
        await OpenAsync(workspace);
        var gate = new SemaphoreSlim(1, 1);
        using var api = new LocalImportApi(workspace, gate);
        using var client = Client(await api.StartAsync("Source writing"));
        await gate.WaitAsync();
        try
        {
            var pending = client.GetAsync("types");
            await Task.Delay(80);
            var stop = disposeAsync ? api.DisposeAsync().AsTask() : api.StopAsync();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.Gone, (await pending).StatusCode);
            Assert.False((await api.StatusAsync()).Running);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task AsynchronousHostDisposalClosesTheImportListener()
    {
        HttpClient client;
        await using (var host = new BackendHost(Path.Combine(_dir.Path, "settings")))
        {
            var streams = FullDuplexStream.CreatePair();
            host.Attach(streams.Item1, streams.Item1);
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(streams.Item2, streams.Item2, formatter));
            rpc.StartListening();
            await rpc.InvokeAsync<ProjectStateDto>("project/create", _dir.Path, "Novel", "Book");
            client = Client(await rpc.InvokeAsync<ImportApiStatus>("importApi/start", "Source writing"));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("types")).StatusCode);
        }
        using (client)
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("types"));
    }
}
