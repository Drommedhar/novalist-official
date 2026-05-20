using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class ExportViewModelTests
{
    private static (ExportViewModel Vm, IProjectService Proj) Build(params ChapterData[] chapters)
    {
        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns(chapters.ToList());
        proj.CurrentProject.Returns((ProjectMetadata?)null);
        return (new ExportViewModel(proj), proj);
    }

    private static ChapterData Chap(string guid, string title, int order)
        => new() { Guid = guid, Title = title, Order = order };

    [Fact]
    public void Refresh_BuildsChapters_DefaultTitle_FormatNames()
    {
        var (vm, _) = Build(Chap("g1", "One", 1), Chap("g2", "Two", 2));
        vm.Refresh();
        Assert.Equal(2, vm.Chapters.Count);
        Assert.Equal("1. One", vm.Chapters[0].DisplayName);
        Assert.Equal("My Novel", vm.Title); // CurrentProject null -> default
        Assert.Equal(7, vm.FormatNames.Count); // 7 built-ins
        Assert.Equal(2, vm.SelectedCount);
    }

    [Fact]
    public void Refresh_UsesProjectName_WhenAvailable()
    {
        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        proj.CurrentProject.Returns(new ProjectMetadata { Name = "Saga" });
        var vm = new ExportViewModel(proj);
        vm.Refresh();
        Assert.Equal("Saga", vm.Title);
    }

    [Fact]
    public void SelectAll_SelectNone_UpdateCount()
    {
        var (vm, _) = Build(Chap("g1", "One", 1), Chap("g2", "Two", 2));
        vm.Refresh();
        vm.SelectNoneCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(string.IsNullOrEmpty(vm.SelectedCountText));
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
    }

    [Theory]
    [InlineData(0, false)] // EPUB
    [InlineData(1, true)]  // DOCX
    [InlineData(2, true)]  // PDF
    [InlineData(3, false)] // Markdown
    public void SelectedFormatIndex_TogglesSmfVisibility(int index, bool smfVisible)
    {
        var (vm, _) = Build();
        vm.SelectedFormatIndex = index == 0 ? 1 : 0; // ensure the next assignment is a real change
        vm.SmfPreset = true;
        vm.SelectedFormatIndex = index;
        Assert.Equal(smfVisible, vm.IsSmfVisible);
        if (!smfVisible) Assert.False(vm.SmfPreset); // reset when hidden
    }

    [Fact]
    public void CodexFormat_HidesChapters()
    {
        var (vm, _) = Build();
        vm.SelectedFormatIndex = 6;
        Assert.True(vm.IsCodexFormat);
        Assert.False(vm.ChaptersVisible);
    }

    [Fact]
    public void PresetId_SyncsSmfPresetBool()
    {
        var (vm, _) = Build();
        vm.SelectedPresetId = ExportPresets.ShunnId;
        Assert.True(vm.SmfPreset);
        vm.SelectedPresetId = ExportPresets.DefaultId;
        Assert.False(vm.SmfPreset);
    }

    [Fact]
    public void AvailablePresets_Exposed()
    {
        var (vm, _) = Build();
        Assert.Same(ExportPresets.All, vm.AvailablePresets);
    }

    [Fact]
    public void LoadExtensionFormats_AppendsToFormatNames()
    {
        var (vm, _) = Build();
        vm.LoadExtensionFormats([new ExportFormatDescriptor { DisplayName = "MyFmt", FileExtension = ".x" }]);
        Assert.Equal(8, vm.FormatNames.Count);
        Assert.Equal("MyFmt", vm.FormatNames[7]);
        Assert.Single(vm.ExtensionFormats);
    }

    [Fact]
    public async Task Export_NoSaveDialog_NoOp()
    {
        var (vm, _) = Build(Chap("g1", "One", 1));
        vm.Refresh();
        await vm.ExportCommand.ExecuteAsync(null); // ShowSaveFileDialog null
        Assert.False(vm.IsExporting);
    }

    [Fact]
    public async Task Export_NoChaptersSelected_NoOp()
    {
        var (vm, _) = Build(Chap("g1", "One", 1));
        vm.Refresh();
        vm.SelectNoneCommand.Execute(null);
        bool dialogShown = false;
        vm.ShowSaveFileDialog = (_, _) => { dialogShown = true; return Task.FromResult<string?>(null); };
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.False(dialogShown); // returned before showing dialog
    }

    [Fact]
    public async Task Export_CancelledDialog_NoExport()
    {
        var (vm, _) = Build(Chap("g1", "One", 1));
        vm.Refresh();
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(null); // cancelled
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.False(vm.IsExporting);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public async Task Export_ExtensionFormat_RunsDelegate_Success()
    {
        var (vm, proj) = Build(Chap("g1", "One", 1));
        proj.ProjectRoot.Returns(@"C:\proj");
        vm.Refresh();
        bool ran = false;
        vm.LoadExtensionFormats([new ExportFormatDescriptor
        {
            DisplayName = "MyFmt", FileExtension = ".x",
            Export = _ => { ran = true; return Task.CompletedTask; }
        }]);
        vm.SelectedFormatIndex = 7; // first extension format
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(@"C:\out\file.x");

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.True(ran);
        Assert.False(vm.IsExporting);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage)); // success message
    }

    [Fact]
    public async Task Export_ExtensionFormat_NullDelegate_StillSucceeds()
    {
        var (vm, proj) = Build(Chap("g1", "One", 1));
        vm.Refresh();
        vm.LoadExtensionFormats([new ExportFormatDescriptor { DisplayName = "MyFmt", FileExtension = ".x", Export = null }]);
        vm.SelectedFormatIndex = 7;
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(@"C:\out\file.x");
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.False(vm.IsExporting);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Export_BuiltInFormat_FailureCaught(int index)
    {
        var (vm, proj) = Build(Chap("g1", "One", 1));
        proj.ProjectRoot.Returns((string?)null);
        vm.Refresh();
        vm.SelectedFormatIndex = index;
        // Output to a non-existent drive so the real ExportService write fails -> catch path.
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(@"Z:\nope\out");
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.False(vm.IsExporting); // finally ran
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void ExportButtonText_ReflectsFormat()
    {
        var (vm, _) = Build();
        vm.SelectedFormatIndex = 3; // Markdown
        Assert.False(string.IsNullOrEmpty(vm.ExportButtonText));
    }

    [Fact]
    public async Task Export_BuiltInMarkdown_Succeeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nov_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (vm, proj) = Build(Chap("g1", "One", 1));
            proj.ProjectRoot.Returns(dir);
            proj.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());
            vm.Refresh();
            vm.SelectedFormatIndex = 3; // Markdown
            var outPath = Path.Combine(dir, "out.md");
            vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(outPath);

            await vm.ExportCommand.ExecuteAsync(null);

            Assert.False(vm.IsExporting);
            Assert.True(File.Exists(outPath)); // real ExportService wrote the file
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Export_ExtensionIndexButNoFormatLoaded_FallsToBuiltInBranch()
    {
        var (vm, proj) = Build(Chap("g1", "One", 1));
        proj.ProjectRoot.Returns((string?)null);
        vm.Refresh();
        vm.SelectedFormatIndex = 7; // extension index, but no extension formats loaded
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(@"Z:\nope\out");
        await vm.ExportCommand.ExecuteAsync(null); // else branch, SelectedFormat default arm
        Assert.False(vm.IsExporting);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public async Task Export_WhileExporting_NoReentry()
    {
        var (vm, proj) = Build(Chap("g1", "One", 1));
        proj.ProjectRoot.Returns(@"C:\proj");
        vm.Refresh();
        var gate = new TaskCompletionSource();
        vm.LoadExtensionFormats([new ExportFormatDescriptor
        {
            DisplayName = "Slow", FileExtension = ".x",
            Export = async _ => await gate.Task
        }]);
        vm.SelectedFormatIndex = 7;
        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(@"C:\out\file.x");

        var first = vm.ExportCommand.ExecuteAsync(null); // begins, IsExporting=true, awaits gate
        await Task.Delay(20);
        Assert.True(vm.IsExporting);
        await vm.ExportCommand.ExecuteAsync(null); // re-entry guard -> returns immediately
        gate.SetResult();
        await first;
        Assert.False(vm.IsExporting);
    }
}
