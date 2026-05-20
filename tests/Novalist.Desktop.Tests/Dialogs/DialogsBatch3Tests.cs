using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Desktop.Tests.Dialogs;

[Collection("Avalonia")]
public class DialogsBatch3Tests
{
    private static readonly string Bundled =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");

    private static void InitLoc() => Loc.Instance.Initialize(Bundled, "en");

    private static void SetField(object target, string name, object? value)
        => target.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);

    // ── CommandPaletteDialog ────────────────────────────────────────
    [AvaloniaFact]
    public void CommandPalette_Build_Filter_Navigate_Execute()
    {
        InitLoc();
        bool executed = false;
        var descriptors = new List<HotkeyDescriptor>
        {
            new() { ActionId = "a.one", DisplayName = "Open Things", Category = "File", OnExecute = () => executed = true },
            new() { ActionId = "a.two", DisplayName = "Close Things", Category = "Edit", CanExecute = () => true,
                    OnExecute = () => throw new InvalidOperationException("boom") }, // covers swallow-catch
            new() { ActionId = "a.skip", DisplayName = "Hidden", Category = "Edit", CanExecute = () => false }, // skipped
        };
        var hk = Substitute.For<IHotkeyService>();
        hk.GetAllDescriptors().Returns(descriptors);
        hk.GetGesture("a.one").Returns("Control+OemPlus+D1");          // HumanizeGesture replacements
        hk.GetGesture("a.two").Returns(string.Empty);                  // HumanizeGesture empty short-circuit
        hk.GetGesture("a.skip").Returns("Control+D2");

        var d = new CommandPaletteDialog(hk);
        DialogHost.Show(d);

        var query = d.GetVisualNamed<TextBox>("QueryBox")!;
        query.Text = "things"; // TextChanged -> Refilter (token path, matches)
        DialogHost.RunJobs();
        query.Text = "things zzqqxx"; // second token matches nothing -> filter returns false
        DialogHost.RunJobs();
        query.Text = "  ";     // whitespace -> Refilter (all path)
        DialogHost.RunJobs();

        // navigation
        DialogHost.Invoke(d, "OnQueryKeyDown", null, Key2(Key.Down));
        DialogHost.Invoke(d, "OnQueryKeyDown", null, Key2(Key.Up));
        DialogHost.Invoke(d, "OnQueryKeyDown", null, Key2(Key.PageDown)); // no-op branch
        DialogHost.Invoke(d, "OnListKeyDown", null, Key2(Key.Up));        // non-enter no-op

        // execute the throwing item via list-enter, then run the deferred post
        var list = d.GetVisualNamed<ListBox>("ResultsList")!;
        list.SelectedItem = list.Items.Cast<CommandPaletteItem>().First(i => i.Descriptor.ActionId == "a.two");
        DialogHost.Invoke(d, "OnListKeyDown", null, Key2(Key.Enter));
        DialogHost.RunJobs(); // runs deferred OnExecute (throws -> swallowed)
        Assert.True(d.DialogClosed.Task.IsCompleted);

        // double-tap executes the non-throwing one
        var d2 = new CommandPaletteDialog(hk);
        DialogHost.Show(d2);
        var list2 = d2.GetVisualNamed<ListBox>("ResultsList")!;
        list2.SelectedItem = list2.Items.Cast<CommandPaletteItem>().First(i => i.Descriptor.ActionId == "a.one");
        DialogHost.Invoke(d2, "OnDoubleTap", null, null);
        DialogHost.RunJobs();
        Assert.True(executed);

        // query Enter path
        var d3 = new CommandPaletteDialog(hk);
        DialogHost.Invoke(d3, "OnQueryKeyDown", null, Key2(Key.Enter));
        DialogHost.RunJobs();

        // ExecuteSelected with no selection -> early return
        var d4 = new CommandPaletteDialog(hk);
        d4.GetVisualNamed<ListBox>("ResultsList")!.SelectedItem = null;
        DialogHost.Invoke(d4, "ExecuteSelected");

        // Escape
        var d5 = new CommandPaletteDialog(hk);
        DialogHost.PressKey(d5, Key.Escape);
        Assert.True(d5.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void CommandPalette_EmptyDescriptors_NoSelection()
    {
        InitLoc();
        var hk = Substitute.For<IHotkeyService>();
        hk.GetAllDescriptors().Returns(new List<HotkeyDescriptor>());
        var d = new CommandPaletteDialog(hk);
        DialogHost.Invoke(d, "OnQueryKeyDown", null, Key2(Key.Down)); // ItemCount 0 branch
        DialogHost.Invoke(d, "OnQueryKeyDown", null, Key2(Key.Up));
        Assert.False(d.DialogClosed.Task.IsCompleted);
    }

    private static KeyEventArgs Key2(Key k)
        => new() { Key = k, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent };

    // ── WizardDialog ────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task Wizard_CloseRequested_CompletesDialog()
    {
        var runner = new WizardRunner(Substitute.For<IFileService>());
        await runner.StartAsync(new WizardDefinition
        {
            Id = "w",
            DisplayName = "Wiz",
            Steps = new List<WizardStep> { new TextStep { Id = "a", Title = "A", Skippable = true } },
        });
        var vm = new WizardDialogViewModel(runner);
        var d = new WizardDialog(vm);
        await vm.CancelCommand.ExecuteAsync(null); // -> runner.Cancelled -> CloseRequested
        Assert.True(d.DialogClosed.Task.IsCompleted);
    }

    // ── MapProfileEditorDialog ──────────────────────────────────────
    [AvaloniaFact]
    public void MapProfile_RoundTrip_AddDelete_OkCancel()
    {
        var existing = new[]
        {
            new MapProfile
            {
                Id = "p1", Name = "Road A", Kind = "road", DefaultWidth = 20, CasingColor = "#112233", CasingExtra = 2,
                Bands = { new MapProfileBand { From = -1, To = 1, Color = "#aabbcc" } },
                Markings = { new MapProfileMarking { Offset = 0, Color = "#ffffff", Width = 1.5, Dash = new List<double> { 2, 3 } } },
            },
            new MapProfile { Id = "", Name = "", Kind = "", CasingColor = "not-a-color" }, // fallback branches
        };
        var d = new MapProfileEditorDialog(existing);
        Assert.Equal(2, d.Profiles.Count);

        DialogHost.Invoke(d, "OnAddProfile", null, Routed());
        Assert.Equal(3, d.Profiles.Count);

        var list = d.GetVisualNamed<ListBox>("ProfileList")!;
        list.SelectedItem = d.Profiles[2];
        DialogHost.Invoke(d, "OnAddBand", null, Routed());
        DialogHost.Invoke(d, "OnAddMarking", null, Routed());

        // delete band / marking via Tag-carrying control
        var prof = d.Profiles[2];
        var band = prof.Bands[0];
        DialogHost.Invoke(d, "OnDeleteBand", new Button { Tag = band }, Routed());
        Assert.DoesNotContain(band, prof.Bands);
        var mk = prof.Markings[0];
        DialogHost.Invoke(d, "OnDeleteMarking", new Button { Tag = mk }, Routed());
        Assert.DoesNotContain(mk, prof.Markings);

        DialogHost.Invoke(d, "OnDeleteProfile", null, Routed());
        Assert.Equal(2, d.Profiles.Count);

        Assert.Equal(d.Profiles[0].Name, d.Profiles[0].ToString()); // ProfileRow.ToString override

        DialogHost.Invoke(d, "OnOk", null, Routed());
        Assert.NotNull(d.Result);
        Assert.Equal(2, d.Result!.Count);
        // first profile preserves id/name; second got generated id + "Profile" name fallback
        Assert.Equal("p1", d.Result[0].Id);
        Assert.StartsWith("profile-", d.Result[1].Id);
        Assert.Equal("Profile", d.Result[1].Name);
    }

    [AvaloniaFact]
    public void MapProfile_Cancel_And_Escape_NullResult()
    {
        var d = new MapProfileEditorDialog(Array.Empty<MapProfile>());
        DialogHost.Invoke(d, "OnCancel", null, Routed());
        Assert.Null(d.Result);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new MapProfileEditorDialog(Array.Empty<MapProfile>());
        DialogHost.PressKey(d2, Key.Escape);
        Assert.Null(d2.Result);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void MapProfile_DeleteBand_WrongSender_NoOp()
    {
        var d = new MapProfileEditorDialog(new[] { new MapProfile { Id = "p", Name = "n", Bands = { new MapProfileBand() } } });
        d.GetVisualNamed<ListBox>("ProfileList")!.SelectedItem = d.Profiles[0];
        DialogHost.Invoke(d, "OnDeleteBand", new Button(), Routed()); // no Tag -> no-op
        DialogHost.Invoke(d, "OnDeleteMarking", new Button(), Routed());
        Assert.Single(d.Profiles[0].Bands);
    }

    private static Avalonia.Interactivity.RoutedEventArgs Routed() => new();

    // ── SnapshotsDialog ─────────────────────────────────────────────
    [AvaloniaFact]
    public void Snapshots_Reload_Take_Restore_Delete_Compare_Close()
    {
        InitLoc();
        var svc = Substitute.For<ISnapshotService>();
        var snap = new SceneSnapshot { Id = "s1", Label = "Label", WordCount = 10, CreatedAt = DateTime.UtcNow };
        var unlabeled = new SceneSnapshot { Id = "s2", Label = "", WordCount = 0, CreatedAt = DateTime.UtcNow };
        svc.ListAsync(Arg.Any<SceneData>()).Returns(new List<SceneSnapshot> { snap, unlabeled });
        svc.TakeAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(snap);
        svc.RestoreAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(true);
        svc.DeleteAsync(Arg.Any<SceneData>(), Arg.Any<string>()).Returns(Task.CompletedTask);

        var chapter = new ChapterData { Title = "Chap" };
        var scene = new SceneData { Title = "Scn" };
        bool restored = false;
        SceneSnapshot? compared = null;
        var d = new SnapshotsDialog(svc, chapter, scene, () => restored = true, s => { compared = s; return Task.CompletedTask; });
        DialogHost.RunJobs(); // ctor ReloadAsync

        var row = new SnapshotsDialog.SnapshotRow(snap);
        var btn = new Button { Tag = row };

        DialogHost.Invoke(d, "OnRestoreItem", btn, Routed());
        Assert.True(row.IsConfirmingRestore);
        DialogHost.Invoke(d, "OnCancelRestore", btn, Routed());
        Assert.False(row.IsConfirmingRestore);
        DialogHost.Invoke(d, "OnConfirmRestore", btn, Routed());
        DialogHost.RunJobs();
        Assert.True(restored);

        DialogHost.Invoke(d, "OnDeleteItem", btn, Routed());
        Assert.True(row.IsConfirmingDelete);
        DialogHost.Invoke(d, "OnCancelDelete", btn, Routed());
        Assert.False(row.IsConfirmingDelete);
        DialogHost.Invoke(d, "OnConfirmDelete", btn, Routed());
        DialogHost.RunJobs();

        DialogHost.Invoke(d, "OnCompareItem", btn, Routed());
        DialogHost.RunJobs();
        Assert.Same(snap, compared);

        // take snapshot
        d.GetVisualNamed<TextBox>("LabelBox")!.Text = "My label";
        DialogHost.Invoke(d, "OnTakeSnapshot", null, Routed());
        DialogHost.RunJobs();

        DialogHost.Invoke(d, "OnClose", null, Routed());
        Assert.True(d.DialogClosed.Task.IsCompleted);

        // unlabeled row uses the "unlabeled" loc string
        var unl = new SnapshotsDialog.SnapshotRow(unlabeled);
        Assert.False(string.IsNullOrEmpty(unl.DisplayLabel));
    }

    [AvaloniaFact]
    public void Snapshots_NullSender_And_Escape_And_FailingService()
    {
        InitLoc();
        var svc = Substitute.For<ISnapshotService>();
        svc.ListAsync(Arg.Any<SceneData>()).Returns(new List<SceneSnapshot>());
        svc.TakeAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>())
           .Returns<SceneSnapshot>(_ => throw new InvalidOperationException("fail")); // catch path
        svc.RestoreAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>())
           .Returns<bool>(_ => throw new InvalidOperationException("fail"));
        svc.DeleteAsync(Arg.Any<SceneData>(), Arg.Any<string>())
           .Returns<Task>(_ => throw new InvalidOperationException("fail"));

        var d = new SnapshotsDialog(svc, new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null,
            _ => throw new InvalidOperationException("fail"));
        DialogHost.RunJobs();

        // null-sender RowFrom -> early returns
        DialogHost.Invoke(d, "OnRestoreItem", null, Routed());
        DialogHost.Invoke(d, "OnCancelRestore", null, Routed());
        DialogHost.Invoke(d, "OnDeleteItem", null, Routed());
        DialogHost.Invoke(d, "OnCancelDelete", null, Routed());

        var btn = new Button { Tag = new SnapshotsDialog.SnapshotRow(new SceneSnapshot { Id = "x" }) };
        DialogHost.Invoke(d, "OnConfirmRestore", btn, Routed()); DialogHost.RunJobs(); // throws -> caught
        DialogHost.Invoke(d, "OnConfirmDelete", btn, Routed()); DialogHost.RunJobs();
        DialogHost.Invoke(d, "OnCompareItem", btn, Routed()); DialogHost.RunJobs();
        DialogHost.Invoke(d, "OnTakeSnapshot", null, Routed()); DialogHost.RunJobs();

        DialogHost.PressKey(d, Key.Escape);
        Assert.True(d.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void Snapshots_DefaultCtor_NoService()
    {
        var d = new SnapshotsDialog();
        // ReloadAsync guards on null service (the early return).
        DialogHost.Invoke(d, "ReloadAsync");
        DialogHost.RunJobs();
        // null-sender / null-service guard returns in the confirm + compare handlers.
        DialogHost.Invoke(d, "OnConfirmRestore", null, Routed());
        DialogHost.Invoke(d, "OnConfirmDelete", null, Routed());
        DialogHost.Invoke(d, "OnCompareItem", null, Routed());
        DialogHost.Invoke(d, "OnTakeSnapshot", null, Routed());
        DialogHost.RunJobs();
        DialogHost.PressKey(d, Key.Escape);
        Assert.True(d.DialogClosed.Task.IsCompleted);
    }

    // ── SnapshotCompareDialog (apply path excluded) ─────────────────
    [AvaloniaFact]
    public void SnapshotCompare_BuildRows_AllDiffTypes_SelectAllNone_Close()
    {
        InitLoc();
        var snapshot = new SceneSnapshot
        {
            Id = "s", CreatedAt = DateTime.UtcNow,
            Content = "<p>same line</p><p>removed line</p><p>changed alpha beta</p>",
        };
        var current = "<p>same line</p><p>added line</p><p>changed alpha gamma</p>";
        var d = new SnapshotCompareDialog(snapshot, current, Substitute.For<IProjectService>(),
            new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null);
        DialogHost.Show(d); // OnAttachedToVisualTree -> SizeToTopLevel

        DialogHost.Invoke(d, "OnSelectAll", null, Routed());
        DialogHost.Invoke(d, "OnSelectNone", null, Routed());
        DialogHost.Invoke(d, "OnClose", null, Routed());
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new SnapshotCompareDialog(snapshot, current, Substitute.For<IProjectService>(),
            new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null);
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    [AvaloniaFact]
    public void SnapshotCompare_LeftOnly_RightOnly_Rows()
    {
        InitLoc();
        // snapshot has an extra line ("B") absent from current -> left-only row.
        var leftOnly = new SnapshotCompareDialog(
            new SceneSnapshot { Id = "s", Content = "<p>A</p><p>B</p><p>C</p>" },
            "<p>A</p><p>C</p>", Substitute.For<IProjectService>(),
            new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null);
        DialogHost.Show(leftOnly);
        DialogHost.Invoke(leftOnly, "OnSelectAll", null, Routed());

        // current has an extra line ("B") absent from snapshot -> right-only row.
        var rightOnly = new SnapshotCompareDialog(
            new SceneSnapshot { Id = "s", Content = "<p>A</p><p>C</p>" },
            "<p>A</p><p>B</p><p>C</p>", Substitute.For<IProjectService>(),
            new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null);
        DialogHost.Show(rightOnly);
        DialogHost.Invoke(rightOnly, "OnSelectAll", null, Routed());
    }

    [AvaloniaFact]
    public void SnapshotCompare_HostResize_TriggersSizeToTopLevel()
    {
        InitLoc();
        var d = new SnapshotCompareDialog(
            new SceneSnapshot { Id = "s", Content = "<p>line</p>" },
            "<p>line</p>", Substitute.For<IProjectService>(),
            new ChapterData { Title = "C" }, new SceneData { Title = "S" }, null);
        var win = new Window { Width = 800, Height = 600, Content = d };
        try
        {
            win.Show();
            DialogHost.RunJobs();
            win.Width = 1100;
            win.Height = 850;
            DialogHost.RunJobs();
        }
        finally
        {
            win.Content = null;
            win.Close();
            DialogHost.RunJobs();
        }
    }

    // ── ImportPluginDialog (browse + import execution excluded: interop) ──
    [AvaloniaFact]
    public void Import_ProjectSelectionChanged_PrefillsNames()
    {
        InitLoc();
        var d = new ImportPluginDialog();
        DialogHost.Show(d);

        var detection = new PluginDetectionResult();
        detection.Projects.Add(new PluginProjectInfo { Name = "Proj", Path = "Proj" });
        SetField(d, "_detectionResult", detection);
        var combo = d.GetVisualNamed<ComboBox>("ProjectComboBox")!;
        combo.ItemsSource = detection.Projects;

        // empty name boxes -> prefilled from selection
        combo.SelectedItem = detection.Projects[0];
        DialogHost.Invoke(d, "OnProjectSelectionChanged", combo, null);
        Assert.Equal("Proj", d.GetVisualNamed<TextBox>("ProjectNameBox")!.Text);
        Assert.Equal("Proj", d.GetVisualNamed<TextBox>("BookNameBox")!.Text);

        // non-PluginProjectInfo selection -> no-op branch
        combo.ItemsSource = new[] { "x" };
        combo.SelectedItem = "x";
        DialogHost.Invoke(d, "OnProjectSelectionChanged", combo, null);
    }

    [AvaloniaFact]
    public void Import_TryBuildRequest_AllValidationBranches()
    {
        InitLoc();
        var d = new ImportPluginDialog();
        DialogHost.Show(d);

        // no vault / no detection -> false (selectVaultFirst)
        Assert.False(TryBuild(d));
        Assert.True(d.GetVisualNamed<TextBlock>("ErrorText")!.IsVisible);

        var detection = new PluginDetectionResult();
        detection.Projects.Add(new PluginProjectInfo { Name = "P", Path = "" });
        SetField(d, "_detectionResult", detection);
        d.GetVisualNamed<TextBox>("VaultPathBox")!.Text = "C:/vault";

        // missing project name -> false
        d.GetVisualNamed<TextBox>("ProjectNameBox")!.Text = "";
        Assert.False(TryBuild(d));

        // missing output -> false
        d.GetVisualNamed<TextBox>("ProjectNameBox")!.Text = "Proj";
        d.GetVisualNamed<TextBox>("OutputPathBox")!.Text = "";
        Assert.False(TryBuild(d));

        // all present, blank book name -> defaults to project name, returns true
        d.GetVisualNamed<TextBox>("OutputPathBox")!.Text = "C:/out";
        d.GetVisualNamed<TextBox>("BookNameBox")!.Text = "";
        object?[] args = { null, null, null, null };
        var mi = typeof(ImportPluginDialog).GetMethod("TryBuildImportRequest",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)mi.Invoke(d, args)!);
        Assert.Equal("Proj", args[2]); // bookName defaulted
    }

    [AvaloniaFact]
    public void Import_Cancel_And_Escape()
    {
        InitLoc();
        var d = new ImportPluginDialog();
        DialogHost.Show(d);

        // importing guard: cancel + escape no-op
        SetField(d, "_importing", true);
        DialogHost.Invoke(d, "OnCancel", null, Routed());
        DialogHost.PressKey(d, Key.Escape);
        Assert.False(d.DialogClosed.Task.IsCompleted);

        SetField(d, "_importing", false);
        DialogHost.Invoke(d, "OnCancel", null, Routed());
        Assert.Null(d.ImportedProjectPath);
        Assert.True(d.DialogClosed.Task.IsCompleted);

        var d2 = new ImportPluginDialog();
        DialogHost.PressKey(d2, Key.Escape);
        Assert.True(d2.DialogClosed.Task.IsCompleted);
    }

    private static bool TryBuild(ImportPluginDialog d)
    {
        object?[] args = { null, null, null, null };
        var mi = typeof(ImportPluginDialog).GetMethod("TryBuildImportRequest",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(d, args)!;
    }
}
