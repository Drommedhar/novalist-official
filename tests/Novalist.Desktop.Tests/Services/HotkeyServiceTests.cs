using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Services;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

public class HotkeyServiceTests
{
    private static (HotkeyService Sut, ISettingsService Settings, AppSettings App) Build()
    {
        var settings = Substitute.For<ISettingsService>();
        var app = new AppSettings();
        settings.Settings.Returns(app);
        settings.SaveAsync().Returns(Task.CompletedTask);
        return (new HotkeyService(settings), settings, app);
    }

    private static HotkeyDescriptor Desc(string id, string gesture = "Ctrl+S")
        => new() { ActionId = id, DefaultGesture = gesture };

    [Fact]
    public void Register_RaisesBindingsChanged()
    {
        var (sut, _, _) = Build();
        var raised = 0;
        sut.BindingsChanged += () => raised++;
        sut.Register(Desc("a"));
        Assert.Equal(1, raised);
        Assert.Single(sut.GetAllDescriptors());
    }

    [Fact]
    public void RegisterRange_AddsAll()
    {
        var (sut, _, _) = Build();
        sut.RegisterRange(new[] { Desc("a"), Desc("b") });
        Assert.Equal(2, sut.GetAllDescriptors().Count);
    }

    [Fact]
    public void Unregister_RemovesAndRaises_OnlyWhenPresent()
    {
        var (sut, _, _) = Build();
        sut.Register(Desc("a"));
        var raised = 0;
        sut.BindingsChanged += () => raised++;
        sut.Unregister("a");
        sut.Unregister("missing"); // no raise
        Assert.Equal(1, raised);
        Assert.Empty(sut.GetAllDescriptors());
    }

    [Fact]
    public void GetGesture_OverrideThenDefaultThenEmpty()
    {
        var (sut, _, app) = Build();
        sut.Register(Desc("a", "Ctrl+A"));
        Assert.Equal("Ctrl+A", sut.GetGesture("a"));            // default
        app.HotkeyBindings["a"] = "Ctrl+Shift+A";
        Assert.Equal("Ctrl+Shift+A", sut.GetGesture("a"));      // override wins
        Assert.Equal(string.Empty, sut.GetGesture("unknown"));  // no descriptor, no override
    }

    [Fact]
    public void SetGesture_PersistsAndRaises()
    {
        var (sut, settings, app) = Build();
        sut.SetGesture("a", "Ctrl+B");
        Assert.Equal("Ctrl+B", app.HotkeyBindings["a"]);
        settings.Received().SaveAsync();
    }

    [Fact]
    public void ResetGesture_RemovesOverride_OnlyWhenPresent()
    {
        var (sut, _, app) = Build();
        app.HotkeyBindings["a"] = "Ctrl+B";
        var raised = 0;
        sut.BindingsChanged += () => raised++;
        sut.ResetGesture("a");
        sut.ResetGesture("a"); // already gone -> no raise
        Assert.False(app.HotkeyBindings.ContainsKey("a"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResetAll_ClearsOverrides()
    {
        var (sut, _, app) = Build();
        app.HotkeyBindings["a"] = "X";
        app.HotkeyBindings["b"] = "Y";
        sut.ResetAll();
        Assert.Empty(app.HotkeyBindings);
    }

    [Fact]
    public void FindConflict()
    {
        var (sut, _, _) = Build();
        sut.Register(Desc("a", "Ctrl+S"));
        sut.Register(Desc("b", "Ctrl+P"));
        Assert.Equal("a", sut.FindConflict("b", "Ctrl+S"));   // collides with a
        Assert.Null(sut.FindConflict("b", "Ctrl+Z"));          // no collision
        Assert.Null(sut.FindConflict("a", "Ctrl+S"));          // same action ignored
        Assert.Null(sut.FindConflict("b", "   "));             // blank
    }
}
