using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class ExtensionsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public ExtensionsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Settings.LoadAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task LoadListContributions_WithSampleExtension()
    {
        // Deploy the real sample extension into the discovery folder.
        var extensionsDir = Path.Combine(_root, "exts", "Sample");
        Directory.CreateDirectory(extensionsDir);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(extensionsDir, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(extensionsDir, "extension.json"),
            """{"id":"com.novalist.sample","name":"Sample","version":"1.0.0","entryAssembly":"Novalist.Sdk.Example.dll"}""");

        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(Path.Combine(_root, "exts"));
        var rpc = new ExtensionsRpc(_workspace);
        var loaded = await rpc.LoadAsync();
        var again = await rpc.LoadAsync();

        Assert.Equal(loaded.Length, again.Length);
        Assert.Equal(loaded.Length, rpc.List().Length);
        var contributions = rpc.Contributions();
        Assert.NotNull(contributions.ExportFormats);
        Assert.True(contributions.AiHookCount >= 0);
    }

    private sealed class StubWebExtension : Novalist.Sdk.IExtension, Novalist.Sdk.Hooks.IWebViewContributor
    {
        public sealed class Controller : Novalist.Sdk.Hooks.IWebViewController, IDisposable
        {
            public event Action<string>? MessagePosted;
            public int Subscribers => MessagePosted?.GetInvocationList().Length ?? 0;
            public int DisposeCalls { get; private set; }
            public bool ThrowOnDispose { get; set; }
            public void Post(string payload) => MessagePosted?.Invoke(payload);
            public void Dispose()
            {
                DisposeCalls++;
                if (ThrowOnDispose) throw new InvalidOperationException("Controller disposal failed");
            }
            public Task<string?> OnMessageAsync(string json)
            {
                MessagePosted?.Invoke("""{"type":"pushed"}""");
                return Task.FromResult<string?>(json == "null-reply" ? null : $"echo:{json}");
            }
        }

        public string Id => "com.test.stub";
        public string DisplayName => "Stub";
        public string Description => "Stub extension";
        public string Version => "1.0.0";
        public string Author => "Tests";
        public void Initialize(Novalist.Sdk.Services.IHostServices host) { }
        public void Shutdown() { }
        public List<Controller> Created { get; } = [];
        public Novalist.Sdk.Hooks.IWebViewController? CreateController(string viewKey)
        {
            if (viewKey != "stub.view") return null;
            var controller = new Controller();
            Created.Add(controller);
            return controller;
        }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("uninstall")]
    [InlineData("workspace")]
    [InlineData("manager-shutdown")]
    [InlineData("disabled-state")]
    [InlineData("controller-fails")]
    public async Task StoppingExtensionsDisconnectsAndDisposesCachedControllers(string action)
    {
        var extension = new StubWebExtension();
        var folder = Path.Combine(_root, "stub");
        System.IO.Directory.CreateDirectory(folder);
        var info = new ExtensionInfo
        {
            Manifest = new Novalist.Sdk.ExtensionManifest { Id = extension.Id },
            FolderPath = folder, Instance = extension, IsEnabled = true, IsLoaded = true
        };
        _workspace.ExtensionsHost.Extensions.Add(info);
        using var rpc = new ExtensionsRpc(_workspace);
        var pushes = 0;
        ExtensionsRpc.WebviewPosted = (_, _, _) => pushes++;
        try
        {
            Assert.Equal("echo:hello", await rpc.WebviewMessageAsync(extension.Id, "stub.view", "hello"));
            var controller = Assert.Single(extension.Created);
            Assert.Equal(1, controller.Subscribers);
            controller.ThrowOnDispose = action == "controller-fails";
            switch (action)
            {
                case "uninstall": await rpc.UninstallAsync(extension.Id); break;
                case "workspace": _workspace.Dispose(); break;
                case "manager-shutdown": _workspace.ExtensionsHost.ShutdownAll(); break;
                case "disabled-state": info.IsEnabled = false; break;
                default: await rpc.SetEnabledAsync(extension.Id, false); break;
            }
            Assert.Null(await rpc.WebviewMessageAsync(extension.Id, "stub.view", "after-stop"));
            controller.Post("late push");
            Assert.Equal(1, pushes);
            Assert.Equal(0, controller.Subscribers);
            Assert.Equal(1, controller.DisposeCalls);
            if (action != "disable") return;
            info.Instance = extension;
            info.IsLoaded = true;
            await rpc.SetEnabledAsync(extension.Id, true);
            Assert.Equal("echo:fresh", await rpc.WebviewMessageAsync(extension.Id, "stub.view", "fresh"));
            Assert.Equal(2, extension.Created.Count);
        }
        finally { ExtensionsRpc.WebviewPosted = null; }
    }

    [Fact]
    public async Task Views_And_WebviewMessages_RouteToControllers()
    {
        var manifest = new Novalist.Sdk.ExtensionManifest
        {
            Id = "com.test.stub",
            Name = "Stub",
            Version = "1.0.0",
            Contributes = new Novalist.Sdk.WebContributions
            {
                Views =
                [
                    new Novalist.Sdk.WebViewContribution
                    {
                        Key = "stub.view",
                        Title = "Stub View",
                        Placement = "main",
                        // The workspace the view joins. Carried through to the
                        // interface so a contributed view lands in a mode's own
                        // list rather than at the end of a flat rail.
                        Mode = "world",
                        Entry = "web/index.html"
                    }
                ]
            }
        };
        _workspace.ExtensionsHost.Extensions.Add(new ExtensionInfo
        {
            Manifest = manifest,
            FolderPath = _root,
            Instance = new StubWebExtension(),
            IsEnabled = true,
            IsLoaded = true
        });
        var rpc = new ExtensionsRpc(_workspace);

        var views = rpc.Views();
        Assert.Single(views);
        Assert.Equal("com.test.stub/web/index.html", views[0].Entry);
        Assert.Equal("world", views[0].Mode);

        string? pushedJson = null;
        ExtensionsRpc.WebviewPosted = (id, key, json) => pushedJson = $"{id}|{key}|{json}";
        var reply = await rpc.WebviewMessageAsync("com.test.stub", "stub.view", "hello");
        Assert.Equal("echo:hello", reply);
        Assert.Contains("pushed", pushedJson);

        // Cached controller path + null replies + unknown view/extension.
        Assert.Null(await rpc.WebviewMessageAsync("com.test.stub", "stub.view", "null-reply"));
        Assert.Null(await rpc.WebviewMessageAsync("com.test.stub", "other.view", "x"));
        Assert.Null(await rpc.WebviewMessageAsync("com.missing", "stub.view", "x"));
        ExtensionsRpc.WebviewPosted = null;
    }

    private static string DeploySampleSource(string root)
    {
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(src, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(src, "extension.json"),
            """{"id":"com.novalist.sample","name":"Sample","version":"1.0.0","author":"Tests","description":"A sample","entryAssembly":"Novalist.Sdk.Example.dll"}""");
        return src;
    }

    [Fact]
    public async Task Install_Uninstall_And_SetEnabled_RoundTrip()
    {
        var extRoot = Path.Combine(_root, "exts");
        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(extRoot);
        var src = DeploySampleSource(_root);
        var rpc = new ExtensionsRpc(_workspace);

        // Install from folder.
        var afterInstall = await rpc.InstallAsync(src);
        var installed = Assert.Single(afterInstall, e => e.Id == "com.novalist.sample");
        Assert.True(installed.IsEnabled);
        Assert.Equal("Tests", installed.Author);
        Assert.Equal("A sample", installed.Description);

        // Disable then enable.
        var afterDisable = await rpc.SetEnabledAsync("com.novalist.sample", false);
        Assert.False(afterDisable.Single(e => e.Id == "com.novalist.sample").IsEnabled);
        var afterEnable = await rpc.SetEnabledAsync("com.novalist.sample", true);
        Assert.True(afterEnable.Single(e => e.Id == "com.novalist.sample").IsEnabled);

        // Uninstall.
        var afterUninstall = await rpc.UninstallAsync("com.novalist.sample");
        Assert.DoesNotContain(afterUninstall, e => e.Id == "com.novalist.sample");
        Assert.False(Directory.Exists(Path.Combine(extRoot, "com.novalist.sample")));
    }

    [Fact]
    public void Directory_ReturnsLoaderExtensionsDirectory()
    {
        var extRoot = Path.Combine(_root, "exts");
        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(extRoot);
        var rpc = new ExtensionsRpc(_workspace);
        Assert.Equal(extRoot, rpc.Directory());
    }

    [Fact]
    public void Shims_LogLocAndNotifications_Work()
    {
        Log.Debug("d");
        Log.Info("i");
        Log.Warn("w");
        Log.Error("e");
        Assert.Equal("Deutsch", Loc.Instance.GetLanguageDisplayName("de"));
        Assert.Equal("简体中文", Loc.Instance.GetLanguageDisplayName("zh-CN"));
        Assert.Equal("English", Loc.Instance.GetLanguageDisplayName("en"));
        Loc.Instance.CurrentLanguage = "de";
        Assert.Equal("de", Loc.Instance.CurrentLanguage);

        string? seen = null;
        HostNotifications.Error = m => seen = m;
        HostNotifications.Error?.Invoke("boom");
        Assert.Equal("boom", seen);
        HostNotifications.Error = null;
    }

    // ── Scripts an extension runs inside the interface ──

    /// <summary>An extension folder with a renderer plugin declared in it.</summary>
    private async Task<ExtensionsRpc> WithPluginAsync(
        string entry, int apiVersion, string? script = "novalist.log('hello')")
    {
        var folder = Path.Combine(_root, "exts", "Plugin");
        Directory.CreateDirectory(folder);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll"),
            Path.Combine(folder, "Novalist.Sdk.Example.dll"), overwrite: true);
        var manifest =
            "{\"id\":\"com.novalist.plugin\",\"name\":\"Plugin\",\"version\":\"1.0.0\","
            + "\"entryAssembly\":\"Novalist.Sdk.Example.dll\","
            + "\"contributes\":{\"renderer\":[{\"entry\":\"" + entry.Replace("\\", "/")
            + "\",\"apiVersion\":" + apiVersion + "}]}}";
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);
        if (script != null)
        {
            var scriptPath = Path.Combine(folder, "plugin.js");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, script);
        }

        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(Path.Combine(_root, "exts"));
        var rpc = new ExtensionsRpc(_workspace);
        await rpc.LoadAsync();
        return rpc;
    }

    [Fact]
    public async Task ARendererPluginComesBackAsItsSource()
    {
        // The entry and the folder, not the script itself: the interface imports
        // it as a real module over the extension protocol, and a source string
        // read here could differ from the file that actually runs.
        var rpc = await WithPluginAsync("plugin.js", ExtensionsRpc.RendererPluginApiVersion);

        var plugin = Assert.Single(rpc.RendererPlugins());
        Assert.Null(plugin.Refused);
        Assert.Equal("plugin.js", plugin.Entry);
        Assert.True(File.Exists(Path.Combine(plugin.FolderPath, plugin.Entry)));
        Assert.Equal("com.novalist.plugin", plugin.ExtensionId);
    }

    [Fact]
    public async Task APluginWrittenForAnotherApiVersionIsRefusedByName()
    {
        // Named rather than dropped: an extension that does nothing and says
        // nothing is the hardest kind of broken to report.
        var rpc = await WithPluginAsync("plugin.js", ExtensionsRpc.RendererPluginApiVersion + 1);

        var plugin = Assert.Single(rpc.RendererPlugins());
        Assert.NotNull(plugin.Refused);
        Assert.Contains("plugin API", plugin.Refused);
        Assert.Empty(plugin.Entry);
    }

    [Fact]
    public async Task APluginPointingOutsideItsOwnFolderIsRefused()
    {
        // Otherwise a manifest could name any file on the machine and have the
        // interface run it.
        var rpc = await WithPluginAsync("../../../outside.js",
            ExtensionsRpc.RendererPluginApiVersion);

        var plugin = Assert.Single(rpc.RendererPlugins());
        Assert.NotNull(plugin.Refused);
        Assert.Empty(plugin.Entry);
    }

    [Fact]
    public async Task APluginWhoseScriptIsMissingIsRefused()
    {
        var rpc = await WithPluginAsync("plugin.js",
            ExtensionsRpc.RendererPluginApiVersion, script: null);

        var plugin = Assert.Single(rpc.RendererPlugins());
        Assert.NotNull(plugin.Refused);
    }

    [Fact]
    public async Task AnExtensionThatDeclaresNoPluginContributesNone()
    {
        var extensionsDir = Path.Combine(_root, "exts", "Quiet");
        Directory.CreateDirectory(extensionsDir);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll"),
            Path.Combine(extensionsDir, "Novalist.Sdk.Example.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(extensionsDir, "extension.json"),
            """{"id":"com.novalist.quiet","name":"Quiet","version":"1.0.0","entryAssembly":"Novalist.Sdk.Example.dll"}""");

        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(Path.Combine(_root, "exts"));
        var rpc = new ExtensionsRpc(_workspace);
        await rpc.LoadAsync();

        Assert.Empty(rpc.RendererPlugins());
    }
}
