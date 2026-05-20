using System.ComponentModel;
using Avalonia.Threading;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class FootnotesPanelViewModelTests
{
    private sealed class FakeEditor : IFootnoteEditorContext
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<string, int>? FootnoteInserted;
        public Action<string>? RemoveFootnoteAction { get; set; }
        public Action<string>? ScrollToFootnoteAction { get; set; }
        public Action? SyncCommentsAction { get; set; }

        private bool _open;
        public bool IsDocumentOpen { get => _open; set { _open = value; Raise(nameof(IsDocumentOpen)); } }
        private SceneData? _scene;
        public SceneData? CurrentScene { get => _scene; set { _scene = value; Raise(nameof(CurrentScene)); } }
        public ChapterData? CurrentChapter { get; set; }
        public string PlainTextContent { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;

        public void RaiseFootnoteInserted(string id, int n) => FootnoteInserted?.Invoke(id, n);
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private sealed class H
    {
        public IProjectService Proj = null!;
        public FootnotesPanelViewModel Vm = null!;
        public FakeEditor Editor = new();
    }

    private static H Build()
    {
        var h = new H();
        h.Proj = Substitute.For<IProjectService>();
        h.Proj.SaveScenesAsync().Returns(Task.CompletedTask);
        h.Vm = new FootnotesPanelViewModel(h.Proj);
        return h;
    }

    private static SceneData SceneWithFootnotes(params (string id, int num, string text)[] fns)
    {
        var s = new SceneData { Id = "sc1", Footnotes = [] };
        foreach (var (id, num, text) in fns)
            s.Footnotes!.Add(new SceneFootnote { Id = id, Number = num, Text = text });
        return s;
    }

    [AvaloniaFact]
    public void Attach_NoScene_Empty()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor);
        Assert.False(h.Vm.IsSceneOpen);
        Assert.False(h.Vm.HasFootnotes);
        Assert.Empty(h.Vm.Footnotes);
    }

    [AvaloniaFact]
    public void Attach_Null_Empty()
    {
        var h = Build();
        h.Vm.AttachEditor(null);
        Assert.False(h.Vm.IsSceneOpen);
    }

    [AvaloniaFact]
    public void Attach_SceneWithFootnotes_PopulatesOrdered()
    {
        var h = Build();
        h.Editor.CurrentScene = SceneWithFootnotes(("b", 2, "two"), ("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        Assert.True(h.Vm.IsSceneOpen);
        Assert.True(h.Vm.HasFootnotes);
        Assert.Equal(2, h.Vm.Footnotes.Count);
        Assert.Equal(1, h.Vm.Footnotes[0].Number); // ordered by number
    }

    [AvaloniaFact]
    public void SceneOpenWithNoFootnotesList_NoFootnotes()
    {
        var h = Build();
        h.Editor.CurrentScene = new SceneData { Id = "sc1", Footnotes = null };
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        Assert.True(h.Vm.IsSceneOpen);
        Assert.False(h.Vm.HasFootnotes);
    }

    [AvaloniaFact]
    public void EditorPropertyChange_ReSyncs()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor);
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one")); // fires CurrentScene change
        h.Editor.IsDocumentOpen = true; // fires IsDocumentOpen change -> Sync
        Assert.True(h.Vm.HasFootnotes);
    }

    [AvaloniaFact]
    public void ReAttach_DetachesPrevious()
    {
        var h = Build();
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        var editor2 = new FakeEditor();
        h.Vm.AttachEditor(editor2); // detaches first
        h.Editor.RaiseFootnoteInserted("x", 9); // old editor event must be ignored
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Assert.False(h.Vm.HasFootnotes); // editor2 has no scene
    }

    [AvaloniaFact]
    public void FootnoteInserted_PostsSync()
    {
        var h = Build();
        var scene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.CurrentScene = scene;
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);

        // simulate MainWindow's handler appending a footnote, then editor raising the event
        scene.Footnotes!.Add(new SceneFootnote { Id = "b", Number = 2, Text = "two" });
        h.Editor.RaiseFootnoteInserted("b", 2);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background); // runs the posted Sync
        Assert.Equal(2, h.Vm.Footnotes.Count);
    }

    [AvaloniaFact]
    public void FootnoteItem_TextEdit_SavesAndSyncsComments()
    {
        var h = Build();
        var synced = false;
        h.Editor.SyncCommentsAction = () => synced = true;
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);

        // Mock SaveScenesAsync completes synchronously, so the async-void handler runs inline.
        h.Vm.Footnotes[0].Text = "edited";
        Assert.Equal("edited", h.Editor.CurrentScene!.Footnotes!.First(f => f.Id == "a").Text);
        _ = h.Proj.Received().SaveScenesAsync();
        Assert.True(synced);
    }

    [AvaloniaFact]
    public void FootnoteItem_TextEdit_SaveThrows_Caught()
    {
        var h = Build();
        h.Proj.SaveScenesAsync().Returns<Task>(_ => throw new InvalidOperationException("boom"));
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Footnotes[0].Text = "edited"; // exception caught in OnTextEdited
        Assert.Equal("edited", h.Vm.Footnotes[0].Text); // no crash
    }

    [AvaloniaFact]
    public void FootnoteItem_Jump_InvokesScroll()
    {
        var h = Build();
        string? scrolled = null;
        h.Editor.ScrollToFootnoteAction = id => scrolled = id;
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Footnotes[0].JumpCommand.Execute(null);
        Assert.Equal("a", scrolled);
    }

    [AvaloniaFact]
    public void FootnoteItem_Delete_RemovesAndSaves()
    {
        var h = Build();
        string? removed = null;
        h.Editor.RemoveFootnoteAction = id => removed = id;
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"), ("b", 2, "two"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);

        var item = h.Vm.Footnotes[0];
        item.DeleteCommand.Execute(null);
        Assert.Equal("a", removed);
        Assert.Single(h.Vm.Footnotes);
        Assert.DoesNotContain(h.Editor.CurrentScene!.Footnotes!, f => f.Id == "a");
        _ = h.Proj.Received().SaveScenesAsync();
    }

    [AvaloniaFact]
    public void FootnoteItem_Delete_SaveThrows_Caught()
    {
        var h = Build();
        h.Proj.SaveScenesAsync().Returns<Task>(_ => throw new InvalidOperationException("boom"));
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Footnotes[0].DeleteCommand.Execute(null); // exception caught in OnDeleteRequested
        Assert.Empty(h.Vm.Footnotes); // removal happened before the throw
    }

    [AvaloniaFact]
    public void FootnoteItem_EditAndDelete_NoSceneFootnotes_NoThrow()
    {
        var h = Build();
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        var item = h.Vm.Footnotes[0];

        // Drop the backing list so the guard paths run
        h.Editor.CurrentScene!.Footnotes = null;
        item.Text = "changed"; // OnTextEdited guard: Footnotes null -> return
        item.DeleteCommand.Execute(null); // OnDeleteRequested guard: Footnotes null -> return
        _ = h.Proj.DidNotReceive().SaveScenesAsync();
    }

    [AvaloniaFact]
    public void FootnoteItem_TextEdit_UnknownId_NoSave()
    {
        var h = Build();
        h.Editor.CurrentScene = SceneWithFootnotes(("a", 1, "one"));
        h.Editor.IsDocumentOpen = true;
        h.Vm.AttachEditor(h.Editor);
        var item = h.Vm.Footnotes[0];
        // Remove the stored footnote so the id no longer resolves
        h.Editor.CurrentScene!.Footnotes!.Clear();
        item.Text = "orphan"; // stored == null -> return before save
        _ = h.Proj.DidNotReceive().SaveScenesAsync();
    }
}
