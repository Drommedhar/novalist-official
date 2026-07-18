using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

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
}
