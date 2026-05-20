using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Sdk.Tests.Models;

public class HotkeyDescriptorTests
{
    [Fact]
    public void EffectiveDisplayName_PrefersProvider_WhenSet()
    {
        var d = new HotkeyDescriptor
        {
            DisplayName = "static",
            DisplayNameProvider = () => "dynamic"
        };

        Assert.Equal("dynamic", d.EffectiveDisplayName);
    }

    [Fact]
    public void EffectiveDisplayName_FallsBackToDisplayName_WhenProviderNull()
    {
        var d = new HotkeyDescriptor { DisplayName = "static" };

        Assert.Equal("static", d.EffectiveDisplayName);
    }

    [Fact]
    public void EffectiveCategory_PrefersProvider_WhenSet()
    {
        var d = new HotkeyDescriptor
        {
            Category = "static",
            CategoryProvider = () => "dynamic"
        };

        Assert.Equal("dynamic", d.EffectiveCategory);
    }

    [Fact]
    public void EffectiveCategory_FallsBackToCategory_WhenProviderNull()
    {
        var d = new HotkeyDescriptor { Category = "static" };

        Assert.Equal("static", d.EffectiveCategory);
    }

    [Fact]
    public void Defaults_AreEmptyAndCallbacksUnset()
    {
        var d = new HotkeyDescriptor();

        Assert.Equal(string.Empty, d.ActionId);
        Assert.Equal(string.Empty, d.DisplayName);
        Assert.Equal(string.Empty, d.Category);
        Assert.Equal(string.Empty, d.DefaultGesture);
        Assert.Null(d.OnExecute);
        Assert.Null(d.CanExecute);
        Assert.Equal(string.Empty, d.EffectiveDisplayName);
        Assert.Equal(string.Empty, d.EffectiveCategory);
    }
}
