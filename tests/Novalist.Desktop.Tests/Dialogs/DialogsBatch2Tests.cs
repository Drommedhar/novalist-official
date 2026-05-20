using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Desktop.Tests.Dialogs;

[Collection("Avalonia")]
public class DialogsBatch2Tests
{
    private static readonly string Bundled =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");

    private static void InitLoc() => Loc.Instance.Initialize(Bundled, "en");

    // ── AddImageSourceDialog ────────────────────────────────────────
    [AvaloniaFact]
    public void AddImageSource_AllChoices()
    {
        var d = new AddImageSourceDialog();
        DialogHost.Show(d);
        DialogHost.Click(d, "OnSelectFromLibrary");
        Assert.Equal(AddImageSourceChoice.Library, d.Result);

        var d2 = new AddImageSourceDialog();
        DialogHost.Click(d2, "OnImportFile");
        Assert.Equal(AddImageSourceChoice.Import, d2.Result);

        var d3 = new AddImageSourceDialog();
        DialogHost.Click(d3, "OnFromClipboard");
        Assert.Equal(AddImageSourceChoice.Clipboard, d3.Result);

        var d4 = new AddImageSourceDialog();
        DialogHost.Click(d4, "OnFromUrl");
        Assert.Equal(AddImageSourceChoice.Url, d4.Result);

        var d5 = new AddImageSourceDialog();
        DialogHost.Click(d5, "OnCancel");
        Assert.Null(d5.Result);

        var d6 = new AddImageSourceDialog();
        DialogHost.PressKey(d6, Key.Escape);
        Assert.Null(d6.Result);
        Assert.True(d6.DialogClosed.Task.IsCompleted);
    }

    // ── EntityCreationDialog ────────────────────────────────────────
    [AvaloniaFact]
    public void EntityCreation_WithTemplates_OkAndWizard()
    {
        InitLoc();
        var templates = new List<TemplateOption> { new("t1", "Hero"), new("t2", "Villain") };
        var d = new EntityCreationDialog("t", "Name?", templates);
        DialogHost.Show(d);
        var inputBox = d.GetVisualNamed<TextBox>("InputBox");
        inputBox!.Text = "  Alice  ";
        var combo = d.GetVisualNamed<ComboBox>("TemplateComboBox");
        combo!.SelectedIndex = 1; // first real template (index 0 = none)
        var wiz = d.GetVisualNamed<CheckBox>("UseWizardCheck");
        if (wiz != null) wiz.IsChecked = true;
        DialogHost.Click(d, "OnOk");
        Assert.Equal("Alice", d.ResultName);
        Assert.Equal("t1", d.ResultTemplateId);
        Assert.True(d.ResultUseWizard);
    }

    [AvaloniaFact]
    public void EntityCreation_NoTemplates_BlankName_NoResult_And_Escape()
    {
        InitLoc();
        var d = new EntityCreationDialog("t", "Name?", new List<TemplateOption>());
        DialogHost.Click(d, "OnOk");
        Assert.Null(d.ResultName);

        DialogHost.Click(d, "OnCancel");
        Assert.Null(d.ResultName);

        var d2 = new EntityCreationDialog("t", "Name?", new List<TemplateOption>());
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void EntityCreation_NoneTemplateSelected_NullTemplateId()
    {
        InitLoc();
        var d = new EntityCreationDialog("t", "p", new List<TemplateOption> { new("t1", "Hero") });
        DialogHost.Show(d);
        d.GetVisualNamed<TextBox>("InputBox")!.Text = "Bob";
        d.GetVisualNamed<ComboBox>("TemplateComboBox")!.SelectedIndex = 0; // "none"
        DialogHost.Click(d, "OnOk");
        Assert.Equal("Bob", d.ResultName);
        Assert.Null(d.ResultTemplateId);
        Assert.False(d.ResultUseWizard);
    }

    [AvaloniaFact]
    public void TemplateOption_ToString_IsName()
        => Assert.Equal("Hero", new TemplateOption("t1", "Hero").ToString());

    // ── SmartListEditorDialog ───────────────────────────────────────
    [AvaloniaFact]
    public void SmartList_New_Ok_BuildsList()
    {
        var d = new SmartListEditorDialog((SmartList?)null);
        d.GetVisualNamed<TextBox>("NameBox")!.Text = "  Drafts  ";
        d.GetVisualNamed<TextBox>("PovBox")!.Text = " Alice ";
        d.GetVisualNamed<TextBox>("TagBox")!.Text = " tagx ";
        DialogHost.Click(d, "OnOk");
        Assert.NotNull(d.Result);
        Assert.Equal("Drafts", d.Result!.Name);
        Assert.Equal("Alice", d.Result.PovContains);
        Assert.Equal("tagx", d.Result.Tag);
        Assert.False(string.IsNullOrEmpty(d.Result.Id));
    }

    [AvaloniaFact]
    public void SmartList_Edit_KeepsId_AndStatus()
    {
        var src = new SmartList { Id = "keep-1", Name = "Old", ChapterStatus = "FirstDraft", PovContains = "X", Tag = "t" };
        var d = new SmartListEditorDialog(src);
        DialogHost.Click(d, "OnOk");
        Assert.Equal("keep-1", d.Result!.Id);
        Assert.Equal("FirstDraft", d.Result.ChapterStatus);
    }

    [AvaloniaFact]
    public void SmartList_BlankName_NoResult_Cancel_Escape()
    {
        var d = new SmartListEditorDialog((SmartList?)null);
        d.GetVisualNamed<TextBox>("NameBox")!.Text = "   ";
        DialogHost.Click(d, "OnOk");
        Assert.Null(d.Result);

        DialogHost.Click(d, "OnCancel");
        Assert.Null(d.Result);

        var d2 = new SmartListEditorDialog((SmartList?)null);
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    // ── StoryDateRangeDialog ────────────────────────────────────────
    [AvaloniaFact]
    public void StoryDateRange_Initial_Ok_Clear_Cancel_Escape()
    {
        var initial = new StoryDateRange { Start = "2024-01-02", End = "2024-01-05", StartTime = "08:30", EndTime = "17:00", Note = "n" };
        var d = new StoryDateRangeDialog("p", initial);
        DialogHost.Show(d);
        DialogHost.Click(d, "OnOk");
        Assert.NotNull(d.Result);
        Assert.Equal("2024-01-02", d.Result!.Start);
        Assert.Equal("08:30", d.Result.StartTime);
        Assert.Equal("17:00", d.Result.EndTime);

        var d2 = new StoryDateRangeDialog("p", null);
        DialogHost.Click(d2, "OnClear");
        Assert.True(d2.Cleared);
        Assert.Null(d2.Result);

        var d3 = new StoryDateRangeDialog("p", null);
        DialogHost.Click(d3, "OnCancel");
        Assert.False(d3.Cleared);
        Assert.Null(d3.Result);

        var d4 = new StoryDateRangeDialog("p", null);
        DialogHost.PressKey(d4, Key.Escape);
        Assert.True(d4.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void StoryDateRange_BadDatesTimes_Ignored()
    {
        // unparsable date/time strings exercise the false-returning parse branches.
        var initial = new StoryDateRange { Start = "not-a-date", End = "", StartTime = "99:99:99x", EndTime = " " };
        var d = new StoryDateRangeDialog("p", initial);
        DialogHost.Show(d);
        DialogHost.Click(d, "OnOk");
        Assert.Equal(string.Empty, d.Result!.Start);
        Assert.Equal(string.Empty, d.Result.StartTime);
    }

    // ── EntityTypeManagerDialog ─────────────────────────────────────
    [AvaloniaFact]
    public void EntityTypeManager_Save_Cancel_Escape()
    {
        var d = new EntityTypeManagerDialog(new EntityTypeManagerViewModel());
        DialogHost.Click(d, "OnSave");
        Assert.True(d.Saved);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new EntityTypeManagerDialog(new EntityTypeManagerViewModel());
        DialogHost.Click(d2, "OnCancel");
        Assert.False(d2.Saved);

        var d3 = new EntityTypeManagerDialog(new EntityTypeManagerViewModel());
        DialogHost.PressKey(d3, Key.Escape);
        Assert.False(d3.Saved);
        Assert.True(d3.DialogClosed.Task.IsCompleted);
    }

    // ── TemplateEditorDialog ────────────────────────────────────────
    [AvaloniaFact]
    public void TemplateEditor_Save_Cancel_Escape()
    {
        var d = new TemplateEditorDialog(new TemplateEditorViewModel("character"));
        DialogHost.Click(d, "OnSave");
        Assert.True(d.Saved);

        var d2 = new TemplateEditorDialog(new TemplateEditorViewModel("character"));
        DialogHost.Click(d2, "OnCancel");
        Assert.False(d2.Saved);

        var d3 = new TemplateEditorDialog(new TemplateEditorViewModel("character"));
        DialogHost.PressKey(d3, Key.Escape);
        Assert.True(d3.DialogClosed.Task.IsCompleted);
    }

    // ── FindReplaceDialog ───────────────────────────────────────────
    [AvaloniaFact]
    public void FindReplace_Close_Escape_DoubleTap()
    {
        var svc = Substitute.For<IFindReplaceService>();
        FindMatch? jumped = null;
        var vm = new FindReplaceViewModel(svc, Substitute.For<ISnapshotService>(), null,
            m => { jumped = m; return Task.CompletedTask; });
        var d = new FindReplaceDialog(vm);

        DialogHost.Show(d);
        var list = d.GetVisualNamed<ListBox>("ResultsList")!;
        var match = new FindMatch { MatchedText = "z" };
        list.ItemsSource = new[] { match };
        list.SelectedItem = match;
        DialogHost.Invoke(d, "OnResultDoubleTapped", null, null);
        Assert.Same(match, jumped);

        DialogHost.Click(d, "OnClose");
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new FindReplaceDialog(vm);
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    // ── ProjectImagePickerDialog ────────────────────────────────────
    [AvaloniaFact]
    public void ProjectImagePicker_Render_Filter_Click_Cancel_Escape()
    {
        InitLoc();
        var paths = new[] { "C:/a/Hero.png", "C:/a/Villain.jpg", "", "C:/a/Map.png" };
        var d = new ProjectImagePickerDialog(paths, "C:/a/Hero.png");
        DialogHost.Show(d);

        // filter to a single match
        var search = d.GetVisualNamed<TextBox>("SearchBox")!;
        search.Text = "Hero";
        DialogHost.RunJobs();

        // click an image via a Button carrying the option as Tag
        var clickBtn = new Button { Tag = new ProjectImageOption("Hero", "C:/a/Hero.png", true) };
        DialogHost.Invoke(d, "OnImageClicked", clickBtn, new Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal("C:/a/Hero.png", d.Result);

        var d2 = new ProjectImagePickerDialog(paths, null);
        DialogHost.Click(d2, "OnCancel");
        Assert.Null(d2.Result);

        var d3 = new ProjectImagePickerDialog(paths, null);
        DialogHost.PressKey(d3, Key.Escape);
        Assert.True(d3.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void ProjectImagePicker_NonButtonSender_NoResult()
    {
        InitLoc();
        var d = new ProjectImagePickerDialog(new[] { "C:/a/x.png" }, null);
        DialogHost.Invoke(d, "OnImageClicked", new TextBox(), new Avalonia.Interactivity.RoutedEventArgs());
        Assert.Null(d.Result);
    }

    // ── InverseRelationshipDialog ───────────────────────────────────
    [AvaloniaFact]
    public void InverseRelationship_Render_Suggestion_Ok_Cancel_Escape()
    {
        InitLoc();
        // 10+ "mentor*" suggestions so RenderSuggestions hits its >= 8 cap (break branch).
        var many = new[]
        {
            "mentor1", "mentor2", "mentor3", "mentor4", "mentor5",
            "mentor6", "mentor7", "mentor8", "mentor9", "mother", "father", "father", "  ",
        };
        var d = new InverseRelationshipDialog("parent", "Bob", "Alice", many, "fa");
        DialogHost.Show(d);

        var input = d.GetVisualNamed<TextBox>("InputBox")!;
        input.Text = "mentor"; // matches 9 -> exercises the >= 8 break
        DialogHost.RunJobs();
        input.Text = "moth"; // narrow back down
        DialogHost.RunJobs();

        // click a suggestion button
        var btn = new Button { Content = "mother" };
        DialogHost.Invoke(d, "OnSuggestionClicked", btn, new Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal("mother", d.Result);

        var d2 = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "x");
        DialogHost.Click(d2, "OnOk");
        Assert.Equal("x", d2.Result);

        var d3 = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "");
        DialogHost.Click(d3, "OnOk"); // empty, not allowed -> no result
        Assert.Null(d3.Result);

        var d4 = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "");
        DialogHost.Click(d4, "OnCancel");
        Assert.Null(d4.Result);

        var d5 = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "");
        DialogHost.PressKey(d5, Key.Escape);
        Assert.True(d5.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void InverseRelationship_AllowEmpty_NonButtonSender()
    {
        InitLoc();
        var d = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "", allowEmpty: true);
        DialogHost.Click(d, "OnOk");
        Assert.Equal(string.Empty, d.Result);

        // non-button sender path of OnSuggestionClicked
        var d2 = new InverseRelationshipDialog("parent", "Bob", "Alice", new[] { "father" }, "x");
        DialogHost.Invoke(d2, "OnSuggestionClicked", new TextBox(), new Avalonia.Interactivity.RoutedEventArgs());
        Assert.Null(d2.Result);
    }

    // ── BusyProgressDialog ──────────────────────────────────────────
    [AvaloniaFact]
    public void BusyProgress_Setters_And_Cancel()
    {
        InitLoc();
        var opts = new BusyProgressOptions { Title = "T", InitialStatus = "S", AllowCancel = true, ShowProgressBar = true, IsModal = false };
        var d = new BusyProgressDialog(opts);
        Assert.False(d.IsModal);

        bool cancelled = false;
        d.Cancelled += () => cancelled = true;

        d.SetStatus("new status");
        d.SetProgress(2.0);   // clamps to 1
        d.SetProgress(-1.0);  // clamps to 0
        d.SetTitle("title2");
        d.SetIndeterminate(true);
        d.SetDetails(new[] { "a", "b" });
        d.SetDetails(null);
        DialogHost.RunJobs();

        DialogHost.PressKey(d, Key.Escape); // AllowCancel -> TriggerCancel
        Assert.True(cancelled);

        // OnCancel is a no-op the second time (already cancelled)
        DialogHost.Click(d, "OnCancel");
        Assert.True(d.CancellationToken.IsCancellationRequested);
    }

    [AvaloniaFact]
    public void BusyProgress_DefaultCtor_Dispose_BothThreads()
    {
        InitLoc();
        var d = new BusyProgressDialog(); // default options, modal
        Assert.True(d.IsModal);
        d.Dispose(); // UI-thread path (CheckAccess true)
        Assert.True(d.IsClosed);
        d.Dispose(); // already closed -> early return

        // off-thread Dispose hits the Post branch
        var d2 = new BusyProgressDialog();
        Task.Run(() => d2.Dispose()).GetAwaiter().GetResult();
        DialogHost.RunJobs();
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void BusyProgress_Escape_NoCancelWhenDisallowed()
    {
        InitLoc();
        var d = new BusyProgressDialog(new BusyProgressOptions { AllowCancel = false });
        DialogHost.PressKey(d, Key.Escape);
        Assert.False(d.CancellationToken.IsCancellationRequested);
    }

    // ── UpdateDialog (OnDownload excluded: launches installer / shuts app) ──
    [AvaloniaFact]
    public void Update_Ctor_Skip_Escape()
    {
        InitLoc();
        var info = new UpdateInfo { Version = "9.9.9", Body = "notes" };
        var d = new UpdateDialog(info, Substitute.For<IUpdateService>());
        DialogHost.Show(d);

        DialogHost.PressKey(d, Key.Escape); // not downloading -> closes
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new UpdateDialog(info, Substitute.For<IUpdateService>());
        DialogHost.Click(d2, "OnSkip"); // _cts null -> no throw
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void Update_DefaultCtor_NoBody()
    {
        InitLoc();
        var info = new UpdateInfo { Version = "1.0.0", Body = "" };
        var d = new UpdateDialog(info, Substitute.For<IUpdateService>());
        DialogHost.PressKey(d, Key.Escape);
        Assert.True(d.DialogClosed.Task.IsCompleted);
        _ = new UpdateDialog(); // parameterless ctor (real UpdateService)
    }
}
