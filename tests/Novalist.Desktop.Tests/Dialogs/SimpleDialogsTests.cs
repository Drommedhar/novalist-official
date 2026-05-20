using Avalonia.Input;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Tests.TestHelpers;
using Xunit;

namespace Novalist.Desktop.Tests.Dialogs;

[Collection("Avalonia")]
public class SimpleDialogsTests
{
    // ── ConfirmDialog ───────────────────────────────────────────────
    [AvaloniaFact]
    public void Confirm_Ok_Cancel_Escape()
    {
        var d = new ConfirmDialog("t", "msg");
        DialogHost.Click(d, "OnConfirm");
        Assert.True(d.Confirmed);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new ConfirmDialog("t", "msg");
        DialogHost.Click(d2, "OnCancel");
        Assert.False(d2.Confirmed);
        Assert.True(d2.DialogClosed.Task.IsCompleted);

        var d3 = new ConfirmDialog("t", "msg");
        DialogHost.PressKey(d3, Key.Escape);
        Assert.False(d3.Confirmed);
        Assert.True(d3.DialogClosed.Task.IsCompleted);
    }

    // ── InputDialog ─────────────────────────────────────────────────
    [AvaloniaFact]
    public void Input_Ok_TrimsAndReturns()
    {
        var d = new InputDialog("t", "p", "  hi  ");
        DialogHost.Show(d); // OnAttachedToVisualTree focus path
        DialogHost.Click(d, "OnOk");
        Assert.Equal("hi", d.Result);
    }

    [AvaloniaFact]
    public void Input_Ok_EmptyDisallowed_DoesNotClose()
    {
        var d = new InputDialog("t", "p", "   ");
        DialogHost.Click(d, "OnOk");
        Assert.Null(d.Result);
        Assert.False(d.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void Input_AllowEmpty_ReturnsEmpty()
    {
        var d = new InputDialog("t", "p", "   ", allowEmpty: true);
        DialogHost.Click(d, "OnOk");
        Assert.Equal(string.Empty, d.Result);
    }

    [AvaloniaFact]
    public void Input_Cancel_And_Escape()
    {
        var d = new InputDialog("t", "p", "x");
        DialogHost.Click(d, "OnCancel");
        Assert.Null(d.Result);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new InputDialog("t", "p", "x");
        DialogHost.PressKey(d2, Key.Escape);
        Assert.Null(d2.Result);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    // ── ChapterDialog ───────────────────────────────────────────────
    [AvaloniaFact]
    public void Chapter_Ok_BuildsResult()
    {
        var d = new ChapterDialog("t", "Ch1", "2024-03-04");
        DialogHost.Show(d);
        DialogHost.Click(d, "OnOk");
        Assert.NotNull(d.Result);
        Assert.Equal("Ch1", d.Result!.Title);
        Assert.Equal("2024-03-04", d.Result.Date);
    }

    [AvaloniaFact]
    public void Chapter_Ok_BlankTitle_NoResult()
    {
        var d = new ChapterDialog("t", "   ");
        DialogHost.Click(d, "OnOk");
        Assert.Null(d.Result);
        Assert.False(d.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void Chapter_Cancel_And_Escape()
    {
        var d = new ChapterDialog("t", "x");
        DialogHost.Click(d, "OnCancel");
        Assert.Null(d.Result);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new ChapterDialog("t", "x");
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    // ── SceneDialog ─────────────────────────────────────────────────
    [AvaloniaFact]
    public void Scene_Ok_BuildsResult()
    {
        var chapters = new[] { new SceneChapterOption("g1", "C1"), new SceneChapterOption("g2", "C2") };
        var d = new SceneDialog(chapters, "t", "Scene", "2024-01-02", "g2");
        DialogHost.Show(d);
        Assert.Equal("C2", chapters[1].ToString()); // SceneChapterOption.ToString override
        DialogHost.Click(d, "OnOk");
        Assert.NotNull(d.Result);
        Assert.Equal("Scene", d.Result!.Title);
        Assert.Equal("g2", d.Result.ChapterGuid);
        Assert.Equal("2024-01-02", d.Result.Date);
    }

    [AvaloniaFact]
    public void Scene_Ok_BlankTitle_NoResult()
    {
        var chapters = new[] { new SceneChapterOption("g1", "C1") };
        var d = new SceneDialog(chapters, "t", "  ");
        DialogHost.Click(d, "OnOk");
        Assert.Null(d.Result);
    }

    [AvaloniaFact]
    public void Scene_Cancel_And_Escape()
    {
        var d = new SceneDialog(new[] { new SceneChapterOption("g1", "C1") });
        DialogHost.Click(d, "OnCancel");
        Assert.Null(d.Result);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new SceneDialog(new[] { new SceneChapterOption("g1", "C1") });
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    // ── DatePickerDialog ────────────────────────────────────────────
    [AvaloniaFact]
    public void DatePicker_Ok_Clear_Cancel_Escape()
    {
        var d = new DatePickerDialog("t", "p", "2024-05-06");
        DialogHost.Show(d);
        DialogHost.Click(d, "OnOk");
        Assert.Equal("2024-05-06", d.Result);

        var d2 = new DatePickerDialog("t", "p");
        DialogHost.Click(d2, "OnClear");
        Assert.Equal(string.Empty, d2.Result);

        var d3 = new DatePickerDialog("t", "p");
        DialogHost.Click(d3, "OnCancel");
        Assert.Null(d3.Result);
        Assert.True(d3.DialogClosed.Task.IsCompleted);

        var d4 = new DatePickerDialog("t", "p");
        DialogHost.PressKey(d4, Key.Escape);
        Assert.True(d4.DialogClosed.Task.IsCompleted);
    }

    // ── AutoCompleteInputDialog ─────────────────────────────────────
    [AvaloniaFact]
    public void AutoComplete_Ok_Cancel_Escape()
    {
        var d = new AutoCompleteInputDialog("p", "  pick  ", new[] { "a", "b" });
        DialogHost.Show(d);
        DialogHost.Click(d, "OnOk");
        Assert.Equal("pick", d.Result);

        var empty = new AutoCompleteInputDialog("p", "   ", new[] { "a" });
        DialogHost.Click(empty, "OnOk");
        Assert.Null(empty.Result);
        Assert.False(empty.DialogClosed.Task.IsCompleted);

        var d2 = new AutoCompleteInputDialog("p", "x", new[] { "a" });
        DialogHost.Click(d2, "OnCancel");
        Assert.Null(d2.Result);
        Assert.True(d2.DialogClosed.Task.IsCompleted);

        var d3 = new AutoCompleteInputDialog("p", "x", new[] { "a" });
        DialogHost.PressKey(d3, Key.Escape);
        Assert.True(d3.DialogClosed.Task.IsCompleted);
    }
}
