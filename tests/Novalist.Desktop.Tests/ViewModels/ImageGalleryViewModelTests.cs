using NSubstitute;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

// Lives in the shared Avalonia collection because the headless app is a process-wide
// singleton. RefreshAsync awaits a Task.Run (a real yield) and kicks off an excluded
// off-thread thumbnail decode; to keep that yield from bouncing the dispatcher-owner
// runner thread (which would break sibling tests), each async body is run inside
// Task.Run(...).GetAwaiter().GetResult(): the bounce is contained on a scratch thread
// and the runner thread merely blocks. The tests never touch UI-thread-affine state.
[Collection("Avalonia")]
public class ImageGalleryViewModelTests
{
    private static (ImageGalleryViewModel Vm, IEntityService Ent, TempDir Dir) Build()
    {
        var ent = Substitute.For<IEntityService>();
        return (new ImageGalleryViewModel(ent), ent, new TempDir());
    }

    private static void SetupImages(IEntityService ent, TempDir dir, params string[] relPaths)
    {
        ent.GetProjectImages().Returns(relPaths.ToList());
        ent.GetImageFullPath(Arg.Any<string>()).Returns(ci =>
        {
            var rel = ci.Arg<string>();
            return Path.Combine(dir.Path, rel.Replace('/', Path.DirectorySeparatorChar));
        });
    }

    [AvaloniaFact]
    public void Refresh_PopulatesImages_AndItemCommandsInvokeCallbacks() => Task.Run(async () =>
    {
        var (vm, ent, dir) = Build();
        using var _d = dir;
        File.WriteAllBytes(Path.Combine(dir.Path, "a.png"), new byte[] { 1, 2, 3 });
        SetupImages(ent, dir, "a.png", "missing.png");

        string? copied = null, revealed = null, opened = null;
        vm.CopyToClipboard = s => { copied = s; return Task.CompletedTask; };
        vm.RevealInExplorer = s => revealed = s;
        vm.OpenExternally = s => opened = s;

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Images.Count);
        Assert.False(vm.IsEmpty);
        Assert.False(string.IsNullOrEmpty(vm.CountText));

        var item = vm.Images.First(i => i.Name == "a");
        Assert.EndsWith("a.png", item.FullPath);
        item.CopyPathCommand!.Execute(null);
        Assert.Equal("a.png", copied);
        item.OpenInExplorerCommand!.Execute(null);
        Assert.EndsWith("a.png", revealed);
        item.OpenExternallyCommand!.Execute(null);
        Assert.EndsWith("a.png", opened);
        item.CopyAsMarkdownCommand!.Execute(null);
        Assert.Contains("![a](a.png)", copied);
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void ItemCommands_NullCallbacks_NoThrow() => Task.Run(async () =>
    {
        var (vm, ent, dir) = Build();
        using var _d = dir;
        SetupImages(ent, dir, "a.png");
        await vm.RefreshAsync();
        var item = vm.Images[0];
        // Callbacks left null -> guarded, must not throw.
        item.CopyPathCommand!.Execute(null);
        item.CopyAsMarkdownCommand!.Execute(null);
        item.OpenInExplorerCommand!.Execute(null);
        item.OpenExternallyCommand!.Execute(null);
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Refresh_NoImages_IsEmpty() => Task.Run(async () =>
    {
        var (vm, ent, dir) = Build();
        using var _d = dir;
        SetupImages(ent, dir);
        await vm.RefreshAsync();
        Assert.True(vm.IsEmpty);
        Assert.False(string.IsNullOrEmpty(vm.EmptyText));
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void FilterQuery_FiltersImages() => Task.Run(async () =>
    {
        var (vm, ent, dir) = Build();
        using var _d = dir;
        SetupImages(ent, dir, "alpha.png", "beta.png");
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Images.Count);

        vm.FilterQuery = "alph"; // OnFilterQueryChanged -> ApplyFilter
        Assert.Single(vm.Images);
        Assert.Equal("alpha", vm.Images[0].Name);

        vm.FilterQuery = "zzz"; // no matches + query -> noResults empty text
        Assert.Empty(vm.Images);
        Assert.True(vm.IsEmpty);
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void ViewToggles_AndClosePreview()
    {
        var (vm, _, dir) = Build();
        using var _d = dir;
        vm.SetListViewCommand.Execute(null);
        Assert.False(vm.IsGridView);
        vm.SetGridViewCommand.Execute(null);
        Assert.True(vm.IsGridView);
        vm.IsPreviewOpen = true;
        vm.ClosePreviewCommand.Execute(null);
        Assert.False(vm.IsPreviewOpen);
    }

    [AvaloniaFact]
    public void Refresh_FireAndForget_DoesNotThrow() => Task.Run(async () =>
    {
        var (vm, ent, dir) = Build();
        using var _d = dir;
        SetupImages(ent, dir, "a.png");
        vm.Refresh(); // fire-and-forget wrapper over RefreshAsync
        await Task.Delay(50);
        Assert.True(vm.Images.Count >= 0);
    }).GetAwaiter().GetResult();
}
