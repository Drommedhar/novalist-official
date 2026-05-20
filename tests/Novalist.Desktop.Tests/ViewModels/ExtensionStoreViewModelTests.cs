using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class ExtensionStoreViewModelTests
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

    private static (ExtensionStoreViewModel Vm, IExtensionGalleryService Gallery, TempDir Dir) Build()
    {
        var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry>());
        var vm = new ExtensionStoreViewModel(gallery, BuildManager(dir.Path));
        return (vm, gallery, dir);
    }

    private static GalleryEntry Entry(string id, string name = "", params string[] tags)
        => new() { Id = id, Name = name == "" ? id : name, Description = "desc " + id, Author = "auth", Repo = "owner/" + id, Tags = tags.ToList() };

    private static GalleryRelease Rel(string version, string body = "notes") => new() { Version = version, Body = body, TagName = "v" + version };

    [AvaloniaFact]
    public async Task Load_BuildsItems_InstalledUpdateCompatible()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var e1 = Entry("a.one", "One");
        var e2 = Entry("a.two", "Two");
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry> { e1, e2 });
        gallery.GetLatestCompatibleReleaseAsync(e1, Arg.Any<CancellationToken>()).Returns(Rel("2.0.0"));
        gallery.GetLatestCompatibleReleaseAsync(e2, Arg.Any<CancellationToken>()).Returns((GalleryRelease?)null); // incompatible
        gallery.ReadStoreMeta("a.one").Returns(new ExtensionStoreMeta { InstalledFromGallery = true, InstalledVersion = "1.0.0" });
        gallery.ReadStoreMeta("a.two").Returns((ExtensionStoreMeta?)null);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
        Assert.Equal(2, vm.Items.Count);
        var one = vm.Items.First(i => i.Id == "a.one");
        Assert.True(one.IsInstalled);
        Assert.True(one.HasUpdate); // 2.0.0 > 1.0.0
        Assert.True(one.IsCompatible);
        var two = vm.Items.First(i => i.Id == "a.two");
        Assert.False(two.IsCompatible); // no release
        Assert.False(vm.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Load_NoUpdate_WhenRemoteOlderOrEqual()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var older = Entry("a.older");
        var equal = Entry("a.equal");
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry> { older, equal });
        gallery.GetLatestCompatibleReleaseAsync(older, Arg.Any<CancellationToken>()).Returns(Rel("1.0.0")); // remote older
        gallery.GetLatestCompatibleReleaseAsync(equal, Arg.Any<CancellationToken>()).Returns(Rel("2.0.0")); // equal
        gallery.ReadStoreMeta("a.older").Returns(new ExtensionStoreMeta { InstalledFromGallery = true, InstalledVersion = "2.0.0" });
        gallery.ReadStoreMeta("a.equal").Returns(new ExtensionStoreMeta { InstalledFromGallery = true, InstalledVersion = "2.0.0" });

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.Items.First(i => i.Id == "a.older").HasUpdate); // 1.0.0 < 2.0.0
        Assert.False(vm.Items.First(i => i.Id == "a.equal").HasUpdate); // 2.0.0 == 2.0.0
    }

    [AvaloniaFact]
    public async Task Load_Empty_SetsIsEmpty()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Load_FetchThrows_SetsError()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>())
               .Returns<List<GalleryEntry>>(_ => throw new InvalidOperationException("net down"));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal("net down", vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Load_ReleaseFetchThrows_PerEntry_Swallowed()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var e1 = Entry("a.one");
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry> { e1 });
        gallery.GetLatestCompatibleReleaseAsync(e1, Arg.Any<CancellationToken>())
               .Returns<GalleryRelease?>(_ => throw new Exception("rel fail"));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Items);
        Assert.False(vm.Items[0].IsCompatible); // release null after swallow
    }

    [AvaloniaFact]
    public async Task Load_Cancelled_Swallowed()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>())
               .Returns<List<GalleryEntry>>(_ => throw new OperationCanceledException());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasError); // OperationCanceledException is swallowed
    }

    private static (ExtensionStoreViewModel Vm, IExtensionGalleryService Gallery, ExtensionManager Mgr, TempDir Dir) BuildWithManager()
    {
        var dir = new TempDir();
        var gallery = Substitute.For<IExtensionGalleryService>();
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry>());
        var mgr = BuildManager(dir.Path);
        var vm = new ExtensionStoreViewModel(gallery, mgr);
        return (vm, gallery, mgr, dir);
    }

    [AvaloniaFact]
    public async Task Uninstall_LoadedExtension_DisablesFirst()
    {
        var (vm, gallery, mgr, dir) = BuildWithManager();
        using var _d = dir;
        mgr.Extensions.Add(new ExtensionInfo { Manifest = new Novalist.Sdk.ExtensionManifest { Id = "a.one" } });
        var item = new StoreExtensionItemViewModel(Entry("a.one"), Rel("1.0.0"), vm) { IsInstalled = true };
        gallery.UninstallExtensionAsync("a.one", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await vm.UninstallCommand.ExecuteAsync(item);
        Assert.False(item.IsInstalled);
    }

    [AvaloniaFact]
    public async Task Update_LoadedExtension_DisablesFirst_InstallsRestart()
    {
        var (vm, gallery, mgr, dir) = BuildWithManager();
        using var _d = dir;
        mgr.Extensions.Add(new ExtensionInfo { Manifest = new Novalist.Sdk.ExtensionManifest { Id = "a.one" } });
        var rel = Rel("2.0.0");
        var item = new StoreExtensionItemViewModel(Entry("a.one"), rel, vm);
        gallery.DownloadExtensionZipAsync(rel, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("z"));
        gallery.InstallExtensionAsync("z", item.Entry, rel, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        gallery.ReadStoreMeta("a.one").Returns((ExtensionStoreMeta?)null);
        await vm.UpdateCommand.ExecuteAsync(item);
        Assert.True(item.IsInstalled);
        Assert.True(item.NeedsRestart);
    }

    [AvaloniaFact]
    public async Task Filter_BySearchTerms()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var e1 = Entry("a.one", "Alpha", "writing");
        var e2 = Entry("a.two", "Beta");
        gallery.FetchGalleryIndexAsync(Arg.Any<CancellationToken>()).Returns(new List<GalleryEntry> { e1, e2 });
        gallery.GetLatestCompatibleReleaseAsync(Arg.Any<GalleryEntry>(), Arg.Any<CancellationToken>()).Returns(Rel("1.0.0"));
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchText = "alpha";
        Assert.True(vm.Items.First(i => i.Id == "a.one").IsVisible);
        Assert.False(vm.Items.First(i => i.Id == "a.two").IsVisible);

        vm.SearchText = "writing"; // tag match
        Assert.True(vm.Items.First(i => i.Id == "a.one").IsVisible);

        vm.SearchText = ""; // all visible
        Assert.All(vm.Items, i => Assert.True(i.IsVisible));
    }

    [AvaloniaFact]
    public async Task ShowDetail_LoadsReadme_BuildsHtml_ThenHide()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        gallery.FetchReadmeAsync("owner/a.one", Arg.Any<CancellationToken>()).Returns("# Title\n\nbody");
        var item = new StoreExtensionItemViewModel(Entry("a.one"), Rel("1.0.0", "rel body"), vm);

        await vm.ShowDetailCommand.ExecuteAsync(item);
        Assert.True(vm.IsDetailVisible);
        Assert.False(vm.IsDetailLoading);
        Assert.Contains("Title", vm.DetailHtml);
        Assert.Contains("rel body", vm.DetailHtml);

        vm.HideDetailCommand.Execute(null);
        Assert.False(vm.IsDetailVisible);
        Assert.Null(vm.SelectedItem);
        Assert.Equal(string.Empty, vm.DetailHtml);
    }

    [AvaloniaFact]
    public async Task ShowDetail_ReadmeThrows_Fallback()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        gallery.FetchReadmeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<string>(_ => throw new Exception("no readme"));
        var item = new StoreExtensionItemViewModel(Entry("a.one"), null, vm);
        await vm.ShowDetailCommand.ExecuteAsync(item);
        Assert.Contains("Could not load README", vm.DetailReadme);
    }

    [AvaloniaFact]
    public async Task Install_NullRelease_NoOp()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var item = new StoreExtensionItemViewModel(Entry("a.one"), null, vm);
        await vm.InstallCommand.ExecuteAsync(item);
        await gallery.DidNotReceive().DownloadExtensionZipAsync(Arg.Any<GalleryRelease>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Install_Success_ReportsProgress_RaisesEvent()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var rel = Rel("1.0.0");
        var item = new StoreExtensionItemViewModel(Entry("a.one"), rel, vm);
        gallery.DownloadExtensionZipAsync(rel, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(ci => { ci.Arg<IProgress<double>>()?.Report(0.5); return Task.FromResult("zip.path"); });
        gallery.InstallExtensionAsync("zip.path", item.Entry, rel, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        gallery.ReadStoreMeta("a.one").Returns(new ExtensionStoreMeta { InstalledFromGallery = true }); // -> InstallDependenciesAsync (early returns)

        string? installedId = null;
        vm.ExtensionInstalled += (_, id) => installedId = id;

        await vm.InstallCommand.ExecuteAsync(item);

        Assert.True(item.IsInstalled);
        Assert.Equal("1.0.0", item.InstalledVersion);
        Assert.False(item.HasUpdate);
        Assert.False(item.IsInstalling);
        Assert.Equal("a.one", installedId);
    }

    [AvaloniaFact]
    public async Task Install_Failure_SetsError()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var rel = Rel("1.0.0");
        var item = new StoreExtensionItemViewModel(Entry("a.one"), rel, vm);
        gallery.DownloadExtensionZipAsync(rel, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns<string>(_ => throw new Exception("download fail"));
        await vm.InstallCommand.ExecuteAsync(item);
        Assert.Equal("download fail", item.InstallError);
        Assert.False(item.IsInstalling);
    }

    [AvaloniaFact]
    public async Task Uninstall_NotLoaded_RemovesAndResets()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var item = new StoreExtensionItemViewModel(Entry("a.one"), Rel("1.0.0"), vm) { IsInstalled = true, InstalledVersion = "1.0.0" };
        gallery.UninstallExtensionAsync("a.one", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await vm.UninstallCommand.ExecuteAsync(item);
        Assert.False(item.IsInstalled);
        Assert.Null(item.InstalledVersion);
        await gallery.Received().UninstallExtensionAsync("a.one", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Uninstall_Failure_SetsError()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var item = new StoreExtensionItemViewModel(Entry("a.one"), Rel("1.0.0"), vm) { IsInstalled = true };
        gallery.UninstallExtensionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task>(_ => throw new Exception("rm fail"));
        await vm.UninstallCommand.ExecuteAsync(item);
        Assert.Equal("rm fail", item.InstallError);
    }

    [AvaloniaFact]
    public async Task Update_NullRelease_NoOp_Else_InstallsAndFlagsRestart()
    {
        var (vm, gallery, dir) = Build();
        using var _d = dir;
        var none = new StoreExtensionItemViewModel(Entry("a.none"), null, vm);
        await vm.UpdateCommand.ExecuteAsync(none); // null release -> no-op
        Assert.False(none.NeedsRestart);

        var rel = Rel("2.0.0");
        var item = new StoreExtensionItemViewModel(Entry("a.one"), rel, vm);
        gallery.DownloadExtensionZipAsync(rel, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("z"));
        gallery.InstallExtensionAsync("z", item.Entry, rel, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        gallery.ReadStoreMeta("a.one").Returns((ExtensionStoreMeta?)null);
        await vm.UpdateCommand.ExecuteAsync(item);
        Assert.True(item.IsInstalled);
        Assert.True(item.NeedsRestart);
    }

    [AvaloniaFact]
    public void Item_StatusText_AllStates()
    {
        var item = new StoreExtensionItemViewModel(Entry("a.one"), Rel("2.0.0"), null!);
        Assert.Equal("Install", item.StatusText);
        item.IsInstalled = true;
        Assert.Equal("Installed", item.StatusText);
        item.HasUpdate = true;
        Assert.StartsWith("Update to", item.StatusText);
        item.IsCompatible = false;
        Assert.Equal("Incompatible", item.StatusText);
        item.NeedsRestart = true;
        Assert.False(string.IsNullOrEmpty(item.StatusText)); // restart label

        Assert.Equal("2.0.0", item.LatestVersion);
        Assert.Equal("auth", item.Author);
        Assert.Equal("owner/a.one", item.Repo);
    }

    [AvaloniaFact]
    public void Item_NullRelease_Defaults()
    {
        var item = new StoreExtensionItemViewModel(Entry("a.one"), null, null!);
        Assert.Equal("—", item.LatestVersion);
        Assert.Equal(string.Empty, item.ReleaseBody);
        Assert.Null(item.Icon);
    }
}
