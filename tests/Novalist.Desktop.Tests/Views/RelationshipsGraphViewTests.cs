using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

[Collection("Avalonia")]
public class RelationshipsGraphViewTests
{
    static RelationshipsGraphViewTests()
    {
        Loc.Instance.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");
        RelationshipRoles.Reload();
    }

    // Synchronous (block on ReloadAsync) so the test methods stay non-async: an `async Task`
    // test that yields would resume the xunit runner on a threadpool thread and poison
    // sibling Avalonia-collection tests (Dispatcher.VerifyAccess).
    private static RelationshipsGraphViewModel LoadedVm()
    {
        var alice = new CharacterData { Name = "Alice", Group = "Crew" };
        alice.Relationships.Add(new EntityRelationship { Role = "father", Target = "Bob" }); // family-tree (unlabeled) edges
        alice.Relationships.Add(new EntityRelationship { Role = "ally", Target = "Carol" }); // peer edge -> labeled
        var bob = new CharacterData { Name = "Bob", Group = "Crew" };
        var carol = new CharacterData { Name = "Carol" }; // ungrouped node
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { alice, bob, carol });
        var vm = new RelationshipsGraphViewModel(ent);
        vm.ReloadAsync().GetAwaiter().GetResult();
        return vm;
    }

    [AvaloniaFact]
    public void DataContext_Rebuild_PropertyChanged_GraphChanged_Unsubscribe()
    {
        var vm = LoadedVm();
        var view = new RelationshipsGraphView();

        view.DataContext = vm;        // OnDataContextChanged set-branch -> Rebuild (nodes/edges/groups)
        Assert.True(vm.Nodes.Count > 0);

        // OnVmPropertyChanged: Nodes/Edges -> Rebuild; other -> no-op
        vm.GetType(); // no-op marker
        RaisePropChanged(vm, nameof(RelationshipsGraphViewModel.Nodes));
        RaisePropChanged(vm, nameof(RelationshipsGraphViewModel.Edges));
        RaisePropChanged(vm, "SomethingElse");

        // OnGraphChanged: collection change -> Rebuild
        vm.Nodes.Add(vm.Nodes[0]);

        // unsubscribe path: swap DataContext away
        view.DataContext = "x";
        view.DataContext = null;
    }

    [AvaloniaFact]
    public void Hosted_Sized_RunsCenterOnGraph_AndLayoutUpdated()
    {
        var vm = LoadedVm();
        var view = new RelationshipsGraphView();
        var win = new Window { Width = 900, Height = 700, Content = view };
        try
        {
            win.Show();
            DialogHost.RunJobs();
            view.DataContext = vm;     // Rebuild -> ScheduleCenter (posted) + LayoutUpdated
            DialogHost.RunJobs();      // runs posted CenterOnGraph (viewport now sized)
            win.Width = 950;           // force another layout pass -> OnViewportLayoutUpdated
            DialogHost.RunJobs();
        }
        finally
        {
            win.Content = null;
            win.Close();
            DialogHost.RunJobs();
        }
    }

    [AvaloniaFact]
    public void EmptyVm_CenterOnGraph_NoNodes()
    {
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns(new List<CharacterData>());
        var vm = new RelationshipsGraphViewModel(ent);
        var view = new RelationshipsGraphView();
        var win = new Window { Width = 600, Height = 400, Content = view };
        try
        {
            win.Show();
            DialogHost.RunJobs();
            view.DataContext = vm; // Rebuild with empty graph -> CenterOnGraph early returns (no nodes/groups)
            DialogHost.RunJobs();
        }
        finally { win.Content = null; win.Close(); DialogHost.RunJobs(); }
    }

    [AvaloniaFact]
    public void CenterOnGraph_NullVm_Abort_And_ScheduleCenter_SizedBranch()
    {
        // CenterOnGraph with no DataContext -> _vm null -> abort branch.
        var bare = new RelationshipsGraphView();
        DialogHost.Invoke(bare, "CenterOnGraph");

        // ScheduleCenter sized-branch: host + size, set _centerPending, run the posted job.
        var view = new RelationshipsGraphView();
        var win = new Window { Width = 800, Height = 600, Content = view };
        try
        {
            win.Show();
            DialogHost.RunJobs();
            SetField(view, "_centerPending", true);
            DialogHost.Invoke(view, "ScheduleCenter");
            DialogHost.RunJobs(); // posted lambda: _centerPending true + viewport sized -> CenterOnGraph
        }
        finally { win.Content = null; win.Close(); DialogHost.RunJobs(); }
    }

    private static void SetField(object t, string name, object? v)
        => t.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(t, v);

    private static void RaisePropChanged(object vm, string prop)
    {
        // The view subscribed via PropertyChanged; raise it through the public event.
        var ev = vm.GetType().GetEvent(nameof(INotifyPropertyChanged.PropertyChanged));
        // Use reflection to invoke the backing delegate is brittle; instead trigger a
        // real notification by toggling a public observable property is not available,
        // so raise directly through the interface's invocation list via the field.
        var field = GetEventField(vm.GetType(), "PropertyChanged");
        var handler = (PropertyChangedEventHandler?)field?.GetValue(vm);
        handler?.Invoke(vm, new PropertyChangedEventArgs(prop));
    }

    private static System.Reflection.FieldInfo? GetEventField(Type? t, string name)
    {
        while (t != null)
        {
            var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null) return f;
            t = t.BaseType;
        }
        return null;
    }
}
