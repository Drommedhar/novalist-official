using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class GitViewModelTests
{
    private static GitFileEntry Staged(string path) => new(path, GitFileStatus.Modified, GitFileStatus.Unmodified);
    private static GitFileEntry Unstaged(string path) => new(path, GitFileStatus.Unmodified, GitFileStatus.Modified);

    private static (GitViewModel Vm, IGitService Git) Build(bool repo = true, bool installed = true)
    {
        var git = Substitute.For<IGitService>();
        git.IsGitRepo.Returns(repo);
        git.IsGitInstalled.Returns(installed);
        return (new GitViewModel(git), git);
    }

    private static GitRepoInfo Info(string branch = "main", bool remote = true, int ahead = 0, int behind = 0, params GitFileEntry[] files)
        => new(branch, remote, ahead, behind, files.ToList());

    [AvaloniaFact]
    public async Task Initialize_NotRepo_SkipsRefresh()
    {
        var (vm, git) = Build(repo: false);
        await vm.InitializeAsync();
        Assert.False(vm.IsGitRepo);
        Assert.True(vm.IsGitInstalled);
        await git.DidNotReceive().GetStatusAsync();
    }

    [AvaloniaFact]
    public async Task Initialize_Repo_Refreshes()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info("dev", true, 2, 1, Staged("a.txt"), Unstaged("b.txt")));
        await vm.InitializeAsync();
        Assert.Equal("dev", vm.BranchName);
        Assert.True(vm.HasRemote);
        Assert.Equal(2, vm.AheadBy);
        Assert.Equal(2, vm.ChangedFileCount);
        Assert.True(vm.HasChanges);
        Assert.True(vm.HasStagedChanges);
    }

    [AvaloniaFact]
    public async Task Refresh_NotRepo_NoOp()
    {
        var (vm, git) = Build(repo: false);
        await vm.RefreshAsync();
        await git.DidNotReceive().GetStatusAsync();
    }

    [AvaloniaFact]
    public async Task Refresh_NullInfo_NoOp()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns((GitRepoInfo?)null);
        await vm.RefreshAsync();
        Assert.False(vm.IsLoading);
    }

    [AvaloniaFact]
    public async Task Refresh_SortsFiles_RaisesEvent()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Unstaged("z.txt"), Unstaged("a.txt") }));
        bool raised = false;
        vm.StatusRefreshed += () => raised = true;
        await vm.RefreshAsync();
        Assert.Equal("a.txt", vm.ChangedFiles[0].RelativePath);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public async Task Refresh_Exception_SetsError()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns<GitRepoInfo?>(_ => throw new InvalidOperationException("boom"));
        await vm.RefreshAsync();
        Assert.True(vm.HasError);
        Assert.Equal("boom", vm.StatusMessage);
        Assert.False(vm.IsLoading);
    }

    [AvaloniaFact]
    public async Task CommitSelected_GuardsAndSuccess()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Staged("a.txt") }));
        await vm.RefreshAsync();

        // no message -> guard
        await vm.CommitSelectedCommand.ExecuteAsync(null);
        await git.DidNotReceive().CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>());

        vm.CommitMessage = "msg";
        git.CommitAsync(Arg.Any<IEnumerable<string>>(), "msg").Returns((string?)null); // success
        await vm.CommitSelectedCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.CommitMessage);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public async Task CommitSelected_Error()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Staged("a.txt") }));
        await vm.RefreshAsync();
        vm.CommitMessage = "m";
        git.CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>()).Returns("fail");
        await vm.CommitSelectedCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal("fail", vm.StatusMessage);
    }

    [AvaloniaFact]
    public async Task CommitSelected_NoStaged_NoOp()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Unstaged("a.txt") }));
        await vm.RefreshAsync();
        vm.CommitMessage = "m";
        await vm.CommitSelectedCommand.ExecuteAsync(null);
        await git.DidNotReceive().CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task CommitAll_GuardsAndSuccessAndError()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Unstaged("a.txt") }));
        await vm.RefreshAsync();

        await vm.CommitAllCommand.ExecuteAsync(null); // empty message -> guard
        await git.DidNotReceive().CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>());

        vm.CommitMessage = "m";
        git.CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>()).Returns((string?)null);
        await vm.CommitAllCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.CommitMessage);

        vm.CommitMessage = "m2";
        git.CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>()).Returns("err");
        await vm.CommitAllCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
    }

    [AvaloniaFact]
    public async Task CommitAll_NoFiles_NoOp()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info());
        await vm.RefreshAsync();
        vm.CommitMessage = "m";
        await vm.CommitAllCommand.ExecuteAsync(null);
        await git.DidNotReceive().CommitAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task Push_NoRemote_NoOp()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(remote: false));
        await vm.RefreshAsync();
        await vm.PushCommand.ExecuteAsync(null);
        await git.DidNotReceive().PushAsync();
    }

    [AvaloniaFact]
    public async Task Push_SuccessAndError()
    {
        var captured = new List<string>();
        Toast.Show = (msg, _) => captured.Add(msg);
        try
        {
            var (vm, git) = Build();
            git.GetStatusAsync().Returns(Info(remote: true));
            await vm.RefreshAsync();

            git.PushAsync().Returns((string?)null);
            await vm.PushCommand.ExecuteAsync(null);
            Assert.False(vm.HasError);

            git.PushAsync().Returns("nope");
            await vm.PushCommand.ExecuteAsync(null);
            Assert.True(vm.HasError);
            Assert.Equal("nope", vm.StatusMessage);
        }
        finally { Toast.Show = null; }
    }

    [AvaloniaFact]
    public async Task Pull_NoRemote_AndSuccessError()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(remote: false));
        await vm.RefreshAsync();
        await vm.PullCommand.ExecuteAsync(null);
        await git.DidNotReceive().PullAsync();

        git.GetStatusAsync().Returns(Info(remote: true));
        await vm.RefreshAsync();
        git.PullAsync().Returns((string?)null);
        await vm.PullCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        git.PullAsync().Returns("e");
        await vm.PullCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
    }

    [AvaloniaFact]
    public async Task DiscardSelected_GuardsAndSuccessError()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Staged("only.txt") })); // no unstaged
        await vm.RefreshAsync();
        await vm.DiscardSelectedCommand.ExecuteAsync(null);
        await git.DidNotReceive().DiscardChangesAsync(Arg.Any<IEnumerable<string>>());

        git.GetStatusAsync().Returns(Info(files: new[] { Unstaged("u.txt") }));
        await vm.RefreshAsync();
        git.DiscardChangesAsync(Arg.Any<IEnumerable<string>>()).Returns("derr");
        await vm.DiscardSelectedCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);

        // success branch: no error -> refresh
        git.DiscardChangesAsync(Arg.Any<IEnumerable<string>>()).Returns((string?)null);
        await vm.DiscardSelectedCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public async Task StageAll_UnstageAll()
    {
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(files: new[] { Unstaged("a.txt") }));
        await vm.RefreshAsync();
        vm.StageAllCommand.Execute(null);
        Assert.True(vm.HasStagedChanges);
        vm.UnstageAllCommand.Execute(null);
        Assert.False(vm.HasStagedChanges);
    }

    [AvaloniaFact]
    public void GetFileStatus_Delegates()
    {
        var (vm, git) = Build();
        git.GetFileStatus("x").Returns(GitFileStatus.Added);
        Assert.Equal(GitFileStatus.Added, vm.GetFileStatus("x"));
    }

    [AvaloniaTheory]
    [InlineData(false, 0, 0, "none")]   // no remote -> empty
    [InlineData(true, 0, 0, "synced")]
    [InlineData(true, 2, 0, "ahead")]
    [InlineData(true, 0, 3, "behind")]
    [InlineData(true, 1, 1, "both")]
    public async Task SyncDisplay_Formats(bool remote, int ahead, int behind, string kind)
    {
        // Build expected from code points so this source stays free of dingbat glyphs.
        var up = ((char)0x2191).ToString();   // up arrow
        var down = ((char)0x2193).ToString(); // down arrow
        var check = ((char)0x2713).ToString();
        var expected = kind switch
        {
            "none" => "",
            "synced" => check,
            "ahead" => up + ahead,
            "behind" => down + behind,
            _ => $"{up}{ahead} {down}{behind}",
        };
        var (vm, git) = Build();
        git.GetStatusAsync().Returns(Info(remote: remote, ahead: ahead, behind: behind));
        await vm.RefreshAsync();
        Assert.Equal(expected, vm.SyncDisplay);
    }

    [AvaloniaTheory]
    [InlineData(GitFileStatus.Modified, "M", "#E5C07B")]
    [InlineData(GitFileStatus.Added, "A", "#98C379")]
    [InlineData(GitFileStatus.Deleted, "D", "#E06C75")]
    [InlineData(GitFileStatus.Renamed, "R", "#56B6C2")]
    [InlineData(GitFileStatus.Untracked, "?", "#ABB2BF")]
    [InlineData(GitFileStatus.Conflicted, "C", "#E06C75")]
    [InlineData(GitFileStatus.Unmodified, " ", "#ABB2BF")]
    public void FileItem_StatusLabelAndColor(GitFileStatus status, string label, string color)
    {
        // Put the status in the work tree so it shows while unstaged.
        var item = new GitFileItemViewModel(new GitFileEntry("dir/file.txt", GitFileStatus.Unmodified, status));
        Assert.Equal(label, item.StatusLabel);
        Assert.Equal(color, item.StatusColor);
        Assert.Equal("file.txt", item.FileName);
        Assert.Equal("dir", item.Directory);
        var brush = Assert.IsType<Avalonia.Media.SolidColorBrush>(item.StatusBrush);
        Assert.Equal(Avalonia.Media.Color.Parse(color), brush.Color);
    }

    [AvaloniaFact]
    public void FileItem_StagedShowsIndexStatus_NotifiesOnToggle()
    {
        var item = new GitFileItemViewModel(new GitFileEntry("a.txt", GitFileStatus.Added, GitFileStatus.Modified));
        Assert.True(item.IsStaged);
        Assert.Equal(GitFileStatus.Added, item.DisplayStatus); // staged -> index status
        var props = new List<string>();
        item.PropertyChanged += (_, e) => props.Add(e.PropertyName!);
        item.IsStaged = false;
        Assert.Contains(nameof(item.DisplayStatus), props);
        Assert.Equal(GitFileStatus.Modified, item.DisplayStatus); // unstaged -> worktree status
    }
}
