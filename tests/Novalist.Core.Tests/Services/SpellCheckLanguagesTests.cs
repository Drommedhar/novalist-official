using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Which dictionaries the platform checker loads. The load-bearing rule: an
/// empty list means "follow the writing language", never "check nothing" — a
/// writer who never opened the setting must still get their own language
/// underlined.
/// </summary>
public class SpellCheckLanguagesTests
{
    [Fact]
    public void NoLanguagesConfigured_FollowsTheWritingLanguage()
    {
        Assert.Equal(["de"], SpellCheckLanguages.Resolve([], "de"));
    }

    [Fact]
    public void NullConfiguration_AlsoFollowsTheWritingLanguage()
    {
        Assert.Equal(["en"], SpellCheckLanguages.Resolve(null, "en"));
    }

    [Fact]
    public void ConfiguredLanguagesWin()
    {
        Assert.Equal(["en-GB", "de-DE"], SpellCheckLanguages.Resolve(["en-GB", "de-DE"], "fr"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTagsAreDropped(string tag)
    {
        Assert.Equal(["en"], SpellCheckLanguages.Resolve([tag], "en"));
    }

    [Fact]
    public void TagsAreTrimmedAndDeduplicated()
    {
        Assert.Equal(["en-GB"], SpellCheckLanguages.Resolve([" en-GB ", "EN-gb"], "en"));
    }

    [Fact]
    public void AWritingLanguageOfNothingStillYieldsSomething()
    {
        // A settings file hand-edited to a blank language must not silently turn
        // spell check off.
        Assert.Equal(["en"], SpellCheckLanguages.Resolve([], "  "));
    }

    // ── Through the settings chain ──

    [Fact]
    public void AppSettings_DefaultsToOnAndFollowsTheWritingLanguage()
    {
        IEffectiveSettings settings = new AppSettings { AutoReplacementLanguage = "de" };

        Assert.True(settings.SpellCheckEnabled);
        Assert.Equal(["de"], settings.SpellCheckLanguages);
    }

    [Fact]
    public void AppSettings_KeepsTheStoredListLiteral()
    {
        // The file records the writer's literal choice; only readers get the
        // resolved list, so "empty" keeps meaning "follow the writing language"
        // after they change that language.
        var settings = new AppSettings();

        Assert.Empty(settings.SpellCheckLanguages);
        Assert.Equal(["en"], ((IEffectiveSettings)settings).SpellCheckLanguages);
    }

    [Fact]
    public void ProjectOverride_WinsOverTheGlobalSetting()
    {
        var global = new AppSettings { SpellCheckEnabled = true, AutoReplacementLanguage = "en" };
        var overrides = new SettingsOverrides { SpellCheckEnabled = false };

        var effective = new EffectiveSettings(() => global, () => overrides);

        Assert.False(effective.SpellCheckEnabled);
    }

    [Fact]
    public void ProjectOverride_CanPickItsOwnDictionaries()
    {
        var global = new AppSettings { AutoReplacementLanguage = "en" };
        var overrides = new SettingsOverrides { SpellCheckLanguages = ["de-DE"] };

        Assert.Equal(["de-DE"], new EffectiveSettings(() => global, () => overrides).SpellCheckLanguages);
    }

    [Fact]
    public void ProjectWithoutOverrides_ReadsTheGlobalSetting()
    {
        var global = new AppSettings { SpellCheckEnabled = false, AutoReplacementLanguage = "fr" };

        var effective = new EffectiveSettings(() => global, () => null);

        Assert.False(effective.SpellCheckEnabled);
        Assert.Equal(["fr"], effective.SpellCheckLanguages);
    }

    [Fact]
    public void PinningTheWritingSection_CarriesTheSpellSettings()
    {
        var global = new AppSettings
        {
            SpellCheckEnabled = false,
            SpellCheckLanguages = ["en-GB"],
            AutoReplacementLanguage = "en"
        };
        var overrides = new SettingsOverrides();

        overrides.PinWriting(new EffectiveSettings(() => global, () => null));

        Assert.False(overrides.SpellCheckEnabled);
        Assert.Equal(["en-GB"], overrides.SpellCheckLanguages);
        Assert.True(overrides.HasWritingOverride);
    }

    [Fact]
    public void ClearingTheWritingSection_DropsTheSpellSettings()
    {
        var overrides = new SettingsOverrides
        {
            SpellCheckEnabled = false,
            SpellCheckLanguages = ["en-GB"]
        };

        overrides.ClearWriting();

        Assert.Null(overrides.SpellCheckEnabled);
        Assert.Null(overrides.SpellCheckLanguages);
    }

    [Fact]
    public void SpellSettingsAloneCountAsAWritingOverride()
    {
        Assert.True(new SettingsOverrides { SpellCheckEnabled = false }.HasWritingOverride);
        Assert.True(new SettingsOverrides { SpellCheckLanguages = ["de"] }.HasWritingOverride);
    }
}
