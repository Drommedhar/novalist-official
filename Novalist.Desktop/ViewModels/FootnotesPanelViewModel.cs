using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class FootnotesPanelViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private EditorViewModel? _editor;
    private string? _currentSceneId;

    [ObservableProperty]
    private bool _isSceneOpen;

    [ObservableProperty]
    private bool _hasFootnotes;

    public ObservableCollection<FootnoteItem> Footnotes { get; } = new();

    public FootnotesPanelViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void AttachEditor(EditorViewModel? editor)
    {
        if (_editor != null)
        {
            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor.FootnoteInserted -= OnFootnoteInserted;
        }

        _editor = editor;

        if (_editor != null)
        {
            _editor.PropertyChanged += OnEditorPropertyChanged;
            _editor.FootnoteInserted += OnFootnoteInserted;
        }

        Sync();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.CurrentScene) or nameof(EditorViewModel.IsDocumentOpen))
            Sync();
    }

    private void OnFootnoteInserted(string id, int number)
    {
        // MainWindowViewModel's per-add handler subscribes AFTER ours and is
        // what actually appends to scene.Footnotes. Defer the sync so the
        // newly-added entry exists when we re-read it.
        Avalonia.Threading.Dispatcher.UIThread.Post(Sync,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private void Sync()
    {
        Footnotes.Clear();
        if (_editor?.IsDocumentOpen != true || _editor.CurrentScene == null)
        {
            IsSceneOpen = false;
            HasFootnotes = false;
            _currentSceneId = null;
            return;
        }

        var scene = _editor.CurrentScene;
        _currentSceneId = scene.Id;
        IsSceneOpen = true;
        if (scene.Footnotes != null)
        {
            foreach (var fn in scene.Footnotes.OrderBy(f => f.Number))
                Footnotes.Add(new FootnoteItem(fn, OnTextEdited, OnJumpRequested, OnDeleteRequested));
        }
        HasFootnotes = Footnotes.Count > 0;
    }

    private async void OnTextEdited(FootnoteItem item)
    {
        if (_editor?.CurrentScene?.Footnotes == null) return;
        var stored = _editor.CurrentScene.Footnotes.FirstOrDefault(f => f.Id == item.Id);
        if (stored == null) return;
        stored.Text = item.Text;
        await _projectService.SaveScenesAsync();
        _editor.SyncCommentsAction?.Invoke();
    }

    private void OnJumpRequested(FootnoteItem item)
    {
        _editor?.ScrollToFootnoteAction?.Invoke(item.Id);
    }

    private async void OnDeleteRequested(FootnoteItem item)
    {
        if (_editor?.CurrentScene?.Footnotes == null) return;
        _editor.CurrentScene.Footnotes.RemoveAll(f => f.Id == item.Id);
        _editor.RemoveFootnoteAction?.Invoke(item.Id);
        Footnotes.Remove(item);
        HasFootnotes = Footnotes.Count > 0;
        await _projectService.SaveScenesAsync();
        _editor.SyncCommentsAction?.Invoke();
    }
}

public partial class FootnoteItem : ObservableObject
{
    private readonly Action<FootnoteItem>? _onTextChanged;
    private readonly Action<FootnoteItem>? _onJump;
    private readonly Action<FootnoteItem>? _onDelete;

    public string Id { get; }
    public int Number { get; }

    [ObservableProperty]
    private string _text;

    public FootnoteItem(SceneFootnote source,
        Action<FootnoteItem>? onTextChanged,
        Action<FootnoteItem>? onJump,
        Action<FootnoteItem>? onDelete)
    {
        Id = source.Id;
        Number = source.Number;
        _text = source.Text;
        _onTextChanged = onTextChanged;
        _onJump = onJump;
        _onDelete = onDelete;
    }

    partial void OnTextChanged(string value) => _onTextChanged?.Invoke(this);

    [RelayCommand]
    private void Jump() => _onJump?.Invoke(this);

    [RelayCommand]
    private void Delete() => _onDelete?.Invoke(this);
}
