using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class SceneNotesViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private EditorViewModel? _editor;
    private string? _currentSceneId;
    private CancellationTokenSource? _autoSaveCts;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isSceneOpen;

    public SceneNotesViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void AttachEditor(EditorViewModel editor)
    {
        if (_editor != null)
            _editor.PropertyChanged -= OnEditorPropertyChanged;

        _editor = editor;
        _editor.PropertyChanged += OnEditorPropertyChanged;

        SyncFromEditor();
    }

    partial void OnNotesChanged(string value)
    {
        if (_editor?.CurrentScene == null || !IsSceneOpen)
            return;

        _editor.CurrentScene.Notes = string.IsNullOrWhiteSpace(value) ? null : value;
        ScheduleAutoSave();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.CurrentScene) or nameof(EditorViewModel.IsDocumentOpen))
            SyncFromEditor();
    }

    private void SyncFromEditor()
    {
        if (_editor?.IsDocumentOpen != true || _editor.CurrentScene == null)
        {
            _currentSceneId = null;
            IsSceneOpen = false;
            Notes = string.Empty;
            CancelAutoSave();
            return;
        }

        var scene = _editor.CurrentScene;
        if (string.Equals(_currentSceneId, scene.Id))
            return;

        CancelAutoSave();
        _currentSceneId = scene.Id;
        Notes = scene.Notes ?? string.Empty;
        IsSceneOpen = true;
    }

    private void ScheduleAutoSave()
    {
        CancelAutoSave();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, token);
                if (!token.IsCancellationRequested)
                    await _projectService.SaveScenesAsync();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void CancelAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
    }

    public async Task FlushAsync()
    {
        CancelAutoSave();
        if (_editor?.CurrentScene != null)
            await _projectService.SaveScenesAsync();
    }
}
