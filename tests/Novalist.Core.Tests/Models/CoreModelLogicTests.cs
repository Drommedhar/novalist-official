using System.ComponentModel;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void EnsureDefaults_PopulatesAutoReplacements_WhenEmpty()
    {
        var s = new AppSettings { AutoReplacementLanguage = "en" };
        s.AutoReplacements.Clear();
        s.EnsureDefaults();
        Assert.NotEmpty(s.AutoReplacements);
    }

    [Fact]
    public void EnsureDefaults_KeepsExisting_WhenNotEmpty()
    {
        var s = new AppSettings();
        s.AutoReplacements.Clear();
        s.AutoReplacements.Add(new AutoReplacementPair { Start = "x" });
        s.EnsureDefaults();
        Assert.Single(s.AutoReplacements);
    }

    [Fact]
    public void GetKnownInverseRoles_Blank_ReturnsEmpty()
        => Assert.Empty(new AppSettings().GetKnownInverseRoles("  "));

    [Fact]
    public void GetKnownInverseRoles_Absent_ReturnsEmpty()
        => Assert.Empty(new AppSettings().GetKnownInverseRoles("unknown"));

    [Fact]
    public void LearnRelationshipPair_AddsBothDirections()
    {
        var s = new AppSettings();
        Assert.True(s.LearnRelationshipPair("Father", "Child"));
        Assert.Contains("Child", s.GetKnownInverseRoles("Father"));
        Assert.Contains("Father", s.GetKnownInverseRoles("Child"));
    }

    [Fact]
    public void LearnRelationshipPair_Blank_ReturnsFalse()
        => Assert.False(new AppSettings().LearnRelationshipPair("", "x"));

    [Fact]
    public void LearnRelationshipPair_Duplicate_ReturnsFalse()
    {
        var s = new AppSettings();
        s.LearnRelationshipPair("Father", "Child");
        Assert.False(s.LearnRelationshipPair("Father", "Child"));
    }
}

public class AutoReplacementDefaultsTests
{
    [Fact]
    public void AvailableLanguages_ContainsEnglish()
        => Assert.Contains("en", AutoReplacementDefaults.AvailableLanguages);

    [Fact]
    public void GetPreset_KnownLanguage_ReturnsPairs()
        => Assert.NotEmpty(AutoReplacementDefaults.GetPreset("de-low"));

    [Fact]
    public void GetPreset_UnknownLanguage_FallsBackToEnglish()
    {
        var en = AutoReplacementDefaults.GetPreset("en");
        var fallback = AutoReplacementDefaults.GetPreset("zz");
        Assert.Equal(en.Count, fallback.Count);
    }
}

public class EntityImageTests
{
    [Fact]
    public void Setter_RaisesPropertyChanged_OnChange()
    {
        var img = new EntityImage();
        var raised = new List<string?>();
        img.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        img.Name = "cover";
        img.Path = "/x";
        Assert.Equal(new[] { "Name", "Path" }, raised);
    }

    [Fact]
    public void Setter_DoesNotRaise_WhenValueUnchanged()
    {
        var img = new EntityImage { Name = "same" };
        var raised = false;
        img.PropertyChanged += (_, _) => raised = true;
        img.Name = "same";
        Assert.False(raised);
    }
}

public class SettingsOverridesTests
{
    [Fact]
    public void HasAppearanceOverride_TrueWhenAnySet_FalseWhenNone()
    {
        Assert.False(new SettingsOverrides().HasAppearanceOverride);
        Assert.True(new SettingsOverrides { Theme = "dark" }.HasAppearanceOverride);
    }

    [Fact]
    public void HasEditorOverride_TrueWhenAnySet()
        => Assert.True(new SettingsOverrides { EditorFontSize = 14 }.HasEditorOverride);

    [Fact]
    public void HasWritingOverride_TrueWhenAnySet()
        => Assert.True(new SettingsOverrides { GrammarCheckEnabled = true }.HasWritingOverride);

    [Fact]
    public void ClearAppearance_NullsAppearanceKeys()
    {
        var o = new SettingsOverrides { Language = "en", Theme = "dark", AccentColor = "#fff" };
        o.ClearAppearance();
        Assert.False(o.HasAppearanceOverride);
    }

    [Fact]
    public void ClearEditor_NullsEditorKeys()
    {
        var o = new SettingsOverrides { EditorFontSize = 14, BookFontSize = 12, EnableBookWidth = true };
        o.ClearEditor();
        Assert.False(o.HasEditorOverride);
    }

    [Fact]
    public void ClearWriting_NullsWritingKeys()
    {
        var o = new SettingsOverrides { GrammarCheckEnabled = true, AutoReplacementLanguage = "en" };
        o.ClearWriting();
        Assert.False(o.HasWritingOverride);
    }
}

public class CharacterOverrideTests
{
    [Fact]
    public void ScopeLabel_Empty_WhenNoScope()
        => Assert.Equal(string.Empty, new CharacterOverride().ScopeLabel);

    [Fact]
    public void ScopeLabel_JoinsPresentParts()
    {
        var o = new CharacterOverride { Act = "I", Chapter = "1", Scene = "A" };
        Assert.Equal("Act: I → Ch: 1 → Sc: A", o.ScopeLabel);
    }
}

public class BookDataTests
{
    [Fact]
    public void ActiveDraft_MatchesById()
    {
        var book = new BookData
        {
            ActiveDraftId = "d2",
            Drafts = { new BookDraftMetadata { Id = "d1" }, new BookDraftMetadata { Id = "d2" } }
        };
        Assert.Equal("d2", book.ActiveDraft!.Id);
    }

    [Fact]
    public void ActiveDraft_FallsBackToFirst_WhenIdMissing()
    {
        var book = new BookData
        {
            ActiveDraftId = "missing",
            Drafts = { new BookDraftMetadata { Id = "d1" } }
        };
        Assert.Equal("d1", book.ActiveDraft!.Id);
    }

    [Fact]
    public void ActiveDraft_Null_WhenNoDrafts()
        => Assert.Null(new BookData().ActiveDraft);
}

public class ExportPresetTests
{
    [Fact]
    public void GetById_Blank_ReturnsFirst()
        => Assert.Same(ExportPresets.All[0], ExportPresets.GetById(null));

    [Fact]
    public void GetById_Match_ReturnsPreset()
    {
        var id = ExportPresets.All[0].Id;
        Assert.Equal(id, ExportPresets.GetById(id).Id);
    }

    [Fact]
    public void GetById_NoMatch_ReturnsFirst()
        => Assert.Same(ExportPresets.All[0], ExportPresets.GetById("does-not-exist"));
}

public class GitFileEntryTests
{
    [Fact]
    public void DisplayStatus_PrefersWorkTree_WhenModified()
    {
        var e = new GitFileEntry("a.txt", GitFileStatus.Added, GitFileStatus.Modified);
        Assert.Equal(GitFileStatus.Modified, e.DisplayStatus);
    }

    [Fact]
    public void DisplayStatus_FallsBackToIndex_WhenWorkTreeUnmodified()
    {
        var e = new GitFileEntry("a.txt", GitFileStatus.Added, GitFileStatus.Unmodified);
        Assert.Equal(GitFileStatus.Added, e.DisplayStatus);
    }

    [Theory]
    [InlineData(GitFileStatus.Modified, true)]
    [InlineData(GitFileStatus.Added, true)]
    [InlineData(GitFileStatus.Unmodified, false)]
    [InlineData(GitFileStatus.Untracked, false)]
    [InlineData(GitFileStatus.Ignored, false)]
    public void IsStaged(GitFileStatus index, bool expected)
    {
        var e = new GitFileEntry("a.txt", index, GitFileStatus.Unmodified);
        Assert.Equal(expected, e.IsStaged);
    }
}

public class SceneAnalysisOverridesTests
{
    [Fact]
    public void HasValues_FalseWhenAllUnset()
        => Assert.False(new SceneAnalysisOverrides().HasValues);

    [Theory]
    [InlineData("pov", null, null, null)]
    [InlineData(null, "joy", null, null)]
    [InlineData(null, null, "conflict", null)]
    public void HasValues_TrueWhenAnyStringSet(string? pov, string? emotion, string? conflict, string? unused)
    {
        _ = unused;
        var o = new SceneAnalysisOverrides { Pov = pov, Emotion = emotion, Conflict = conflict };
        Assert.True(o.HasValues);
    }

    [Fact]
    public void HasValues_TrueWhenIntensitySet()
        => Assert.True(new SceneAnalysisOverrides { Intensity = 5 }.HasValues);

    [Fact]
    public void HasValues_TrueWhenTagsSet()
        => Assert.True(new SceneAnalysisOverrides { Tags = new() { "a" } }.HasValues);

    [Fact]
    public void Clone_CopiesTags_AsNewList()
    {
        var o = new SceneAnalysisOverrides { Pov = "p", Intensity = 3, Tags = new() { "a", "b" } };
        var clone = o.Clone();
        Assert.Equal("p", clone.Pov);
        Assert.Equal(o.Tags, clone.Tags);
        Assert.NotSame(o.Tags, clone.Tags);
    }

    [Fact]
    public void Clone_NullTags_StaysNull()
        => Assert.Null(new SceneAnalysisOverrides().Clone().Tags);
}

public class ProjectMetadataTests
{
    [Fact]
    public void GetActiveBook_MatchesById()
    {
        var meta = new ProjectMetadata
        {
            ActiveBookId = "b2",
            Books = { new BookData { Id = "b1" }, new BookData { Id = "b2" } }
        };
        Assert.Equal("b2", meta.GetActiveBook()!.Id);
    }

    [Fact]
    public void GetActiveBook_FallsBackToFirst()
    {
        var meta = new ProjectMetadata { ActiveBookId = "x", Books = { new BookData { Id = "b1" } } };
        Assert.Equal("b1", meta.GetActiveBook()!.Id);
    }

    [Fact]
    public void GetActiveBook_Null_WhenNoBooks()
        => Assert.Null(new ProjectMetadata().GetActiveBook());
}

public class CustomEntityTypeDefinitionTests
{
    [Fact]
    public void IsUserSource_TrueForUser()
        => Assert.True(new CustomEntityTypeDefinition { Source = "user" }.IsUserSource);

    [Fact]
    public void IsUserSource_FalseForExtension()
        => Assert.False(new CustomEntityTypeDefinition { Source = "com.ext" }.IsUserSource);
}

public class WordHistoryEntryTests
{
    [Fact]
    public void DateOnly_ParsesIsoDate()
        => Assert.Equal(new DateOnly(2024, 10, 22), new WordHistoryEntry { Date = "2024-10-22" }.DateOnly());

    [Fact]
    public void DateOnly_Invalid_ReturnsMinValue()
        => Assert.Equal(DateOnly.MinValue, new WordHistoryEntry { Date = "nonsense" }.DateOnly());
}

public class WellKnownPropertyTypesTests
{
    [Fact]
    public void FromEnum_ReturnsEnumName()
        => Assert.Equal("String", WellKnownPropertyTypes.FromEnum(CustomPropertyType.String));

    [Fact]
    public void TryToEnum_KnownKey_ReturnsTrue()
    {
        Assert.True(WellKnownPropertyTypes.TryToEnum("string", out var t));
        Assert.Equal(CustomPropertyType.String, t);
    }

    [Fact]
    public void TryToEnum_UnknownKey_ReturnsFalse()
        => Assert.False(WellKnownPropertyTypes.TryToEnum("color", out _));
}

public class InWorldCalendarTests
{
    [Fact]
    public void CustomYearLength_SumsDaysPerMonth()
    {
        var cal = new InWorldCalendar { DaysPerMonth = { 30, 31, 29 } };
        Assert.Equal(90, cal.CustomYearLength);
    }

    [Fact]
    public void CustomYearLength_Zero_WhenNoMonths()
        => Assert.Equal(0, new InWorldCalendar().CustomYearLength);
}

public class NovalistProjectTests
{
    [Fact]
    public void CreateNew_SetsFieldsAndGeneratesId()
    {
        var p = NovalistProject.CreateNew("My Book", "/path");
        Assert.Equal("My Book", p.Name);
        Assert.Equal("/path", p.Path);
        Assert.StartsWith("project-", p.Id);
        Assert.True(p.CreatedAt <= DateTime.UtcNow);
    }
}

public class EntityTemplateToStringTests
{
    [Fact]
    public void ToString_ReturnsName()
    {
        Assert.Equal("Char", new CharacterTemplate { Name = "Char" }.ToString());
        Assert.Equal("Loc", new LocationTemplate { Name = "Loc" }.ToString());
        Assert.Equal("Item", new ItemTemplate { Name = "Item" }.ToString());
        Assert.Equal("Lore", new LoreTemplate { Name = "Lore" }.ToString());
        Assert.Equal("Custom", new CustomEntityTemplate { Name = "Custom" }.ToString());
    }
}

public class StoryStructureTemplateTests
{
    [Fact]
    public void GetById_Match_ReturnsTemplate()
    {
        var id = StoryStructureTemplates.All[0].Id;
        Assert.Equal(id, StoryStructureTemplates.GetById(id)!.Id);
    }

    [Fact]
    public void GetById_NoMatch_ReturnsNull()
        => Assert.Null(StoryStructureTemplates.GetById("nope"));
}
