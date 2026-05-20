using Novalist.Core.Utilities;
using Novalist.Desktop.Localization;
using Xunit;

namespace Novalist.Desktop.Tests.Localization;

[Collection("Avalonia")]
public class LocalizationTests
{
    private static readonly string BundledLocales =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");

    private static void RestoreBundled()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        RelationshipRoles.Reload();
    }

    // ── LocFormatters (pure) ────────────────────────────────────────
    [AvaloniaFact]
    public void ReadingTime_AllBranches()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        Assert.False(string.IsNullOrEmpty(LocFormatters.ReadingTime(0)));   // < 1
        Assert.False(string.IsNullOrEmpty(LocFormatters.ReadingTime(5)));   // < 60
        Assert.False(string.IsNullOrEmpty(LocFormatters.ReadingTime(120))); // exact hours
        Assert.False(string.IsNullOrEmpty(LocFormatters.ReadingTime(95)));  // hours + minutes
    }

    [AvaloniaFact]
    public void ReadabilityLevel_AllArms()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        foreach (var level in Enum.GetValues<ReadabilityLevel>())
            Assert.False(string.IsNullOrEmpty(LocFormatters.ReadabilityLevel(level)));
    }

    // ── Loc.T format ────────────────────────────────────────────────
    [AvaloniaFact]
    public void T_BadFormatTemplate_ReturnsTemplate()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        // Missing key -> Resolve returns the key as the template; "{0" is an invalid
        // format string -> FormatException caught -> template returned unchanged.
        Assert.Equal("{0", Loc.T("{0", "x"));
    }

    [AvaloniaFact]
    public void Resolve_NonEnglishMissingKey_FallsBackToEnglish()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-fb-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "en.json"), "{\"shared\":\"EN\",\"onlyEn\":\"OnlyEN\"}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "de.json"), "{\"shared\":\"DE\"}");
            Loc.Instance.Initialize(tmp, "de");
            Assert.Equal("DE", Loc.T("shared"));        // from active (de) strings
            Assert.Equal("OnlyEN", Loc.T("onlyEn"));    // missing in de -> English fallback (Resolve fallback branch)
            Assert.Equal("missing.key", Loc.T("missing.key")); // missing everywhere -> key returned
        }
        finally
        {
            RestoreBundled();
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void GetLanguageDisplayName_MissingFile_ReturnsCode()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        Assert.Equal("zz-nonexistent", Loc.Instance.GetLanguageDisplayName("zz-nonexistent"));
    }

    // ── File-edge branches (temp locale dir, restored afterward) ────
    [AvaloniaFact]
    public void Loc_FileEdgeCases_EmptyDir_Malformed_NumberBool()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-loc-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            // number + bool + nested values exercise FlattenJson's Number/True/False arms.
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "xx.json"),
                "{\"language\":{\"name\":\"X-Lang\"},\"count\":5,\"flag\":true,\"off\":false}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "bad.json"), "{ this is not valid json ");

            Loc.Instance.Initialize(tmp, "en");
            Assert.Contains("xx", Loc.Instance.GetAvailableLanguages());
            Assert.Equal("X-Lang", Loc.Instance.GetLanguageDisplayName("xx")); // flattens number/bool
            Assert.Equal("bad", Loc.Instance.GetLanguageDisplayName("bad"));   // malformed -> catch -> code

            // Nonexistent directory -> available languages falls back to ["en"].
            Loc.Instance.Initialize(System.IO.Path.Combine(tmp, "does-not-exist"), "en");
            Assert.Equal(new[] { "en" }, Loc.Instance.GetAvailableLanguages());
        }
        finally
        {
            RestoreBundled();
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }
    }

    // ── RelationshipRoles file parsing ──────────────────────────────
    [AvaloniaFact]
    public void RelationshipRoles_EmptyDir_NoRelationships_Malformed()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-rel-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            // valid relationships map (role-type -> keywords), a file with no
            // "relationships" key (continue), and a malformed file (catch).
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "en.json"),
                "{\"relationships\":{\"parent\":[\"father\",\"mother\"],\"child\":[\"son\"]}}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "norels.json"), "{\"other\":1}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmp, "bad.json"), "{ broken ");

            Loc.Instance.Initialize(tmp, "en");
            RelationshipRoles.Reload();
            Assert.Contains("father", RelationshipRoles.Get("parent"));
            Assert.Contains("son", RelationshipRoles.Get("child"));

            // Nonexistent dir -> the IsNullOrEmpty/Directory guard returns empty.
            Loc.Instance.Initialize(System.IO.Path.Combine(tmp, "does-not-exist"), "en");
            RelationshipRoles.Reload();
            Assert.Empty(RelationshipRoles.Get("parent"));
        }
        finally
        {
            RestoreBundled();
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }
    }
}
