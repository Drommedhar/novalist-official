using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Utilities;

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
    private string _synopsis = string.Empty;

    [ObservableProperty]
    private bool _isSceneOpen;

    public ObservableCollection<SceneCommentItem> Comments { get; } = new();

    /// <summary>Called by MainWindow after a new comment is anchored so the
    /// list refreshes and the new entry is highlighted for inline editing.</summary>
    public void SyncCommentsFromScene(SceneData scene, string? focusCommentId = null)
    {
        Comments.Clear();
        if (scene.Comments != null)
        {
            foreach (var c in scene.Comments)
                Comments.Add(new SceneCommentItem(c, OnCommentTextEdited));
        }
        if (focusCommentId != null)
            SelectedComment = Comments.FirstOrDefault(c => c.Id == focusCommentId);
    }

    private async void OnCommentTextEdited(SceneCommentItem item)
    {
        try
        {
            if (_editor?.CurrentScene == null) return;
            var stored = _editor.CurrentScene.Comments?.FirstOrDefault(c => c.Id == item.Id);
            if (stored == null) return;
            stored.Text = item.Text;
            await _projectService.SaveScenesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("SceneNotesViewModel.OnCommentTextEdited failed", ex);
        }
    }

    public SceneNotesViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void AttachEditor(EditorViewModel editor)
    {
        if (_editor != null)
        {
            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor.CommentClicked -= OnEditorCommentClicked;
        }

        _editor = editor;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.CommentClicked += OnEditorCommentClicked;

        SyncFromEditor();
    }

    private void OnEditorCommentClicked(string commentId)
    {
        var item = Comments.FirstOrDefault(c => c.Id == commentId);
        if (item != null) SelectedComment = item;
    }

    [ObservableProperty]
    private SceneCommentItem? _selectedComment;

    [RelayCommand]
    private void JumpToComment(SceneCommentItem? item)
    {
        if (item == null || _editor == null) return;
        _editor.ScrollToCommentAction?.Invoke(item.Id);
    }

    [RelayCommand]
    private async Task DeleteCommentAsync(SceneCommentItem? item)
    {
        if (item == null || _editor?.CurrentScene == null) return;
        _editor.RemoveCommentAction?.Invoke(item.Id);
        var scene = _editor.CurrentScene;
        scene.Comments?.RemoveAll(c => c.Id == item.Id);
        Comments.Remove(item);
        await _projectService.SaveScenesAsync();
    }

    partial void OnNotesChanged(string value)
    {
        if (_editor?.CurrentScene == null || !IsSceneOpen)
            return;

        _editor.CurrentScene.Notes = string.IsNullOrWhiteSpace(value) ? null : value;
        ScheduleAutoSave();
    }

    partial void OnSynopsisChanged(string value)
    {
        if (_editor?.CurrentScene == null || !IsSceneOpen)
            return;

        _editor.CurrentScene.Synopsis = string.IsNullOrWhiteSpace(value) ? null : value;
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
            Synopsis = string.Empty;
            CancelAutoSave();
            return;
        }

        var scene = _editor.CurrentScene;
        if (string.Equals(_currentSceneId, scene.Id))
            return;

        CancelAutoSave();
        _currentSceneId = scene.Id;
        Notes = scene.Notes ?? string.Empty;
        Synopsis = scene.Synopsis ?? string.Empty;
        SyncCommentsFromScene(scene);
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

public partial class SceneCommentItem : ObservableObject
{
    private readonly Action<SceneCommentItem>? _onTextChanged;

    public string Id { get; }
    public string AnchorText { get; }
    public string AnchorPreview => Truncate(AnchorText, 80);

    [ObservableProperty]
    private string _text;

    public SceneCommentItem(SceneComment source, Action<SceneCommentItem>? onTextChanged = null)
    {
        Id = source.Id;
        AnchorText = source.AnchorText;
        _text = source.Text;
        _onTextChanged = onTextChanged;
    }

    partial void OnTextChanged(string value) => _onTextChanged?.Invoke(this);

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
