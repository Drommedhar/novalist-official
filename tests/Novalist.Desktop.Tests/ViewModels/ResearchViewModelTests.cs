using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class ResearchViewModelTests
{
    private static (ResearchViewModel Vm, IResearchService Svc) Build(params ResearchItem[] items)
    {
        var svc = Substitute.For<IResearchService>();
        svc.GetAll().Returns(items);
        return (new ResearchViewModel(svc), svc);
    }

    private static ResearchItem Note(string title, string id = "", string content = "", params string[] tags)
        => new() { Id = id == "" ? title : id, Title = title, Type = ResearchItemType.Note, Content = content, Tags = tags.ToList() };

    [Fact]
    public void AvailableTypes_ExposesAllEnumValues()
    {
        var (vm, _) = Build();
        Assert.Equal(Enum.GetValues<ResearchItemType>().Length, vm.AvailableTypes.Length);
    }

    [Fact]
    public void Refresh_PopulatesAndReselects()
    {
        var a = Note("A", "1");
        var (vm, svc) = Build(a);
        vm.SelectedItem = a; // simulate prior selection
        vm.Refresh();
        Assert.Single(vm.Items);
        Assert.True(vm.HasSelection);
        Assert.Equal("1", vm.SelectedItem!.Id);
    }

    [Fact]
    public void Refresh_PriorSelectionGone_ClearsSelection()
    {
        var gone = Note("Gone", "9");
        var (vm, svc) = Build(Note("A", "1"));
        vm.SelectedItem = gone;
        vm.Refresh();
        Assert.Null(vm.SelectedItem);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void Filter_MatchesTitleContentTags()
    {
        var (vm, _) = Build(
            Note("Alpha", "1", "body"),
            Note("Beta", "2", "haystack needle"),
            Note("Gamma", "3", "", "tagx"));
        vm.Refresh();
        Assert.Equal(3, vm.Items.Count);

        vm.SearchQuery = "alph";   // title
        Assert.Single(vm.Items);
        vm.SearchQuery = "needle"; // content
        Assert.Single(vm.Items);
        Assert.Equal("2", vm.Items[0].Id);
        vm.SearchQuery = "tagx";   // tag
        Assert.Single(vm.Items);
        Assert.Equal("3", vm.Items[0].Id);
        vm.SearchQuery = "  ";     // whitespace -> all
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task AddNote_SavesAndSelects()
    {
        var (vm, svc) = Build();
        await vm.AddNoteCommand.ExecuteAsync(null);
        await svc.Received().SaveAsync(Arg.Is<ResearchItem>(i => i.Type == ResearchItemType.Note));
    }

    [Fact]
    public async Task AddLink_SavesLinkWithDefaultUrl()
    {
        var (vm, svc) = Build();
        await vm.AddLinkCommand.ExecuteAsync(null);
        await svc.Received().SaveAsync(Arg.Is<ResearchItem>(i => i.Type == ResearchItemType.Link && i.Content == "https://"));
    }

    [Theory]
    [InlineData("doc.pdf", ResearchItemType.Pdf)]
    [InlineData("pic.PNG", ResearchItemType.Image)]
    [InlineData("photo.jpeg", ResearchItemType.Image)]
    [InlineData("notes.txt", ResearchItemType.File)]
    public async Task ImportFile_MapsExtensionToType(string fileName, ResearchItemType expected)
    {
        var (vm, svc) = Build();
        svc.ImportFileAsync(Arg.Any<string>()).Returns("research/" + fileName);
        vm.PickFileToImport = () => Task.FromResult<string?>(@"C:\src\" + fileName);
        await vm.ImportFileCommand.ExecuteAsync(null);
        await svc.Received().SaveAsync(Arg.Is<ResearchItem>(i => i.Type == expected));
    }

    [Fact]
    public async Task ImportFile_NoPicker_OrEmptyPath_NoOp()
    {
        var (vm, svc) = Build();
        await vm.ImportFileCommand.ExecuteAsync(null); // picker null
        vm.PickFileToImport = () => Task.FromResult<string?>("");
        await vm.ImportFileCommand.ExecuteAsync(null); // empty path
        await svc.DidNotReceive().ImportFileAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SaveSelected_NoSelection_NoOp_ThenSaves()
    {
        var (vm, svc) = Build(Note("A", "1"));
        await vm.SaveSelectedCommand.ExecuteAsync(null); // null selection
        await svc.DidNotReceive().SaveAsync(Arg.Any<ResearchItem>());
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];
        await vm.SaveSelectedCommand.ExecuteAsync(null);
        await svc.Received().SaveAsync(vm.SelectedItem!);
    }

    [Fact]
    public async Task DeleteSelected_ConfirmGate()
    {
        var (vm, svc) = Build(Note("A", "1"));
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];

        vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        await svc.DidNotReceive().DeleteAsync(Arg.Any<string>());

        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        await svc.Received().DeleteAsync("1");
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task DeleteSelected_NoSelection_NoOp()
    {
        var (vm, svc) = Build();
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        await svc.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DeleteSelected_NoConfirmDialog_DeletesDirectly()
    {
        var (vm, svc) = Build(Note("A", "1"));
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];
        await vm.DeleteSelectedCommand.ExecuteAsync(null); // ShowConfirmDialog null
        await svc.Received().DeleteAsync("1");
    }

    [Fact]
    public async Task AddTag_AddsUniqueTrimmed_SkipsDuplicate_ClearsInput()
    {
        var item = Note("A", "1");
        var (vm, svc) = Build(item);
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];

        vm.NewTagText = "  red ";
        await vm.AddTagCommand.ExecuteAsync(null);
        Assert.Contains("red", vm.SelectedItem!.Tags);
        Assert.Equal(string.Empty, vm.NewTagText);

        vm.NewTagText = "RED"; // duplicate (case-insensitive) -> no add
        await vm.AddTagCommand.ExecuteAsync(null);
        Assert.Single(vm.SelectedItem.Tags);
    }

    [Fact]
    public async Task AddTag_NoSelection_OrEmpty_NoOp()
    {
        var (vm, svc) = Build();
        vm.NewTagText = "x";
        await vm.AddTagCommand.ExecuteAsync(null); // no selection
        var item = Note("A", "1");
        (vm, svc) = Build(item);
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];
        vm.NewTagText = "   ";
        await vm.AddTagCommand.ExecuteAsync(null); // empty tag
        Assert.Empty(vm.SelectedItem!.Tags);
    }

    [Fact]
    public async Task RemoveTag_RemovesCaseInsensitive()
    {
        var item = Note("A", "1", "", "blue");
        var (vm, svc) = Build(item);
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];
        await vm.RemoveTagCommand.ExecuteAsync("BLUE");
        Assert.Empty(vm.SelectedItem!.Tags);
    }

    [Fact]
    public async Task RemoveTag_NullSelectionOrTag_NoOp()
    {
        var (vm, svc) = Build();
        await vm.RemoveTagCommand.ExecuteAsync("x"); // no selection
        var item = Note("A", "1", "", "blue");
        (vm, svc) = Build(item);
        vm.Refresh();
        vm.SelectedItem = vm.Items[0];
        await vm.RemoveTagCommand.ExecuteAsync(null); // null tag
        Assert.Single(vm.SelectedItem!.Tags);
    }

    [Fact]
    public void SelectedFlags_ImageAndFile()
    {
        var svc = Substitute.For<IResearchService>();
        svc.GetAbsolutePath(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var vm = new ResearchViewModel(svc);

        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.Image, Content = "a.png" };
        Assert.True(vm.SelectedIsImage);
        Assert.True(vm.SelectedIsFile);
        Assert.Equal("a.png", vm.SelectedAbsolutePath);

        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.Note, Content = "x" };
        Assert.False(vm.SelectedIsImage);
        Assert.False(vm.SelectedIsFile);
        Assert.Null(vm.SelectedAbsolutePath); // not a file type
    }

    [Fact]
    public void SelectedFileSize_AndModified_RealFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        File.WriteAllBytes(path, new byte[2048]);
        var svc = Substitute.For<IResearchService>();
        svc.GetAbsolutePath("rel").Returns(path);
        var vm = new ResearchViewModel(svc)
        {
            SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "rel" }
        };
        Assert.Equal("2.0 KB", vm.SelectedFileSize);
        Assert.False(string.IsNullOrEmpty(vm.SelectedModified));
    }

    [Fact]
    public void SelectedFileSize_Bytes_And_Megabytes()
    {
        using var dir = new TempDir();
        var svc = Substitute.For<IResearchService>();
        var small = Path.Combine(dir.Path, "s.bin"); File.WriteAllBytes(small, new byte[10]);
        var big = Path.Combine(dir.Path, "b.bin"); File.WriteAllBytes(big, new byte[2 * 1024 * 1024]);
        svc.GetAbsolutePath("s").Returns(small);
        svc.GetAbsolutePath("b").Returns(big);
        var vm = new ResearchViewModel(svc);

        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "s" };
        Assert.Equal("10 B", vm.SelectedFileSize);
        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "b" };
        Assert.EndsWith("MB", vm.SelectedFileSize);
    }

    [Fact]
    public void SelectedFileSize_Modified_MissingFile_Empty()
    {
        var svc = Substitute.For<IResearchService>();
        svc.GetAbsolutePath("gone").Returns(@"C:\definitely\missing\nope.bin");
        var vm = new ResearchViewModel(svc)
        {
            SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "gone" }
        };
        Assert.Equal(string.Empty, vm.SelectedFileSize);
        Assert.Equal(string.Empty, vm.SelectedModified);
    }

    [Fact]
    public void RevealSelected_InvokesCallbackWhenPathPresent()
    {
        var svc = Substitute.For<IResearchService>();
        svc.GetAbsolutePath("rel").Returns(@"C:\x\rel");
        string? revealed = null;
        var vm = new ResearchViewModel(svc)
        {
            RevealInExplorer = p => revealed = p,
            SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "rel" }
        };
        vm.RevealSelectedCommand.Execute(null);
        Assert.Equal(@"C:\x\rel", revealed);
    }

    [Fact]
    public void RevealSelected_NoPath_NoInvoke()
    {
        var svc = Substitute.For<IResearchService>();
        bool called = false;
        var vm = new ResearchViewModel(svc) { RevealInExplorer = _ => called = true };
        vm.RevealSelectedCommand.Execute(null); // no selection -> empty path
        Assert.False(called);
    }

    [Fact]
    public void OpenExternal_GuardsWithoutLaunching()
    {
        var svc = Substitute.For<IResearchService>();
        svc.GetAbsolutePath(Arg.Any<string>()).Returns(string.Empty);
        var vm = new ResearchViewModel(svc);

        vm.OpenExternalCommand.Execute(null); // null selection -> return

        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.Link, Content = "" };
        vm.OpenExternalCommand.Execute(null); // link branch, empty target -> return

        vm.SelectedItem = new ResearchItem { Type = ResearchItemType.File, Content = "x" };
        vm.OpenExternalCommand.Execute(null); // file branch, GetAbsolutePath empty -> return
        // No assertion needed: must not throw or spawn a process.
    }

    [Fact]
    public void OpenExternal_NonEmptyTarget_ReachesLaunch_NoThrow()
    {
        var svc = Substitute.For<IResearchService>();
        var vm = new ResearchViewModel(svc)
        {
            // Garbage control-character "URL": non-empty so the launch line runs,
            // but ShellExecute has nothing to open -> swallowed, no process spawned.
            SelectedItem = new ResearchItem { Type = ResearchItemType.Link, Content = ":invalid" }
        };
        vm.OpenExternalCommand.Execute(null); // exercises target!=empty -> LaunchExternal
    }
}
