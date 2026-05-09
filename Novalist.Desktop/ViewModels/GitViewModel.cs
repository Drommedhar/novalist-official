using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class GitViewModel : ObservableObject
{
    private readonly IGitService _gitService;

    [ObservableProperty]
    private bool _isGitRepo;

    [ObservableProperty]
    private bool _isGitInstalled;

    [ObservableProperty]
    private string _branchName = string.Empty;

    [ObservableProperty]
    private bool _hasRemote;

    [ObservableProperty]
    private int _aheadBy;

    [ObservableProperty]
    private int _behindBy;

    [ObservableProperty]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitFileItemViewModel> _changedFiles = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public bool HasChanges => ChangedFiles.Count > 0;
    public bool HasStagedChanges => ChangedFiles.Any(f => f.IsStaged);
    public int ChangedFileCount => ChangedFiles.Count;
    public string SyncDisplay => FormatSyncDisplay();

    /// <summary>
    /// Fired when git status changes so other VMs can update indicators.
    /// </summary>
    public event Action? StatusRefreshed;

    public GitViewModel(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task InitializeAsync()
    {
        IsGitInstalled = _gitService.IsGitInstalled;
        IsGitRepo = _gitService.IsGitRepo;

        if (IsGitRepo)
            await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_gitService.IsGitRepo)
            return;

        IsLoading = true;
        HasError = false;
        StatusMessage = string.Empty;

        try
        {
            var info = await _gitService.GetStatusAsync();
            if (info == null)
                return;

            BranchName = info.BranchName;
            HasRemote = info.HasRemote;
            AheadBy = info.AheadBy;
            BehindBy = info.BehindBy;

            ChangedFiles = new ObservableCollection<GitFileItemViewModel>(
                info.ChangedFiles
                    .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new GitFileItemViewModel(f)));

            NotifyStatusProperties();
            StatusRefreshed?.Invoke();
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CommitSelectedAsync()
    {
        var staged = ChangedFiles.Where(f => f.IsStaged).Select(f => f.RelativePath).ToList();
        if (staged.Count == 0 || string.IsNullOrWhiteSpace(CommitMessage))
            return;

        IsLoading = true;
        HasError = false;

        var error = await _gitService.CommitAsync(staged, CommitMessage.Trim());
        if (error != null)
        {
            HasError = true;
            StatusMessage = error;
        }
        else
        {
            CommitMessage = string.Empty;
            StatusMessage = Loc.T("git.commitSuccess");
        }

        IsLoading = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CommitAllAsync()
    {
        if (ChangedFiles.Count == 0 || string.IsNullOrWhiteSpace(CommitMessage))
            return;

        IsLoading = true;
        HasError = false;

        var allPaths = ChangedFiles.Select(f => f.RelativePath).ToList();
        var error = await _gitService.CommitAsync(allPaths, CommitMessage.Trim());
        if (error != null)
        {
            HasError = true;
            StatusMessage = error;
        }
        else
        {
            CommitMessage = string.Empty;
            StatusMessage = Loc.T("git.commitSuccess");
        }

        IsLoading = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        if (!HasRemote)
            return;

        IsLoading = true;
        HasError = false;

        var error = await _gitService.PushAsync();
        if (error != null)
        {
            HasError = true;
            StatusMessage = error;
            Toast.Show?.Invoke(Loc.T("toast.pushFailed", error), ToastSeverity.Error);
        }
        else
        {
            StatusMessage = Loc.T("git.pushSuccess");
            Toast.Show?.Invoke(Loc.T("git.pushSuccess"), ToastSeverity.Success);
        }

        IsLoading = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (!HasRemote)
            return;

        IsLoading = true;
        HasError = false;

        var error = await _gitService.PullAsync();
        if (error != null)
        {
            HasError = true;
            StatusMessage = error;
            Toast.Show?.Invoke(Loc.T("toast.pullFailed", error), ToastSeverity.Error);
        }
        else
        {
            StatusMessage = Loc.T("git.pullSuccess");
            Toast.Show?.Invoke(Loc.T("git.pullSuccess"), ToastSeverity.Success);
        }

        IsLoading = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DiscardSelectedAsync()
    {
        var unstaged = ChangedFiles
            .Where(f => !f.IsStaged)
            .Select(f => f.RelativePath)
            .ToList();
        if (unstaged.Count == 0)
            return;

        IsLoading = true;
        HasError = false;

        var error = await _gitService.DiscardChangesAsync(unstaged);
        if (error != null)
        {
            HasError = true;
            StatusMessage = error;
        }

        IsLoading = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private void StageAll()
    {
        foreach (var file in ChangedFiles)
            file.IsStaged = true;
        NotifyStatusProperties();
    }

    [RelayCommand]
    private void UnstageAll()
    {
        foreach (var file in ChangedFiles)
            file.IsStaged = false;
        NotifyStatusProperties();
    }

    /// <summary>
    /// Returns the git file status for a project-relative path.
    /// </summary>
    public GitFileStatus GetFileStatus(string projectRelativePath)
        => _gitService.GetFileStatus(projectRelativePath);

    private void NotifyStatusProperties()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(ChangedFileCount));
        OnPropertyChanged(nameof(SyncDisplay));
    }

    private string FormatSyncDisplay()
    {
        if (!HasRemote)
            return string.Empty;

        var parts = new List<string>();
        if (AheadBy > 0) parts.Add($"↑{AheadBy}");
        if (BehindBy > 0) parts.Add($"↓{BehindBy}");
        return parts.Count > 0 ? string.Join(" ", parts) : "✓";
    }
}

public partial class GitFileItemViewModel : ObservableObject
{
    public string RelativePath { get; }
    public GitFileStatus IndexStatus { get; }
    public GitFileStatus WorkTreeStatus { get; }

    [ObservableProperty]
    private bool _isStaged;

    public string FileName => System.IO.Path.GetFileName(RelativePath);
    public string Directory => System.IO.Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? string.Empty;

    public GitFileStatus DisplayStatus => IsStaged ? IndexStatus : WorkTreeStatus;

    public string StatusLabel => DisplayStatus switch
    {
        GitFileStatus.Modified => "M",
        GitFileStatus.Added => "A",
        GitFileStatus.Deleted => "D",
        GitFileStatus.Renamed => "R",
        GitFileStatus.Untracked => "?",
        GitFileStatus.Conflicted => "C",
        _ => " "
    };

    public string StatusColor => DisplayStatus switch
    {
        GitFileStatus.Modified => "#E5C07B",
        GitFileStatus.Added => "#98C379",
        GitFileStatus.Deleted => "#E06C75",
        GitFileStatus.Renamed => "#56B6C2",
        GitFileStatus.Untracked => "#ABB2BF",
        GitFileStatus.Conflicted => "#E06C75",
        _ => "#ABB2BF"
    };

    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(StatusColor));

    public GitFileItemViewModel(GitFileEntry entry)
    {
        RelativePath = entry.RelativePath;
        IndexStatus = entry.IndexStatus;
        WorkTreeStatus = entry.WorkTreeStatus;
        _isStaged = entry.IsStaged;
    }

    partial void OnIsStagedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusBrush));
    }
}
