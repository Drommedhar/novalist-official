using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

[Collection("Avalonia")]
public class ExtensionManagerTests
{
    private const string SampleId = "com.novalist.writingtoolkit";

    private static void DeploySample(string extRoot)
    {
        var folder = Path.Combine(extRoot, "Sample");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(folder, "Novalist.Sdk.Example.dll"));
        var pdb = Path.ChangeExtension(dll, ".pdb");
        if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(folder, "Novalist.Sdk.Example.pdb"));
        File.WriteAllText(Path.Combine(folder, "extension.json"),
            $$"""{ "id": "{{SampleId}}", "name": "Sample", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
    }

    private static (ExtensionManager Mgr, AppSettings Settings) Build(string extRoot)
    {
        var settings = Substitute.For<ISettingsService>();
        var app = new AppSettings();
        settings.Settings.Returns(app);
        settings.SaveAsync().Returns(Task.CompletedTask);
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var mgr = new ExtensionManager(settings, host, new ExtensionLoader(extRoot));
        host.ExtensionManager = mgr;
        return (mgr, app);
    }

    [AvaloniaFact]
    public async Task LoadAll_LoadsSample_CollectsHooks_SkipsDisabledAndBad()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        // Disabled extension.
        var dis = Path.Combine(ext.Path, "Disabled");
        Directory.CreateDirectory(dis);
        File.WriteAllText(Path.Combine(dis, "extension.json"),
            """{ "id": "ext.disabled", "name": "D", "entryAssembly": "x.dll" }""");
        // Corrupt manifest -> discovered with LoadError -> load returns false.
        var bad = Path.Combine(ext.Path, "Bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "extension.json"), "{ broken");

        var (mgr, settings) = Build(ext.Path);
        settings.Extensions["ext.disabled"] = false;

        await mgr.LoadAllAsync();

        var sample = mgr.Extensions.First(e => e.Manifest.Id == SampleId);
        Assert.True(sample.IsLoaded);
        Assert.NotEmpty(mgr.RibbonItems);       // hooks collected from the sample
        Assert.NotEmpty(mgr.SidebarPanels);
        Assert.NotEmpty(mgr.ThemeOverrides);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == "ext.disabled" && !e.IsEnabled);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == "Bad" && e.LoadError != null);
    }

    [AvaloniaFact]
    public async Task Disable_RemovesHooksAndUnloads_ThenEnableReloads()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);
        await mgr.LoadAllAsync();
        Assert.NotEmpty(mgr.RibbonItems);

        await mgr.DisableExtensionAsync(SampleId);
        var sample = mgr.Extensions.First(e => e.Manifest.Id == SampleId);
        Assert.False(sample.IsLoaded);
        Assert.Empty(mgr.RibbonItems);   // hooks removed
        Assert.Empty(mgr.SidebarPanels);

        await mgr.EnableExtensionAsync(SampleId);
        Assert.True(sample.IsLoaded);
        Assert.NotEmpty(mgr.RibbonItems); // re-collected
    }

    [AvaloniaFact]
    public async Task Disable_UnknownId_NoOp()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        await mgr.DisableExtensionAsync("nope");
        await mgr.EnableExtensionAsync("nope");
    }

    [AvaloniaFact]
    public async Task DiscoverAndEnable_LoadsNewExtension()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);

        await mgr.DiscoverAndEnableAsync(SampleId);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == SampleId && e.IsLoaded);

        // Already known -> no duplicate.
        await mgr.DiscoverAndEnableAsync(SampleId);
        Assert.Single(mgr.Extensions, e => e.Manifest.Id == SampleId);
    }

    [AvaloniaFact]
    public void Host_ExposesHostServices()
    {
        using var ext = new TempDir();
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var mgr = new ExtensionManager(settings, host, new ExtensionLoader(ext.Path));
        Assert.Same(host, mgr.Host);
    }

    [AvaloniaFact]
    public async Task DiscoverAndEnable_UnknownId_NoOp()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        await mgr.DiscoverAndEnableAsync("not.installed");
        Assert.Empty(mgr.Extensions);
    }

    private const string ThrowingId = "test.throwing";

    private static void DeployThrowing(string extRoot)
    {
        var folder = Path.Combine(extRoot, "Throwing");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.TestExtension.dll");
        File.Copy(dll, Path.Combine(folder, "Novalist.TestExtension.dll"));
        var pdb = Path.ChangeExtension(dll, ".pdb");
        if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(folder, "Novalist.TestExtension.pdb"));
        File.WriteAllText(Path.Combine(folder, "extension.json"),
            $$"""{ "id": "{{ThrowingId}}", "name": "Throwing", "entryAssembly": "Novalist.TestExtension.dll" }""");
    }

    [AvaloniaFact]
    public async Task LoadAll_InitializeThrows_RecordedAsLoadError()
    {
        using var ext = new TempDir();
        DeployThrowing(ext.Path);
        Environment.SetEnvironmentVariable("NOVALIST_TEST_THROW_INIT", "1");
        try
        {
            var (mgr, _) = Build(ext.Path);
            await mgr.LoadAllAsync();
            var info = mgr.Extensions.First(e => e.Manifest.Id == ThrowingId);
            Assert.False(info.IsLoaded);
            Assert.Contains("Initialize failed", info.LoadError);
        }
        finally { Environment.SetEnvironmentVariable("NOVALIST_TEST_THROW_INIT", null); }
    }

    [AvaloniaFact]
    public async Task ShutdownAll_ShutdownThrows_Swallowed()
    {
        using var ext = new TempDir();
        DeployThrowing(ext.Path);
        Environment.SetEnvironmentVariable("NOVALIST_TEST_THROW_SHUTDOWN", "1");
        try
        {
            var (mgr, _) = Build(ext.Path);
            await mgr.LoadAllAsync(); // initializes fine (init env off)
            mgr.ShutdownAll();        // Shutdown throws -> swallowed
            Assert.All(mgr.Extensions, e => Assert.False(e.IsLoaded));
        }
        finally { Environment.SetEnvironmentVariable("NOVALIST_TEST_THROW_SHUTDOWN", null); }
    }

    [AvaloniaFact]
    public async Task Disable_LoadedExtensionWithoutCollectedHooks_NoOpOnRemove()
    {
        using var ext = new TempDir();
        var (mgr, settings) = Build(ext.Path);
        // Inject a "loaded" extension that never went through CollectHooks (no undo entry).
        mgr.Extensions.Add(new ExtensionInfo
        {
            Manifest = new ExtensionManifest { Id = "manual" },
            Instance = new Novalist.TestExtension.ThrowingExtension(),
            IsLoaded = true
        });
        await mgr.DisableExtensionAsync("manual"); // RemoveHooks finds no undo -> returns
        Assert.False(mgr.Extensions.First(e => e.Manifest.Id == "manual").IsLoaded);
    }

    [AvaloniaFact]
    public async Task ShutdownAll_UnloadsLoadedExtensions()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);
        await mgr.LoadAllAsync();
        Assert.True(mgr.Extensions.First().IsLoaded);

        mgr.ShutdownAll();
        Assert.All(mgr.Extensions, e => Assert.False(e.IsLoaded));
        Assert.Empty(mgr.RibbonItems);
    }
}
