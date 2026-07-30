using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend;

/// <summary>
/// Owns the JSON-RPC endpoint over a duplex stream pair and the lifetime of the
/// RPC facades. stdout framing is LSP-style (Content-Length headers) so the
/// renderer can use vscode-jsonrpc unchanged.
/// </summary>
public sealed class BackendHost : IDisposable
{
    private readonly TaskCompletionSource _shutdownRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Workspace _workspace;
    private readonly IProcessRunner? _processRunner;
    private JsonRpc? _rpc;
    private Core.Services.AssetWatchService? _assetWatch;

    /// <param name="processRunner">
    /// External-process backend for shell-out services (Git). Null uses the real
    /// <see cref="ProcessRunner"/> (desktop). The mobile host injects an
    /// <see cref="UnavailableProcessRunner"/> so Git degrades to "unavailable"
    /// inside the sandbox.
    /// </param>
    public BackendHost(string? settingsDirectory = null, IProcessRunner? processRunner = null)
    {
        _workspace = new Workspace(settingsDirectory);
        _processRunner = processRunner;
    }

    /// <summary>Reroutes Console.Out to stderr so stray writes cannot corrupt RPC framing.</summary>
    public static void GuardStandardOutput()
    {
        Console.SetOut(Console.Error);
    }

    /// <summary>Attaches the RPC endpoint and all facades to the given streams. Does not block.</summary>
    public JsonRpc Attach(Stream sending, Stream receiving)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var handler = new HeaderDelimitedMessageHandler(sending, receiving, formatter);
        var rpc = new JsonRpc(handler);
        var targetOptions = new JsonRpcTargetOptions { DisposeOnDisconnect = false };
        rpc.AddLocalRpcTarget(new SystemRpc(RequestShutdown), targetOptions);
        rpc.AddLocalRpcTarget(new ProjectRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ScenesRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new BinderRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CollectionsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CompletionRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new AttachmentsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CleanupRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new LinksRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new DarlingsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new VoicesRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new GroupsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new EntitiesRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new WikiRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ContextRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new DialogueRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new DashboardRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ManuscriptRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new PlotRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SmartListsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new BookmarksRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new TimelineRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CalendarRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new RelationshipsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new LibraryRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ExportRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ExposeRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new GitRpc(_workspace, _processRunner), targetOptions);
        rpc.AddLocalRpcTarget(new SearchRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SnapshotsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new BackupRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new DraftCompareRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CorkboardRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SuggestionsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new StyleRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ReviewRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new CanvasRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SceneBulkRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SpellRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SceneStageRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new WordTargetRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new PublishingRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SceneSplitRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new StructureRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ExportPresetRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new AnalyticsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SprintRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new MatterRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SettingsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new AppearanceRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new MapsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new GrammarRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new TemplatesRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ImportRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ManuscriptImportRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ManuscriptPropertyRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new PremiseRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new PromiseRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new InboxRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ArcRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SceneLabelRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new TagsRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SceneTemplateRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new SeriesRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new EntitySheetRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new UnlinkedMentionRpc(_workspace), targetOptions);
        var extensionsRpc = new ExtensionsRpc(_workspace);
        rpc.AddLocalRpcTarget(extensionsRpc, targetOptions);
        rpc.AddLocalRpcTarget(new ExtensionContribRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new ExtensionStoreRpc(_workspace), targetOptions);
        rpc.AddLocalRpcTarget(new UiBridgeRpc(_workspace), targetOptions);
        Extensions.HostNotifications.Error = message =>
            _ = rpc.NotifyAsync("ui/showNotification", message);
        // Route imperative host-service UI capabilities (toasts, busy-progress,
        // wizards) out to the renderer as ui/* notifications.
        _workspace.UiBridge.Notifier = (method, payload) => rpc.NotifyAsync(method, payload);
        ExtensionsRpc.WebviewPosted = (extensionId, viewKey, json) =>
            _ = rpc.NotifyAsync("extensions/webviewPosted", extensionId, viewKey, json);
        // Themes, Locales and Analysis were read once at startup and a restart
        // was needed after any change - which is the wrong loop for something a
        // writer iterates on. The renderer reloads the folders it is told about.
        _workspace.UserAssets.EnsureDirectories();
        _assetWatch = new Core.Services.AssetWatchService(
            new Dictionary<Core.Services.UserAssetKind, string>
            {
                [Core.Services.UserAssetKind.Themes] = _workspace.UserAssets.ThemesDirectory,
                [Core.Services.UserAssetKind.Locales] = _workspace.UserAssets.LocalesDirectory,
                [Core.Services.UserAssetKind.Analysis] = _workspace.UserAssets.AnalysisDirectory
            },
            changed => rpc.NotifyAsync("appearance/assetsChanged", AssetsChanged(changed)));
        rpc.StartListening();
        _rpc = rpc;
        return rpc;
    }

    /// <summary>Runs until the peer disconnects or a shutdown request arrives.</summary>
    public async Task RunAsync(Stream sending, Stream receiving)
    {
        var rpc = Attach(sending, receiving);
        await Task.WhenAny(rpc.Completion, _shutdownRequested.Task);
    }

    /// <summary>Test seam: the owned workspace (for driving host-service events
    /// through the attached RPC endpoint).</summary>
    internal Workspace Workspace => _workspace;

    internal void RequestShutdown() => _shutdownRequested.TrySetResult();

    internal bool IsShutdownRequested => _shutdownRequested.Task.IsCompleted;

    /// <summary>
    /// Handles a folder change and returns the names to tell the renderer.
    ///
    /// The analysis lexicons are read in this process, so they are re-registered
    /// here; themes and locales live in the renderer, which is only told which
    /// folders to re-read. Extracted from the watcher callback so the decision
    /// is testable without a real filesystem event.
    /// </summary>
    internal string[] AssetsChanged(IReadOnlyCollection<Core.Services.UserAssetKind> changed)
    {
        if (changed.Contains(Core.Services.UserAssetKind.Analysis))
        {
            Core.Services.SceneAnalysisLexicon.RegisterUserDirectory(
                _workspace.UserAssets.AnalysisDirectory);
        }
        return [.. changed.Select(kind => kind.ToString().ToLowerInvariant())];
    }

    public void Dispose()
    {
        _assetWatch?.Dispose();
        _rpc?.Dispose();
        _workspace.Dispose();
    }
}
