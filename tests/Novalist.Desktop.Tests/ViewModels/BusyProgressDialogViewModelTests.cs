using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class BusyProgressDialogViewModelTests
{
    [Fact]
    public void Defaults()
    {
        var vm = new BusyProgressDialogViewModel();
        Assert.True(vm.IsIndeterminate);
        Assert.True(vm.ShowProgressBar);
        Assert.Equal("Cancel", vm.CancelLabel);
        Assert.False(vm.HasStatus);
        Assert.Empty(vm.Details);
    }

    [Fact]
    public void Status_DrivesHasStatus_Notification()
    {
        var vm = new BusyProgressDialogViewModel();
        bool notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HasStatus)) notified = true; };
        vm.Status = "Working";
        Assert.True(vm.HasStatus);
        Assert.True(notified);
        vm.Status = "   ";
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public void Properties_RoundTrip()
    {
        var vm = new BusyProgressDialogViewModel
        {
            Title = "T",
            IsIndeterminate = false,
            ShowProgressBar = false,
            Progress = 0.5,
            AllowCancel = true,
            CancelLabel = "Stop",
            HasDetails = true,
        };
        vm.Details.Add("line");
        Assert.Equal("T", vm.Title);
        Assert.False(vm.IsIndeterminate);
        Assert.False(vm.ShowProgressBar);
        Assert.Equal(0.5, vm.Progress);
        Assert.True(vm.AllowCancel);
        Assert.Equal("Stop", vm.CancelLabel);
        Assert.True(vm.HasDetails);
        Assert.Single(vm.Details);
    }
}
