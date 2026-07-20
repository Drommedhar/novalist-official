using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Backend.Extensions;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Backend.Tests;

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

    [Fact]
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
        Assert.NotEmpty(mgr.ThemeOverrides);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == "ext.disabled" && !e.IsEnabled);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == "Bad" && e.LoadError != null);
    }

    [Fact]
    public async Task LoadAllAsync_CalledTwice_DoesNotDuplicate()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);

        await mgr.LoadAllAsync();
        var firstCount = mgr.Extensions.Count(e => e.Manifest.Id == SampleId);
        // A reconnect / re-hydrate re-invokes extensions/load -> LoadAllAsync again.
        await mgr.LoadAllAsync();

        Assert.Equal(1, firstCount);
        Assert.Equal(1, mgr.Extensions.Count(e => e.Manifest.Id == SampleId));
    }

    [Fact]
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

        await mgr.EnableExtensionAsync(SampleId);
        Assert.True(sample.IsLoaded);
        Assert.NotEmpty(mgr.RibbonItems); // re-collected
    }

    [Fact]
    public async Task Disable_UnknownId_NoOp()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        await mgr.DisableExtensionAsync("nope");
        await mgr.EnableExtensionAsync("nope");
    }

    [Fact]
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

    [Fact]
    public async Task Reload_LoadedExtension_UnloadsAndReloadsFromDisk()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);
        await mgr.LoadAllAsync();
        Assert.True(mgr.Extensions.Single(e => e.Manifest.Id == SampleId).IsLoaded);

        await mgr.ReloadExtensionAsync(SampleId);

        var sample = mgr.Extensions.Single(e => e.Manifest.Id == SampleId);
        Assert.True(sample.IsLoaded);
        Assert.NotEmpty(mgr.RibbonItems);
    }

    [Fact]
    public async Task Reload_NotYetKnown_DiscoversAndLoads()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, _) = Build(ext.Path);
        // Never loaded, not in the collection: the existing==null path runs.
        await mgr.ReloadExtensionAsync(SampleId);
        Assert.Contains(mgr.Extensions, e => e.Manifest.Id == SampleId && e.IsLoaded);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    // Builds a source folder (outside the extensions dir) that install-from-folder
    // copies in. Includes a nested "Locales" folder so CopyDirectory recursion runs.
    private static string BuildSourceFolder(string root)
    {
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(src, "Novalist.Sdk.Example.dll"));
        var pdb = Path.ChangeExtension(dll, ".pdb");
        if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(src, "Novalist.Sdk.Example.pdb"));
        File.WriteAllText(Path.Combine(src, "extension.json"),
            $$"""{ "id": "{{SampleId}}", "name": "Sample", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
        var locales = Path.Combine(src, "Locales");
        Directory.CreateDirectory(locales);
        File.WriteAllText(Path.Combine(locales, "en.json"), "{}");
        return src;
    }

    [Fact]
    public async Task InstallFromFolder_CopiesEnablesAndLoads()
    {
        using var ext = new TempDir();
        using var work = new TempDir();
        var src = BuildSourceFolder(work.Path);
        var (mgr, settings) = Build(ext.Path);

        var id = await mgr.InstallFromFolderAsync(src);

        Assert.Equal(SampleId, id);
        var info = mgr.Extensions.Single(e => e.Manifest.Id == SampleId);
        Assert.True(info.IsLoaded);
        // Files were copied into the extensions dir (including the nested folder).
        Assert.True(File.Exists(Path.Combine(ext.Path, SampleId, "extension.json")));
        Assert.True(File.Exists(Path.Combine(ext.Path, SampleId, "Locales", "en.json")));
        Assert.True(settings.Extensions[SampleId]);

        // Reinstall over the top replaces cleanly (still a single entry, still loaded).
        var again = await mgr.InstallFromFolderAsync(src);
        Assert.Equal(SampleId, again);
        Assert.Single(mgr.Extensions, e => e.Manifest.Id == SampleId);
        Assert.True(mgr.Extensions.Single(e => e.Manifest.Id == SampleId).IsLoaded);
    }

    [Fact]
    public async Task InstallFromFolder_MissingFolder_Throws()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => mgr.InstallFromFolderAsync(Path.Combine(ext.Path, "does-not-exist")));
    }

    [Fact]
    public async Task InstallFromFolder_NoManifest_Throws()
    {
        using var ext = new TempDir();
        using var work = new TempDir();
        var (mgr, _) = Build(ext.Path);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.InstallFromFolderAsync(work.Path));
        Assert.Contains("extension.json", ex.Message);
    }

    [Fact]
    public async Task InstallFromFolder_CorruptManifest_Throws()
    {
        using var ext = new TempDir();
        using var work = new TempDir();
        File.WriteAllText(Path.Combine(work.Path, "extension.json"), "{ not json");
        var (mgr, _) = Build(ext.Path);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.InstallFromFolderAsync(work.Path));
        Assert.Contains("could not be parsed", ex.Message);
    }

    [Fact]
    public async Task InstallFromFolder_ManifestWithoutId_Throws()
    {
        using var ext = new TempDir();
        using var work = new TempDir();
        File.WriteAllText(Path.Combine(work.Path, "extension.json"), """{ "name": "No Id" }""");
        var (mgr, _) = Build(ext.Path);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.InstallFromFolderAsync(work.Path));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public async Task Uninstall_UnloadsDeletesFolderAndDropsSetting()
    {
        using var ext = new TempDir();
        DeploySample(ext.Path);
        var (mgr, settings) = Build(ext.Path);
        await mgr.LoadAllAsync();
        var folder = mgr.Extensions.Single(e => e.Manifest.Id == SampleId).FolderPath;
        Assert.True(Directory.Exists(folder));

        await mgr.UninstallAsync(SampleId);

        Assert.DoesNotContain(mgr.Extensions, e => e.Manifest.Id == SampleId);
        Assert.False(Directory.Exists(folder));
        Assert.False(settings.Extensions.ContainsKey(SampleId));
        Assert.Empty(mgr.RibbonItems);
    }

    [Fact]
    public async Task Uninstall_UnknownId_NoOp()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        await mgr.UninstallAsync("not.installed"); // RemoveInstalledAsync early-returns
        Assert.Empty(mgr.Extensions);
    }

    [Fact]
    public async Task Uninstall_DiscoveredButFolderAlreadyGone_NoDeleteError()
    {
        using var ext = new TempDir();
        var (mgr, _) = Build(ext.Path);
        // A discovered-but-unloaded entry whose FolderPath no longer exists exercises
        // the Directory.Exists=false branch of RemoveInstalledAsync.
        mgr.Extensions.Add(new ExtensionInfo
        {
            Manifest = new ExtensionManifest { Id = "ghost" },
            FolderPath = Path.Combine(ext.Path, "gone")
        });
        await mgr.UninstallAsync("ghost");
        Assert.DoesNotContain(mgr.Extensions, e => e.Manifest.Id == "ghost");
    }
}
