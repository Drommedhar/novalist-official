using Avalonia.Input;
using Novalist.Desktop.Utilities;
using Xunit;

namespace Novalist.Desktop.Tests.Utilities;

public class WebViewKeyMapperTests
{
    [Theory]
    [InlineData("KeyA", "a", Key.A)]
    [InlineData("Digit5", "5", Key.D5)]
    [InlineData("Comma", ",", Key.OemComma)]
    [InlineData("F7", "F7", Key.F7)]
    [InlineData("ArrowLeft", "ArrowLeft", Key.Left)]
    [InlineData("Enter", "Enter", Key.Enter)]
    public void MapToAvaloniaKey_KnownCode(string code, string key, Key expected)
        => Assert.Equal(expected, WebViewKeyMapper.MapToAvaloniaKey(code, key));

    [Fact]
    public void MapToAvaloniaKey_UnknownCode_FallsBackToLetterKey()
        => Assert.Equal(Key.G, WebViewKeyMapper.MapToAvaloniaKey("Unknown", "g"));

    [Fact]
    public void MapToAvaloniaKey_UnknownCode_FallsBackToDigitKey()
        => Assert.Equal(Key.D3, WebViewKeyMapper.MapToAvaloniaKey("Unknown", "3"));

    [Fact]
    public void MapToAvaloniaKey_Unmappable_ReturnsNone()
    {
        Assert.Equal(Key.None, WebViewKeyMapper.MapToAvaloniaKey("Unknown", "@"));
        Assert.Equal(Key.None, WebViewKeyMapper.MapToAvaloniaKey("Unknown", "multi"));
    }
}
