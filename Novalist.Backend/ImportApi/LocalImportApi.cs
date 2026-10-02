using System.Text.Json;
using Novalist.Core.Services;
#if NOVALIST_IMPORT_API
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#endif

namespace Novalist.Backend.ImportApi;

public sealed record ImportApiStatus(bool Running, string? Url, string? Token, string? Book, string? Draft,
    int Imported = 0, int Skipped = 0, int Failed = 0);

/// <summary>An explicitly started, loopback-only import surface for external agents.</summary>
public sealed class LocalImportApi : IDisposable
{
#if NOVALIST_IMPORT_API
    private readonly Workspace workspace;
    private readonly SemaphoreSlim gate;
#endif
    public LocalImportApi(Workspace workspace, SemaphoreSlim gate)
    {
#if NOVALIST_IMPORT_API
        this.workspace = workspace;
        this.gate = gate;
#endif
    }

#if NOVALIST_IMPORT_API
    public const int MaxBodyBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };
    private Session? _session;
    internal Func<WebApplication, Task> StartServer { get; set; } = static app => app.StartAsync();

    public async Task<ImportApiStatus> StartAsync(string sectionTitle)
    {
        await StopIfScopeChangedAsync();
        if (_session != null) return Status();
        var service = new StructuredImportService(workspace.Projects, workspace.FileService, sectionTitle);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [], ApplicationName = typeof(LocalImportApi).Assembly.GetName().Name, ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        // This process's stdout carries framed RPC, and request logs could contain source IDs.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, ApiLifetime>();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = StructuredImportService.MaxImageBytes;
            options.Limits.MaxConcurrentConnections = 8;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        var app = builder.Build();
        var session = new Session(workspace, service, app);
        app.Run(context => HandleAsync(session, context));
        try
        {
            await StartServer(app);
            session.Url = app.Urls.Single() + "/v1";
            _session = session;
        }
        catch
        {
            await app.DisposeAsync();
            session.Cancel.Dispose();
            throw;
        }
        return Status();
    }

    public async Task<ImportApiStatus> StatusAsync()
    {
        await StopIfScopeChangedAsync();
        return Status();
    }

    private ImportApiStatus Status() => _session is { } session
        ? new(true, session.Url, session.Token, workspace.Projects.ActiveBook!.Name,
            workspace.Projects.ActiveBook!.ActiveDraft!.Name, session.Imported, session.Skipped, session.Failed)
        : new(false, null, null, null, null);

    public Task StopIfScopeChangedAsync()
        => _session != null && !_session.Matches(workspace) ? StopAsync() : Task.CompletedTask;

    public async Task StopAsync()
    {
        var session = _session;
        _session = null;
        if (session == null) return;
        session.Cancel.Cancel();
        try { await session.App.StopAsync(); }
        finally
        {
            await session.App.DisposeAsync();
            session.Cancel.Dispose();
        }
    }

    private async Task HandleAsync(Session session, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Host.Host != "127.0.0.1" || context.Request.Headers.Origin.Count > 0)
        {
            await ErrorAsync(context, 403, "local_access_only", "Use the supplied loopback URL from a local agent, without an Origin header.");
            return;
        }
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()),
                Encoding.UTF8.GetBytes("Bearer " + session.Token)))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ErrorAsync(context, 401, "unauthorized", "Send the access token in the Authorization: Bearer header.");
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Cancel.Token, context.RequestAborted);
        try
        {
            ImportRequest? input = null;
            ImageAttachmentRequest? attachment = null;
            byte[]? image = null;
            var contentType = context.Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
            if (context.Request.Method == "POST" && context.Request.Path is { Value: "/v1/import" or "/v1/import/validate" or "/v1/images/attach" or "/v1/images" })
            {
                var upload = context.Request.Path == "/v1/images";
                var limit = upload ? StructuredImportService.MaxImageBytes : MaxBodyBytes;
                context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = limit;
                if (upload ? !StructuredImportService.ImageContentTypes.Contains(contentType) : contentType != "application/json")
                {
                    await ErrorAsync(context, 415, upload ? "image_required" : "json_required", upload
                        ? "Send raw PNG, JPEG, GIF, WebP, BMP or SVG bytes with their matching image Content-Type." : "Send Content-Type: application/json.");
                    return;
                }
                if (context.Request.ContentLength > limit)
                {
                    await ErrorAsync(context, 413, "request_too_large", $"Limit this request to {limit} bytes.");
                    return;
                }
                if (upload)
                {
                    using var buffer = new MemoryStream();
                    await context.Request.Body.CopyToAsync(buffer, cancellation.Token);
                    image = buffer.ToArray();
                }
                else if (context.Request.Path == "/v1/images/attach")
                {
                    attachment = await JsonSerializer.DeserializeAsync<ImageAttachmentRequest>(context.Request.Body, JsonOptions, cancellation.Token);
                    if (attachment?.Target == null || attachment.Id == null || attachment.Images == null)
                        throw new ArgumentException("Send an object containing target, id and an images array of uploaded image references.");
                }
                else
                {
                    input = await JsonSerializer.DeserializeAsync<ImportRequest>(context.Request.Body, JsonOptions, cancellation.Token);
                    if (input?.Entries == null || input.Entries.Any(entry => entry == null))
                        throw new ArgumentException("Send an object containing an entries array of import records.");
                }
            }
            ApiResponse response;
            await gate.WaitAsync(cancellation.Token);
            try
            {
                if (_session != session || !session.Matches(workspace))
                    response = Error(409, "session_changed", "The import session ended or its project, book or draft changed. Start it again in Novalist.");
                else response = await DispatchAsync(session, context.Request.Method, context.Request.Path.Value!, input, attachment, image, contentType);
            }
            finally { gate.Release(); }
            context.Response.StatusCode = response.Status;
            if (response.Allow != null) context.Response.Headers.Allow = response.Allow;
            if (response.Json != null)
            {
                context.Response.ContentType = "application/schema+json; charset=utf-8";
                await context.Response.WriteAsync(response.Json);
            }
            else await context.Response.WriteAsJsonAsync(response.Payload, JsonOptions);
        }
        catch (JsonException) { await ErrorAsync(context, 400, "invalid_json", "Send valid JSON with the documented request shape and exact keys."); }
        catch (ArgumentException exception) { await ErrorAsync(context, 400, "invalid_request", exception.Message); }
        catch (ImportImageNotFoundException exception) { await ErrorAsync(context, 404, "image_not_found", exception.Message); }
        catch (ImportEntryLockedException exception) { await ErrorAsync(context, 409, "entry_locked", exception.Message); }
        catch (KeyNotFoundException exception) { await ErrorAsync(context, 404, "entry_not_found", exception.Message); }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) { await ErrorAsync(context, exception.StatusCode, "invalid_http_request", "The request is too large or could not be read."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ErrorAsync(context, 503, "save_failed", "The request could not be saved. Check disk space and file permissions, then retry with the same source IDs or image bytes.");
        }
        catch (OperationCanceledException)
        {
            if (!context.RequestAborted.IsCancellationRequested)
                await ErrorAsync(context, 410, "session_ended", "The local import API stopped. Start it again in Novalist and use its new connection instructions.");
        }
    }

    private async Task<ApiResponse> DispatchAsync(Session session, string method, string path, ImportRequest? input,
        ImageAttachmentRequest? attachment, byte[]? image, string? contentType)
    {
        var isWrite = path is "/v1/import" or "/v1/import/validate" or "/v1/images" or "/v1/images/attach";
        if (method != (isWrite ? "POST" : "GET"))
            return new(405, new { error = new { code = "method_not_allowed", message = "Use the documented HTTP method." } }, Allow: isWrite ? "POST" : "GET");
        switch (path)
        {
            case "/v1":
            case "/v1/":
                return new(200, new
                {
                    name = "Novalist import API", version = 1,
                    instructions = "Read types and schemas, preserve source facts and writing. Upload local images first and use their returned imageId in data.images. Validate documents, then import bounded batches. Never invent IDs or unknown facts. Reuse each sourceId on retries; existing entries are skipped without overwriting edits. To add pictures to a skipped entry, POST /v1/images/attach with its target, result id and image references.",
                    limits = new { maxEntries = StructuredImportService.MaxEntries, maxBodyBytes = MaxBodyBytes,
                        maxImageBytes = StructuredImportService.MaxImageBytes, maxImagesPerEntry = StructuredImportService.MaxImagesPerEntry },
                    endpoints = new { types = "/v1/types", schema = "/v1/schema", typeSchema = "/v1/types/{key}/schema", validate = "/v1/import/validate", import = "/v1/import", status = "/v1/status", images = "/v1/images", attachImages = "/v1/images/attach" },
                    request = new { entries = new[] { new { sourceId = "stable full source path, plus #entry-name when splitting a file", folder = "Optional scene chapter or tag group", document = "An object matching the target import schema" } } },
                    imageUpload = new { method = "POST", endpoint = "/v1/images", body = "Raw image bytes, one file per request; never JSON or base64", contentTypes = StructuredImportService.ImageContentTypes,
                        response = new { imageId = "64 lowercase hexadecimal characters identifying the uploaded bytes", contentType = "Verified image MIME type", bytes = "Image size", reused = "true when the same bytes were already stored" } },
                    imageAttachment = new { method = "POST", endpoint = "/v1/images/attach", target = "Codex target whose schema includes data.images", id = "The imported or skipped result id", images = new[] { new { imageId = "The upload response imageId", name = "Source image label or filename", alt = "Optional alternative text from the source" } },
                        instructions = "Adds missing pictures only; preserves text, fields and existing images. Repeat safely. Locked entries must be unlocked in Novalist first." },
                    destination = new { book = workspace.Projects.ActiveBook!.Name, draft = workspace.Projects.ActiveBook!.ActiveDraft!.Name }
                });
            case "/v1/types":
                var types = new EntityService(workspace.Projects).GetCustomEntityTypes().ToDictionary(type => type.TypeKey);
                return new(200, new { types = session.Service.Schema().Targets.Select(key => new
                {
                    key, name = types.GetValueOrDefault(key)?.DisplayName ?? key, schema = "/v1/types/" + Uri.EscapeDataString(key) + "/schema"
                }).ToArray() });
            case "/v1/schema":
                return new(200, null, session.Service.Schema().Export());
            case "/v1/status":
                return new(200, new { running = true, imported = session.Imported, skipped = session.Skipped, failed = session.Failed });
            case "/v1/images":
                var uploaded = await session.Service.UploadImageAsync(image!, contentType!);
                return new(uploaded.Reused ? 200 : 201, uploaded);
            case "/v1/images/attach":
                var attached = await session.Service.AttachImagesAsync(attachment!.Target!, attachment.Id!, attachment.Images!);
                if (attached.Added > 0)
                {
                    workspace.UiBridge.EntitiesChanged();
                    workspace.UiBridge.ImportChanged(attached.Target, attached.Id);
                }
                return new(200, attached);
            case "/v1/import":
            case "/v1/import/validate":
                var validateOnly = path.EndsWith("/validate", StringComparison.Ordinal);
                var results = await session.Service.ImportAsync(input!.Entries!, validateOnly);
                var imported = results.Count(result => result.Status == "imported");
                var skipped = results.Count(result => result.Status == "skipped");
                var failed = results.Count(result => result.Status is "invalid" or "failed");
                if (!validateOnly)
                {
                    session.Imported += imported;
                    session.Skipped += skipped;
                    session.Failed += failed;
                    if (imported + skipped > 0)
                    {
                        workspace.UiBridge.EntitiesChanged();
                        workspace.UiBridge.ProjectStructureChanged();
                        workspace.UiBridge.ImportChanged();
                    }
                }
                return new(200, new { results, imported, skipped, failed, validated = results.Count(result => result.Status == "valid"), validateOnly });
            default:
                const string prefix = "/v1/types/", suffix = "/schema";
                if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) && path.Length > prefix.Length + suffix.Length)
                {
                    var key = path[prefix.Length..^suffix.Length];
                    var schema = session.Service.Schema();
                    if (schema.Targets.Contains(key, StringComparer.Ordinal)) return new(200, null, schema.ExportTarget(key));
                }
                return Error(404, "not_found", "Unknown endpoint or import type. Read GET /v1 for discovery.");
        }
    }

    private static ApiResponse Error(int status, string code, string message) => new(status, new { error = new { code, message } });
    private static Task ErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        var body = JsonSerializer.SerializeToUtf8Bytes(Error(status, code, message).Payload, JsonOptions);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = body.Length;
        return context.Response.Body.WriteAsync(body).AsTask();
    }

    private sealed record ImportRequest(StructuredImportEntry[]? Entries);
    private sealed record ImageAttachmentRequest(string? Target, string? Id, ImportImageReference[]? Images);
    private sealed record ApiResponse(int Status, object? Payload, string? Json = null, string? Allow = null);
    private sealed class ApiLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Session(Workspace workspace, StructuredImportService service, WebApplication app)
    {
        private readonly object _project = workspace.Projects.CurrentProject!;
        private readonly string? _bookRoot = workspace.Projects.ActiveBookRoot, _draftRoot = workspace.Projects.ActiveDraftRoot;
        public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        public string Url { get; set; } = string.Empty;
        public CancellationTokenSource Cancel { get; } = new();
        public StructuredImportService Service { get; } = service;
        public WebApplication App { get; } = app;
        public int Imported, Skipped, Failed;
        public bool Matches(Workspace current) => ReferenceEquals(_project, current.Projects.CurrentProject)
            && _bookRoot == current.Projects.ActiveBookRoot && _draftRoot == current.Projects.ActiveDraftRoot;
    }
#else
    public Task<ImportApiStatus> StartAsync(string sectionTitle) => throw new PlatformNotSupportedException("The local import API is available in the desktop app.");
    public Task<ImportApiStatus> StatusAsync() => Task.FromResult(new ImportApiStatus(false, null, null, null, null));
    public Task StopAsync() => Task.CompletedTask;
    public Task StopIfScopeChangedAsync() => Task.CompletedTask;
#endif
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
