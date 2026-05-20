using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class FindReplaceViewModelTests
{
    private static FindReplaceViewModel Build(out IFindReplaceService svc,
        Func<(string, string)?>? anchor = null, Func<FindMatch, Task>? onJump = null)
    {
        svc = Substitute.For<IFindReplaceService>();
        return new FindReplaceViewModel(svc, Substitute.For<ISnapshotService>(), anchor, onJump);
    }

    [Fact]
    public void AvailableScopes_AreFindScopes()
        => Assert.Contains(FindScope.ActiveBook, Build(out _).AvailableScopes.Cast<FindScope>());

    [Fact]
    public async Task Find_EmptyPattern_NoOp()
    {
        var vm = Build(out var svc);
        await vm.FindCommand.ExecuteAsync(null);
        await svc.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
    }

    [Fact]
    public async Task Find_PopulatesResults_WithAnchor()
    {
        var vm = Build(out var svc, anchor: () => ("c1", "s1"));
        svc.FindAsync(Arg.Any<FindOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<FindMatch> { new() { MatchedText = "x" }, new() { MatchedText = "y" } });
        vm.Pattern = "x";
        await vm.FindCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);
        Assert.Contains("2 match", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Find_ServiceThrows_SetsStatus()
    {
        var vm = Build(out var svc);
        svc.FindAsync(Arg.Any<FindOptions>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<FindMatch>>>(_ => throw new InvalidOperationException("boom"));
        vm.Pattern = "x";
        await vm.FindCommand.ExecuteAsync(null);
        Assert.Equal("boom", vm.StatusText);
    }

    [Fact]
    public async Task ReplaceAll_EmptyPattern_NoOp()
    {
        var vm = Build(out var svc);
        await vm.ReplaceAllCommand.ExecuteAsync(null);
        await svc.DidNotReceiveWithAnyArgs().ReplaceAllAsync(default!, default, default);
    }

    [Fact]
    public async Task ReplaceAll_ReportsCount()
    {
        var vm = Build(out var svc);
        svc.ReplaceAllAsync(Arg.Any<FindOptions>(), Arg.Any<ISnapshotService>(), Arg.Any<CancellationToken>()).Returns(3);
        vm.Pattern = "x"; vm.Replacement = "y";
        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Contains("Replaced 3", vm.StatusText);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task ReplaceAll_ServiceThrows_SetsStatus()
    {
        var vm = Build(out var svc);
        svc.ReplaceAllAsync(Arg.Any<FindOptions>(), Arg.Any<ISnapshotService>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("bad"));
        vm.Pattern = "x";
        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Equal("bad", vm.StatusText);
    }

    [Fact]
    public async Task JumpTo_InvokesCallback_AndNoOpWhenNull()
    {
        FindMatch? jumped = null;
        var vm = Build(out _, onJump: m => { jumped = m; return Task.CompletedTask; });
        var match = new FindMatch { MatchedText = "x" };
        await vm.JumpToCommand.ExecuteAsync(match);
        Assert.Same(match, jumped);

        var vm2 = Build(out _); // no callback
        await vm2.JumpToCommand.ExecuteAsync(new FindMatch());
    }
}
