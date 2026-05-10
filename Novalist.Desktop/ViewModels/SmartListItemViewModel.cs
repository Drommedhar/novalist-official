using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class SmartListItemViewModel : ObservableObject
{
    private readonly ISmartListService _service;
    private readonly Action<ChapterData, SceneData>? _onOpen;
    private bool _evaluated;

    public SmartList Source { get; }

    public string Name => Source.Name;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _matchCount;

    public ObservableCollection<SmartListSceneEntryViewModel> Matches { get; } = new();

    public SmartListItemViewModel(SmartList source, ISmartListService service, Action<ChapterData, SceneData>? onOpen)
    {
        Source = source;
        _service = service;
        _onOpen = onOpen;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_evaluated)
            _ = EvaluateAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _evaluated = false;
        Matches.Clear();
        await EvaluateAsync();
    }

    private async Task EvaluateAsync()
    {
        IsLoading = true;
        try
        {
            var matches = await _service.EvaluateAsync(Source);
            Matches.Clear();
            foreach (var (chapter, scene) in matches)
                Matches.Add(new SmartListSceneEntryViewModel(chapter, scene, _onOpen));
            MatchCount = Matches.Count;
            _evaluated = true;
        }
        finally { IsLoading = false; }
    }
}

public partial class SmartListSceneEntryViewModel : ObservableObject
{
    private readonly Action<ChapterData, SceneData>? _onOpen;

    public ChapterData Chapter { get; }
    public SceneData Scene { get; }
    public string DisplayLabel => $"{Chapter.Title} → {Scene.Title}";

    public SmartListSceneEntryViewModel(ChapterData chapter, SceneData scene, Action<ChapterData, SceneData>? onOpen)
    {
        Chapter = chapter;
        Scene = scene;
        _onOpen = onOpen;
    }

    [RelayCommand]
    private void Open()
    {
        _onOpen?.Invoke(Chapter, Scene);
    }
}
