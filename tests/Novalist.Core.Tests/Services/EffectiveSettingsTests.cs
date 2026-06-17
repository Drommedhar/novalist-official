using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class EffectiveSettingsTests
{
    [Fact]
    public void Resolves_ToGlobal_WhenNoOverrides()
    {
        var global = new AppSettings
        {
            Language = "en",
            Theme = "dark",
            AccentColor = "#111",
            EditorFontFamily = "Inter",
            EditorFontSize = 13,
            TypewriterScrollEnabled = true,
            TypewriterScrollAnchor = "center",
            PageViewEnabled = true,
            EnableBookParagraphSpacing = true,
            EnableBookWidth = true,
            BookPageFormat = "A4",
            BookTextBlockWidth = 6.0,
            BookFontFamily = "Georgia",
            BookFontSize = 12,
            AutoReplacementLanguage = "en",
            DialogueCorrectionEnabled = true,
            GrammarCheckEnabled = true,
            GrammarCheckApiUrl = "http://g",
            GrammarCheckApiKey = "key-g",
            GrammarCheckUsername = "user-g",
            GrammarCheckPickyMode = true,
            GrammarCheckMotherTongue = "de"
        };
        var sut = new EffectiveSettings(() => global, () => null);

        Assert.Equal("en", sut.Language);
        Assert.Equal("dark", sut.Theme);
        Assert.Equal("#111", sut.AccentColor);
        Assert.Equal("Inter", sut.EditorFontFamily);
        Assert.Equal(13, sut.EditorFontSize);
        Assert.True(sut.TypewriterScrollEnabled);
        Assert.Equal("center", sut.TypewriterScrollAnchor);
        Assert.True(sut.PageViewEnabled);
        Assert.True(sut.EnableBookParagraphSpacing);
        Assert.True(sut.EnableBookWidth);
        Assert.Equal("A4", sut.BookPageFormat);
        Assert.Equal(6.0, sut.BookTextBlockWidth);
        Assert.Equal("Georgia", sut.BookFontFamily);
        Assert.Equal(12, sut.BookFontSize);
        Assert.Equal("en", sut.AutoReplacementLanguage);
        Assert.Same(global.AutoReplacements, sut.AutoReplacements);
        Assert.True(sut.DialogueCorrectionEnabled);
        Assert.True(sut.GrammarCheckEnabled);
        Assert.Equal("http://g", sut.GrammarCheckApiUrl);
        Assert.Equal("key-g", sut.GrammarCheckApiKey);
        Assert.Equal("user-g", sut.GrammarCheckUsername);
        Assert.True(sut.GrammarCheckPickyMode);
        Assert.Equal("de", sut.GrammarCheckMotherTongue);
    }

    [Fact]
    public void Resolves_ToOverride_WhenSet()
    {
        var global = new AppSettings { Language = "en", Theme = "dark" };
        var ovr = new SettingsOverrides
        {
            Language = "de",
            Theme = "light",
            AccentColor = "#abc",
            EditorFontFamily = "Mono",
            EditorFontSize = 20,
            TypewriterScrollEnabled = false,
            TypewriterScrollAnchor = "top",
            PageViewEnabled = false,
            EnableBookParagraphSpacing = false,
            EnableBookWidth = false,
            BookPageFormat = "Letter",
            BookTextBlockWidth = 9.0,
            BookFontFamily = "Serif",
            BookFontSize = 18,
            AutoReplacementLanguage = "de",
            AutoReplacements = new List<AutoReplacementPair> { new() { Start = "x" } },
            DialogueCorrectionEnabled = false,
            GrammarCheckEnabled = false,
            GrammarCheckApiUrl = "http://o",
            GrammarCheckApiKey = "key-o",
            GrammarCheckUsername = "user-o",
            GrammarCheckPickyMode = true,
            GrammarCheckMotherTongue = "fr"
        };
        var sut = new EffectiveSettings(() => global, () => ovr);

        Assert.Equal("de", sut.Language);
        Assert.Equal("light", sut.Theme);
        Assert.Equal("#abc", sut.AccentColor);
        Assert.Equal("Mono", sut.EditorFontFamily);
        Assert.Equal(20, sut.EditorFontSize);
        Assert.False(sut.TypewriterScrollEnabled);
        Assert.Equal("top", sut.TypewriterScrollAnchor);
        Assert.False(sut.PageViewEnabled);
        Assert.False(sut.EnableBookParagraphSpacing);
        Assert.False(sut.EnableBookWidth);
        Assert.Equal("Letter", sut.BookPageFormat);
        Assert.Equal(9.0, sut.BookTextBlockWidth);
        Assert.Equal("Serif", sut.BookFontFamily);
        Assert.Equal(18, sut.BookFontSize);
        Assert.Equal("de", sut.AutoReplacementLanguage);
        Assert.Same(ovr.AutoReplacements, sut.AutoReplacements);
        Assert.False(sut.DialogueCorrectionEnabled);
        Assert.False(sut.GrammarCheckEnabled);
        Assert.Equal("http://o", sut.GrammarCheckApiUrl);
        Assert.Equal("key-o", sut.GrammarCheckApiKey);
        Assert.Equal("user-o", sut.GrammarCheckUsername);
        Assert.True(sut.GrammarCheckPickyMode);
        Assert.Equal("fr", sut.GrammarCheckMotherTongue);
    }
}
