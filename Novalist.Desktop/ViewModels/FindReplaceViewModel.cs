using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class FindReplaceViewModel : ObservableObject
{
    private readonly IFindReplaceService _service;
    private readonly ISnapshotService _snapshotService;
    private readonly Func<(string ChapterGuid, string SceneId)?>? _getCurrentSceneAnchor;
    private readonly Func<FindMatch, Task>? _onJumpRequested;

    [ObservableProperty] private string _pattern = string.Empty;
    [ObservableProperty] private string _replacement = string.Empty;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private FindScope _scope = FindScope.ActiveBook;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;

    public ObservableCollection<FindMatch> Results { get; } = [];

    public Array AvailableScopes => Enum.GetValues<FindScope>();

    public FindReplaceViewModel(
        IFindReplaceService service,
        ISnapshotService snapshotService,
        Func<(string ChapterGuid, string SceneId)?>? getCurrentSceneAnchor = null,
        Func<FindMatch, Task>? onJumpRequested = null)
    {
        _service = service;
        _snapshotService = snapshotService;
        _getCurrentSceneAnchor = getCurrentSceneAnchor;
        _onJumpRequested = onJumpRequested;
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        if (string.IsNullOrEmpty(Pattern)) return;
        IsBusy = true;
        try
        {
            var anchor = _getCurrentSceneAnchor?.Invoke();
            var opts = new FindOptions
            {
                Pattern = Pattern,
                MatchCase = MatchCase,
                WholeWord = WholeWord,
                UseRegex = UseRegex,
                Scope = Scope,
                AnchorChapterGuid = anchor?.ChapterGuid,
                AnchorSceneId = anchor?.SceneId
            };
            var matches = await _service.FindAsync(opts, CancellationToken.None);
            Results.Clear();
            foreach (var m in matches) Results.Add(m);
            StatusText = $"{matches.Count} match(es)";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReplaceAllAsync()
    {
        if (string.IsNullOrEmpty(Pattern)) return;
        IsBusy = true;
        try
        {
            var anchor = _getCurrentSceneAnchor?.Invoke();
            var opts = new FindOptions
            {
                Pattern = Pattern,
                Replacement = Replacement,
                MatchCase = MatchCase,
                WholeWord = WholeWord,
                UseRegex = UseRegex,
                Scope = Scope,
                AnchorChapterGuid = anchor?.ChapterGuid,
                AnchorSceneId = anchor?.SceneId
            };
            var n = await _service.ReplaceAllAsync(opts, _snapshotService, CancellationToken.None);
            Results.Clear();
            StatusText = $"Replaced {n}.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task JumpToAsync(FindMatch match)
    {
        if (_onJumpRequested != null)
            await _onJumpRequested.Invoke(match);
    }
}
