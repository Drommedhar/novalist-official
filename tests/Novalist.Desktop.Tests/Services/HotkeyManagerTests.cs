using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Services;
using Novalist.Sdk.Models;
using Avalonia.Input;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

[Collection("Avalonia")]
public class HotkeyManagerTests
{
    private static HotkeyService NewHotkeyService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns(Task.CompletedTask);
        return new HotkeyService(settings);
    }

    [AvaloniaFact]
    public void TryExecute_MatchingGesture_InvokesAction()
    {
        var svc = NewHotkeyService();
        var fired = false;
        svc.Register(new HotkeyDescriptor { ActionId = "a", DefaultGesture = "Ctrl+S", OnExecute = () => fired = true });
        var mgr = new HotkeyManager(svc);

        Assert.True(mgr.TryExecute(Key.S, KeyModifiers.Control));
        Assert.True(fired);
    }

    [AvaloniaFact]
    public void TryExecute_NoMatch_ReturnsFalse()
    {
        var mgr = new HotkeyManager(NewHotkeyService());
        Assert.False(mgr.TryExecute(Key.Z, KeyModifiers.Control));
    }

    [AvaloniaFact]
    public void TryExecute_CanExecuteFalse_Skips()
    {
        var svc = NewHotkeyService();
        var fired = false;
        svc.Register(new HotkeyDescriptor
        {
            ActionId = "a", DefaultGesture = "Ctrl+S",
            OnExecute = () => fired = true, CanExecute = () => false
        });
        var mgr = new HotkeyManager(svc);

        Assert.False(mgr.TryExecute(Key.S, KeyModifiers.Control));
        Assert.False(fired);
    }

    [AvaloniaFact]
    public void RebuildsOnBindingsChanged_AndSkipsInvalidGestures()
    {
        var svc = NewHotkeyService();
        var mgr = new HotkeyManager(svc);
        // Registered after construction -> BindingsChanged triggers a rebuild.
        var fired = false;
        svc.Register(new HotkeyDescriptor { ActionId = "a", DefaultGesture = "Ctrl+P", OnExecute = () => fired = true });
        svc.Register(new HotkeyDescriptor { ActionId = "bad", DefaultGesture = "not-a-gesture" }); // skipped
        svc.Register(new HotkeyDescriptor { ActionId = "blank", DefaultGesture = "" });            // skipped

        Assert.True(mgr.TryExecute(Key.P, KeyModifiers.Control));
        Assert.True(fired);
    }

    [AvaloniaFact]
    public void HandleKeyDown_AlreadyHandled_NoOp()
    {
        var mgr = new HotkeyManager(NewHotkeyService());
        var e = new KeyEventArgs { Key = Key.S, KeyModifiers = KeyModifiers.Control, Handled = true };
        mgr.HandleKeyDown(e);
        Assert.True(e.Handled); // unchanged
    }

    [AvaloniaFact]
    public void HandleKeyDown_MatchingGesture_SetsHandled()
    {
        var svc = NewHotkeyService();
        svc.Register(new HotkeyDescriptor { ActionId = "a", DefaultGesture = "Ctrl+S", OnExecute = () => { } });
        var mgr = new HotkeyManager(svc);

        var e = new KeyEventArgs { Key = Key.S, KeyModifiers = KeyModifiers.Control };
        mgr.HandleKeyDown(e);
        Assert.True(e.Handled);
    }

    [AvaloniaFact]
    public void HandleKeyDown_NoMatch_LeavesUnhandled()
    {
        var mgr = new HotkeyManager(NewHotkeyService());
        var e = new KeyEventArgs { Key = Key.Z, KeyModifiers = KeyModifiers.None };
        mgr.HandleKeyDown(e);
        Assert.False(e.Handled);
    }
}
