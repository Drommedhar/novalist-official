using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class SceneNotesViewModelTests
{
    private sealed class H
    {
        public IProjectService Proj = null!;
        public ISettingsService Settings = null!;
        public IEntityService Entity = null!;
        public EditorViewModel Editor = null!;
        public SceneNotesViewModel Vm = null!;
        public Dictionary<string, string> Disk = new(StringComparer.OrdinalIgnoreCase);
    }

    private static H Build()
    {
        var h = new H();
        var app = new AppSettings();
        h.Settings = Substitute.For<ISettingsService>();
        h.Settings.Settings.Returns(app);
        h.Settings.Effective.Returns(app);

        h.Proj = Substitute.For<IProjectService>();
        h.Proj.SaveScenesAsync().Returns(Task.CompletedTask);
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>())
            .Returns(ci => Task.FromResult(h.Disk.GetValueOrDefault(((SceneData)ci[1]).Id, "content")));
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/p/s.html");
        h.Proj.WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(Task.CompletedTask);

        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());

        h.Editor = new EditorViewModel(h.Proj, h.Settings, h.Entity) { AutoSaveDelayMs = 0 };
        h.Vm = new SceneNotesViewModel(h.Proj);
        return h;
    }

    private static async Task<SceneData> OpenScene(H h, SceneData? scene = null)
    {
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        scene ??= new SceneData { Id = "s1", Title = "Sc", Notes = "n", Synopsis = "syn" };
        await h.Editor.OpenSceneAsync(ch, scene);
        return scene;
    }

    [AvaloniaFact]
    public void Attach_NoDocument_Clears()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor);
        Assert.False(h.Vm.IsSceneOpen);
        Assert.Empty(h.Vm.Notes);
        Assert.Empty(h.Vm.Comments);
    }

    [AvaloniaFact]
    public async Task Attach_WithScene_LoadsNotesSynopsisComments()
    {
        var h = Build();
        var scene = new SceneData
        {
            Id = "s1", Title = "Sc", Notes = "my notes", Synopsis = "my synopsis",
            Comments = [new SceneComment { Id = "c1", AnchorText = "anchor", Text = "comment" }],
        };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        Assert.True(h.Vm.IsSceneOpen);
        Assert.Equal("my notes", h.Vm.Notes);
        Assert.Equal("my synopsis", h.Vm.Synopsis);
        Assert.Single(h.Vm.Comments);
    }

    [AvaloniaFact]
    public async Task EditorSceneChange_ReSyncs()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor);
        await OpenScene(h); // fires PropertyChanged -> SyncFromEditor
        Assert.True(h.Vm.IsSceneOpen);
        Assert.Equal("n", h.Vm.Notes);
    }

    [AvaloniaFact]
    public async Task NotesAndSynopsisEdits_WriteToScene()
    {
        var h = Build();
        var scene = await OpenScene(h);
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Notes = "edited notes";
        Assert.Equal("edited notes", scene.Notes);
        h.Vm.Notes = "   "; // whitespace -> null
        Assert.Null(scene.Notes);
        h.Vm.Synopsis = "edited synopsis";
        Assert.Equal("edited synopsis", scene.Synopsis);
        h.Vm.Synopsis = "";
        Assert.Null(scene.Synopsis);
    }

    [AvaloniaFact]
    public void NotesEdit_NoScene_NoThrow()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor); // no scene
        h.Vm.Notes = "x"; // IsSceneOpen false -> guard
        h.Vm.Synopsis = "y";
        Assert.False(h.Vm.IsSceneOpen);
    }

    [AvaloniaFact]
    public async Task JumpToComment_InvokesScroll()
    {
        var h = Build();
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "t" }] };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        string? scrolled = null;
        h.Editor.ScrollToCommentAction = id => scrolled = id;
        h.Vm.JumpToCommentCommand.Execute(h.Vm.Comments[0]);
        Assert.Equal("c1", scrolled);
        h.Vm.JumpToCommentCommand.Execute(null); // null guard
    }

    [AvaloniaFact]
    public async Task DeleteComment_RemovesAndSaves()
    {
        var h = Build();
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "t" }] };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        string? removed = null;
        h.Editor.RemoveCommentAction = id => removed = id;
        await h.Vm.DeleteCommentCommand.ExecuteAsync(h.Vm.Comments[0]);
        Assert.Equal("c1", removed);
        Assert.Empty(h.Vm.Comments);
        Assert.Empty(scene.Comments!);
        await h.Proj.Received().SaveScenesAsync();
        await h.Vm.DeleteCommentCommand.ExecuteAsync(null); // null guard
    }

    [AvaloniaFact]
    public async Task EditorCommentClicked_SelectsComment()
    {
        var h = Build();
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "t" }] };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        h.Editor.RaiseCommentClicked("c1");
        Assert.Equal("c1", h.Vm.SelectedComment!.Id);
        h.Editor.RaiseCommentClicked("missing"); // no match -> unchanged
    }

    [AvaloniaFact]
    public async Task SyncCommentsFromScene_FocusesComment()
    {
        var h = Build();
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "t" }] };
        h.Vm.SyncCommentsFromScene(scene, "c1");
        Assert.Equal("c1", h.Vm.SelectedComment!.Id);
    }

    [AvaloniaFact]
    public async Task CommentTextEdit_SavesScene()
    {
        var h = Build();
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "old" }] };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Comments[0].Text = "new text"; // OnCommentTextEdited
        Assert.Equal("new text", scene.Comments![0].Text);
        await h.Proj.Received().SaveScenesAsync();
    }

    [AvaloniaFact]
    public async Task CommentTextEdit_SaveThrows_Caught()
    {
        var h = Build();
        h.Proj.SaveScenesAsync().Returns<Task>(_ => throw new InvalidOperationException("boom"));
        var scene = new SceneData { Id = "s1", Comments = [new SceneComment { Id = "c1", AnchorText = "a", Text = "old" }] };
        await OpenScene(h, scene);
        h.Vm.AttachEditor(h.Editor);
        h.Vm.Comments[0].Text = "boom"; // exception caught in OnCommentTextEdited
        Assert.Equal("boom", h.Vm.Comments[0].Text);
    }

    [AvaloniaFact]
    public async Task ReAttach_DetachesPrevious()
    {
        var h = Build();
        await OpenScene(h);
        h.Vm.AttachEditor(h.Editor);
        var editor2 = new EditorViewModel(h.Proj, h.Settings, h.Entity) { AutoSaveDelayMs = 0 };
        h.Vm.AttachEditor(editor2); // detaches first
        h.Editor.RaiseCommentClicked("c1"); // old editor event ignored now
        Assert.False(h.Vm.IsSceneOpen); // editor2 has no open scene
    }

    [AvaloniaFact]
    public async Task Flush_SavesWhenSceneOpen()
    {
        var h = Build();
        await OpenScene(h);
        h.Vm.AttachEditor(h.Editor);
        await h.Vm.FlushAsync();
        await h.Proj.Received().SaveScenesAsync();
    }

    [AvaloniaFact]
    public async Task Flush_NoScene_NoSave()
    {
        var h = Build();
        h.Vm.AttachEditor(h.Editor);
        await h.Vm.FlushAsync();
        await h.Proj.DidNotReceive().SaveScenesAsync();
    }

    [AvaloniaFact]
    public void AutoSave_FiresThenCancels()
    {
        // Scratch thread: contain the Task.Delay yield off the Avalonia collection runner.
        Task.Run(async () =>
        {
            var h = Build();
            var scene = await OpenScene(h);
            h.Vm.AttachEditor(h.Editor);
            h.Vm.Notes = "trigger autosave"; // schedules #1 (1500ms)
            await Task.Delay(40);
            h.Vm.Notes = "again"; // cancels #1 (OperationCanceledException), reschedules #2
            await Task.Delay(1700); // let #2 actually fire SaveScenesAsync
            await h.Proj.Received().SaveScenesAsync();
        }).GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void SceneCommentItem_PreviewTruncates()
    {
        var shortC = new SceneCommentItem(new SceneComment { Id = "a", AnchorText = "short", Text = "t" });
        Assert.Equal("short", shortC.AnchorPreview);
        var longAnchor = new string('x', 100);
        var longC = new SceneCommentItem(new SceneComment { Id = "b", AnchorText = longAnchor, Text = "t" });
        Assert.EndsWith("…", longC.AnchorPreview);
        Assert.Equal(81, longC.AnchorPreview.Length); // 80 + ellipsis
    }
}
