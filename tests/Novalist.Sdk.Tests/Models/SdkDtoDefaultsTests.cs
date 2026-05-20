using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Wizards;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Sdk.Tests.Models;

/// <summary>
/// Exercises the constructor / field-initializer of every concrete DTO in the
/// SDK so the default-value lines are covered, and asserts the documented
/// defaults so a wrong default surfaces as a failing test.
/// </summary>
public class SdkDtoDefaultsTests
{
    [Fact]
    public void ExtensionManifest_Defaults()
    {
        var m = new ExtensionManifest();
        Assert.Equal(string.Empty, m.Id);
        Assert.Equal(string.Empty, m.Name);
        Assert.Equal(string.Empty, m.Description);
        Assert.Equal(string.Empty, m.Version);
        Assert.Equal(string.Empty, m.Author);
        Assert.Equal(string.Empty, m.EntryAssembly);
        Assert.Equal(string.Empty, m.MinHostVersion);
        Assert.Equal(string.Empty, m.MaxHostVersion);
        Assert.Empty(m.Dependencies);
        Assert.Empty(m.Tags);
        Assert.Null(m.Icon);
    }

    [Fact]
    public void AiFinding_AndCachedAndAnalysis_Defaults()
    {
        var f = new AiFinding();
        Assert.Equal(string.Empty, f.Type);
        Assert.Null(f.ScenePov);
        Assert.Null(f.SceneIntensity);

        var c = new CachedAiFinding();
        Assert.Equal(string.Empty, c.Title);
        Assert.Null(c.SceneConflict);

        var whole = new WholeStoryAnalysisResult();
        Assert.Empty(whole.Findings);
        Assert.Equal(string.Empty, whole.RawResponse);

        var chap = new ChapterAnalysisResult();
        Assert.Empty(chap.Scenes);

        var scene = new SceneAnalysisResult();
        Assert.Empty(scene.Findings);

        Assert.Equal(string.Empty, new EntitySummary().Details);
        Assert.Null(new ChapterContext().ActName);

        var checks = new EnabledChecks();
        Assert.True(checks.References);
        Assert.True(checks.Inconsistencies);
        Assert.True(checks.Suggestions);
        Assert.False(checks.SceneStats);
    }

    [Fact]
    public void AiSettings_Defaults()
    {
        var s = new AiSettings();
        Assert.False(s.Enabled);
        Assert.Equal("lmstudio", s.Provider);
        Assert.Equal("chapter", s.AnalysisMode);
        Assert.Equal("http://localhost:1234", s.LmStudioBaseUrl);
        Assert.Equal("copilot", s.CopilotPath);
        Assert.Equal(0.7, s.Temperature);
        Assert.Equal(0.9, s.TopP);
        Assert.Equal(0.05, s.MinP);
        Assert.Equal(1.1, s.FrequencyPenalty);
        Assert.Equal(64, s.RepeatLastN);
        Assert.Equal(4, s.MaxParallelPrompts);
        Assert.True(s.CheckReferences);
        Assert.True(s.GrammarCheckEnabled);
        Assert.False(s.EnableCharacterKnowledge);
        Assert.NotEmpty(AiSettings.DefaultSystemPrompt);
    }

    [Fact]
    public void AiServiceDtos_Defaults()
    {
        Assert.Equal(string.Empty, new AiModelInfo().Key);
        Assert.Equal("user", new AiChatMessage().Role);
        Assert.Null(new AiChatMessage().ImagePaths);
        Assert.False(new AiChatResult().WasTruncated);
        Assert.Empty(new AiAnalysisResult().Findings);
        Assert.Equal(string.Empty, new ChapterTextEntry().Text);
        Assert.Empty(new ChapterFindingsEntry().Findings);
    }

    [Fact]
    public void HostServiceInfoDtos_Defaults()
    {
        Assert.Equal(string.Empty, new ProjectInfo().Name);
        Assert.Equal(string.Empty, new BookInfo().Id);
        Assert.Equal(0, new ChapterInfo().Order);
        Assert.Equal(0, new SceneInfo().WordCount);
        Assert.Empty(new CharacterInfo().Aliases);
        var d = new CharacterDetailedInfo();
        Assert.Empty(d.Aliases);
        Assert.Empty(d.CustomProperties);
        Assert.Empty(d.Relationships);
        Assert.Empty(d.Sections);
        Assert.Equal(string.Empty, d.ResolvedFromScope);
        Assert.Equal(string.Empty, new CharacterRelationshipInfo().Note);
        Assert.Equal(string.Empty, new CharacterSectionInfo().Content);
        Assert.Equal(string.Empty, new LocationInfo().Type);
        Assert.Equal(string.Empty, new ItemInfo().Type);
        Assert.Equal(string.Empty, new LoreInfo().Category);
        var ce = new CustomEntityInfo();
        Assert.Empty(ce.Fields);
        Assert.Null(ce.Sections);
        Assert.Equal(string.Empty, new CustomEntitySectionInfo().Content);
        Assert.Equal(string.Empty, new CustomEntityTypeInfo().TypeKey);
    }

    [Fact]
    public void UiDescriptorDtos_Defaults()
    {
        Assert.Equal(string.Empty, new ActivityBarItem().Label);
        Assert.Equal(string.Empty, new ContextMenuItem().Label);
        Assert.Equal(string.Empty, new ContentViewDescriptor().ViewKey);
        Assert.Equal("Right", new SidebarPanel().Side);
        Assert.Null(new ThemeOverride().Styles);
        Assert.Equal(string.Empty, new ExportFormatDescriptor().FileExtension);
        Assert.Equal(string.Empty, new ExportContext().BookName);
        Assert.Equal(string.Empty, new SettingsPage().Category);
        Assert.Equal("en", new AiPromptContext().Language);
        Assert.Equal("Extensions", new RibbonItem().Tab);
        Assert.Equal("Large", new RibbonItem().Size);
        Assert.Equal(string.Empty, new PropertyTypeDescriptor().TypeKey);
        Assert.Equal("String", new EntityFieldDescriptor().TypeKey);
        var feat = new EntityTypeFeatures();
        Assert.True(feat.IncludeImages);
        Assert.False(feat.IncludeRelationships);
        Assert.True(feat.IncludeSections);
        var et = new EntityTypeDescriptor();
        Assert.NotNull(et.Features);
        Assert.Empty(et.DefaultFields);
    }

    [Fact]
    public void StatusBarItem_Defaults_AndDefaultTextLambda()
    {
        var item = new StatusBarItem();
        Assert.Equal("Right", item.Alignment);
        Assert.Equal(100, item.Order);
        Assert.Null(item.GetTooltip);
        Assert.Null(item.OnClick);
        Assert.Null(item.OnRefresh);
        // Invoke the default GetText lambda to cover its body.
        Assert.Equal(string.Empty, item.GetText());
    }

    [Fact]
    public void WizardDefinitionAndSteps_Defaults()
    {
        var def = new WizardDefinition();
        Assert.Equal(WizardScope.Project, def.Scope);
        Assert.Empty(def.Steps);

        Assert.Equal("equals", new WizardCondition().Operator);
        Assert.True(new TextStep().Skippable);
        var choice = new ChoiceStep();
        Assert.False(choice.MultiSelect);
        Assert.False(choice.AutoSkipIfChoicesEmpty);
        Assert.Empty(choice.Choices);
        Assert.Equal(string.Empty, new WizardChoice().Value);
        Assert.Equal(0, new NumberStep().DefaultValue);
        Assert.False(new DateStep().AllowInWorld);
        Assert.Equal(string.Empty, new EntityRefStep().TargetEntityTypeKey);
        Assert.Empty(new EntityListStep().SubSteps);
        Assert.Empty(new CompoundStep().SubSteps);
    }

    [Fact]
    public void InlineActionDtos_Defaults()
    {
        var desc = new InlineActionDescriptor();
        Assert.Equal(100, desc.Priority);
        Assert.Equal(string.Empty, desc.Group);
        Assert.Equal(string.Empty, new InlineActionRequest().SelectedText);
        var result = new InlineActionResult();
        Assert.Equal(InlineActionDisposition.ReplaceSelection, result.Disposition);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GrammarCheckDtos_Defaults()
    {
        Assert.Empty(new GrammarCheckResult().Issues);
        var issue = new GrammarIssue();
        Assert.Equal(GrammarIssueType.Grammar, issue.Type);
        Assert.Empty(issue.Replacements);
        Assert.Equal(0, issue.Offset);
    }

    [Fact]
    public void BusyProgressOptions_Defaults()
    {
        var o = new BusyProgressOptions();
        Assert.True(o.IsIndeterminate);
        Assert.True(o.ShowProgressBar);
        Assert.False(o.AllowCancel);
        Assert.Null(o.CancelLabel);
        Assert.True(o.IsModal);
        Assert.Equal(string.Empty, o.Title);
    }

    [Fact]
    public void EditorDocumentContext_RequiredInit()
    {
        var ctx = new EditorDocumentContext
        {
            SceneId = "s",
            ChapterGuid = "c",
            SceneTitle = "st",
            ChapterTitle = "ct",
            FilePath = "f"
        };
        Assert.Equal("s", ctx.SceneId);
        Assert.Equal("f", ctx.FilePath);
    }
}
