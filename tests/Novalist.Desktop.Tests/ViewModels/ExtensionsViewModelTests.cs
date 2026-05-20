using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class ExtensionsViewModelTests
{
    private static ExtensionManager BuildManager(string extRoot)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns(Task.CompletedTask);
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var mgr = new ExtensionManager(settings, host, new ExtensionLoader(extRoot));
        host.ExtensionManager = mgr;
        return mgr;
    }

    private static ExtensionInfo Info(string id, bool enabled = true)
        => new() { Manifest = new ExtensionManifest { Id = id, Name = id, Version = "1.0.0", Author = "auth", Description = "d" }, IsEnabled = enabled };

    [AvaloniaFact]
    public void Ctor_NoGallery_EmptyManager()
    {
        using var dir = new TempDir();
        var vm = new ExtensionsViewModel(BuildManager(dir.Path));
        Assert.Null(vm.Store);
        Assert.False(vm.HasExtensions);
        Assert.Empty(vm.Items);
    }

    [AvaloniaFact]
    public void Ctor_WithGallery_CreatesStore()
    {
        using var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        var vm = new ExtensionsViewModel(BuildManager(dir.Path), gallery);
        Assert.NotNull(vm.Store);
    }

    [AvaloniaFact]
    public void Refresh_BuildsItems_FromManager_WithGalleryMeta()
    {
        using var dir = new TempDir();
        var mgr = BuildManager(dir.Path);
        mgr.Extensions.Add(Info("a.one"));
        var gallery = Substitute.For<IExtensionGalleryService>();
        gallery.ReadStoreMeta("a.one").Returns(new ExtensionStoreMeta { InstalledFromGallery = true });
        var vm = new ExtensionsViewModel(mgr, gallery);
        Assert.True(vm.HasExtensions);
        Assert.Single(vm.Items);
        Assert.True(vm.Items[0].IsFromGallery);
    }

    [AvaloniaFact]
    public void SelectTab_ParsesIndex()
    {
        using var dir = new TempDir();
        var vm = new ExtensionsViewModel(BuildManager(dir.Path));
        vm.SelectTabCommand.Execute("1");
        Assert.Equal(1, vm.SelectedTab);
        vm.SelectTabCommand.Execute("x"); // unparsable -> unchanged
        Assert.Equal(1, vm.SelectedTab);
    }

    [AvaloniaFact]
    public async Task SelectTab_BrowseStore_TriggersLoad()
    {
        using var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry>());
        var vm = new ExtensionsViewModel(BuildManager(dir.Path), gallery);
        // Store.LoadAsync runs inline (all awaited mocks complete synchronously),
        // so no delay is needed and the runner thread is not bounced off the dispatcher.
        vm.SelectedTab = 1; // OnSelectedTabChanged -> Store.LoadAsync (Items empty, not loading)
        await gallery.Received().FetchGalleryIndexAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task CheckForUpdates_NoGallery_ReturnsZero()
    {
        using var dir = new TempDir();
        var vm = new ExtensionsViewModel(BuildManager(dir.Path));
        Assert.Equal(0, await vm.CheckForExtensionUpdatesAsync());
    }

    [AvaloniaFact]
    public async Task CheckForUpdates_AppliesToMatchingItems()
    {
        using var dir = new TempDir();
        var mgr = BuildManager(dir.Path);
        mgr.Extensions.Add(Info("a.one"));
        var gallery = Substitute.For<IExtensionGalleryService>();
        var entry = new GalleryEntry { Id = "a.one" };
        var rel = new GalleryRelease { Version = "2.0.0" };
        gallery.CheckForUpdatesAsync(Arg.Any<CancellationToken>()).Returns(new List<ExtensionUpdateInfo>
        {
            new() { ExtensionId = "a.one", InstalledVersion = "1.0.0", AvailableVersion = "2.0.0", Release = rel, Entry = entry }
        });
        var vm = new ExtensionsViewModel(mgr, gallery);

        var count = await vm.CheckForExtensionUpdatesAsync();
        Assert.Equal(1, count);
        Assert.Equal(1, vm.UpdateCount);
        var item = vm.Items.First(i => i.Id == "a.one");
        Assert.True(item.HasUpdate);
        Assert.Equal("2.0.0", item.AvailableVersion);
    }

    [AvaloniaFact]
    public async Task CheckForUpdates_Throws_ReturnsZero()
    {
        using var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        gallery.CheckForUpdatesAsync(Arg.Any<CancellationToken>())
               .Returns<List<ExtensionUpdateInfo>>(_ => throw new Exception("net"));
        var vm = new ExtensionsViewModel(BuildManager(dir.Path), gallery);
        Assert.Equal(0, await vm.CheckForExtensionUpdatesAsync());
    }

    // ── ExtensionItemViewModel ──────────────────────────────────────
    [AvaloniaFact]
    public void Item_ExposesManifestProps()
    {
        using var dir = new TempDir();
        var mgr = BuildManager(dir.Path);
        var info = Info("a.one");
        info.LoadError = "boom";
        var item = new ExtensionItemViewModel(info, mgr);
        Assert.Equal("a.one", item.Id);
        Assert.Equal("1.0.0", item.Version);
        Assert.Equal("auth", item.Author);
        Assert.Equal("d", item.Description);
        Assert.True(item.HasError);
        Assert.Equal("boom", item.ErrorMessage);
        Assert.False(item.IsLoaded);
        Assert.False(item.HasSettings);
        Assert.StartsWith("ext_", item.SettingsKey);
    }

    [AvaloniaFact]
    public void Item_ApplyIsEnabled_EnableThenDisable() => Task.Run(async () =>
    {
        // Task.Run contains any off-thread yield from the real manager so the
        // shared Avalonia-collection runner thread is not bounced.
        using var dir = new TempDir();
        var mgr = BuildManager(dir.Path);
        var info = Info("a.one");
        mgr.Extensions.Add(info);
        var item = new ExtensionItemViewModel(info, mgr);
        await item.ApplyIsEnabledAsync(false); // disable branch
        await item.ApplyIsEnabledAsync(true);  // enable branch (load fails fast, no throw)
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Item_ApplyIsEnabled_ManagerThrows_Caught() => Task.Run(async () =>
    {
        using var dir = new TempDir();
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns<Task>(_ => throw new Exception("save fail")); // makes DisableExtensionAsync throw
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var mgr = new ExtensionManager(settings, host, new ExtensionLoader(dir.Path));
        host.ExtensionManager = mgr;
        var info = Info("a.one");
        mgr.Extensions.Add(info);
        var item = new ExtensionItemViewModel(info, mgr);
        await item.ApplyIsEnabledAsync(false); // SaveAsync throws -> caught in ApplyIsEnabledAsync
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public async Task Item_UpdateExtension_NoReleaseOrEntry_NoOp()
    {
        using var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        var item = new ExtensionItemViewModel(Info("a.one"), BuildManager(dir.Path), gallery);
        await item.UpdateExtensionCommand.ExecuteAsync(null); // UpdateRelease/Entry null -> return
        Assert.False(item.IsUpdating);
        await gallery.DidNotReceive().DownloadExtensionZipAsync(Arg.Any<GalleryRelease>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Item_UpdateExtension_Success_FlagsRestart()
    {
        using var dir = new TempDir();
        var mgr = BuildManager(dir.Path);
        var info = Info("a.one");
        mgr.Extensions.Add(info);
        var gallery = Substitute.For<IExtensionGalleryService>();
        var rel = new GalleryRelease { Version = "2.0.0" };
        var entry = new GalleryEntry { Id = "a.one" };
        gallery.DownloadExtensionZipAsync(rel, Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("zip"));
        gallery.InstallExtensionAsync("zip", entry, rel, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var item = new ExtensionItemViewModel(info, mgr, gallery)
        {
            UpdateRelease = rel,
            UpdateEntry = entry,
            HasUpdate = true,
        };
        await item.UpdateExtensionCommand.ExecuteAsync(null);
        Assert.False(item.IsUpdating);
        Assert.True(item.NeedsRestart);
        Assert.False(item.HasUpdate);
    }
}
