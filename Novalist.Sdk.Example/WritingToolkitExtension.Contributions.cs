using System.Text;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example;

public sealed partial class WritingToolkitExtension
{
    // ── IWizardContributor ──────────────────────────────────────────

    public IReadOnlyList<Novalist.Sdk.Models.Wizards.WizardDefinition> GetWizards() =>
    [
        new Novalist.Sdk.Models.Wizards.WizardDefinition
        {
            Id = "com.novalist.writingtoolkit.pomodoro",
            DisplayName = _loc.T("wizard.pomodoro.title"),
            Description = _loc.T("wizard.pomodoro.description"),
            Scope = Novalist.Sdk.Models.Wizards.WizardScope.Reference,
            Steps =
            {
                new Novalist.Sdk.Models.Wizards.NumberStep
                {
                    Id = "duration",
                    Title = _loc.T("wizard.pomodoro.duration"),
                    Min = 5, Max = 90, DefaultValue = 25, Unit = "min",
                    Skippable = false,
                },
                new Novalist.Sdk.Models.Wizards.ChoiceStep
                {
                    Id = "autostart",
                    Title = _loc.T("wizard.pomodoro.autostart"),
                    Choices =
                    {
                        new Novalist.Sdk.Models.Wizards.WizardChoice { Value = "true", Label = _loc.T("wizard.pomodoro.yes") },
                        new Novalist.Sdk.Models.Wizards.WizardChoice { Value = "false", Label = _loc.T("wizard.pomodoro.no") },
                    },
                },
            },
        },
    ];

    // ── IExportFormatContributor ────────────────────────────────────

    public IReadOnlyList<ExportFormatDescriptor> GetExportFormats() =>
    [
        new ExportFormatDescriptor
        {
            FormatKey = "plaintext_clean",
            DisplayName = _loc.T("export.plainTextClean"),
            FileExtension = ".txt",
            IconPath = "M17 3a2.83 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5zM15 5l4 4",
            Export = async context =>
            {
                var sb = new StringBuilder();
                var chapters = _host.ProjectService.GetChaptersOrdered();
                foreach (var chapter in chapters)
                {
                    sb.AppendLine($"# {chapter.Title}");
                    sb.AppendLine();
                    var scenes = _host.ProjectService.GetScenesForChapter(chapter.Guid);
                    foreach (var scene in scenes)
                    {
                        var content = await _host.ProjectService.ReadSceneContentAsync(chapter.Guid, scene.Id);
                        sb.AppendLine(content);
                        sb.AppendLine();
                    }
                }
                await _host.FileService.WriteTextAsync(context.OutputPath, sb.ToString());
            }
        }
    ];

    // ── IThemeContributor ───────────────────────────────────────────

    public IReadOnlyList<ThemeOverride> GetThemeOverrides() =>
    [
        // A token map: the usual form. Every --nl-* left out keeps its default,
        // so a theme can restate the whole palette or just a corner of it.
        new ThemeOverride
        {
            Name = "Sepia",
            AccentColor = "#8a6d3b",
            Tokens = new Dictionary<string, string>
            {
                ["--nl-base"] = "244 236 216",
                ["--nl-surface-window"] = "#f4ecd8",
                ["--nl-surface-sidebar"] = "#ece0c8",
                ["--nl-surface-toolbar"] = "#ece0c8",
                ["--nl-surface-inspector"] = "#ece0c8",
                ["--nl-surface-editor"] = "#faf4e6",
                ["--nl-surface-card"] = "#ece0c8",
                ["--nl-surface-input"] = "#faf4e6",
                ["--nl-surface-overlay"] = "rgb(59 47 47 / 0.4)",
                ["--nl-surface-hover"] = "rgb(59 47 47 / 0.06)",
                ["--nl-surface-selected"] = "rgb(138 109 59 / 0.2)",
                ["--nl-text"] = "#3b2f2f",
                ["--nl-text-dim"] = "#6b5b4b",
                ["--nl-text-subtle"] = "#8a7a68",
                ["--nl-accent-hover"] = "#a8854a",
                ["--nl-accent-ink"] = "#faf4e6",
                ["--nl-focus-ring"] = "rgb(138 109 59 / 0.6)",
                ["--nl-border"] = "#d8c8a8",
                ["--nl-border-subtle"] = "#e4d8bd",
                ["--nl-border-firm"] = "#c4b08a",
                ["--nl-scrollbar-thumb"] = "rgb(59 47 47 / 0.2)",
                ["--nl-scrollbar-thumb-hover"] = "rgb(59 47 47 / 0.32)",
                ["--nl-scrollbar-thumb-active"] = "rgb(138 109 59 / 0.6)"
            }
        },
        // A stylesheet: for themes that need rules a token map cannot hold. The
        // path is relative to the extension folder.
        new ThemeOverride
        {
            Name = "Dark Ocean",
            AccentColor = "#1b6ca8",
            ResourcePath = "Themes/dark-ocean.css"
        }
    ];

    // ── IEntityTypeContributor ──────────────────────────────────────

    public IReadOnlyList<EntityTypeDescriptor> GetEntityTypes() =>
    [
        new EntityTypeDescriptor
        {
            TypeKey = "ext.writingtoolkit.faction",
            DisplayName = _loc.T("entityType.faction"),
            DisplayNamePlural = _loc.T("entityType.factions"),
            FolderName = "Factions",
            DefaultFields =
            [
                new EntityFieldDescriptor { Key = "leader", DisplayName = _loc.T("entityType.faction.leader"), TypeKey = "EntityRef", EnumOptions = ["Character"] },
                new EntityFieldDescriptor { Key = "type", DisplayName = _loc.T("entityType.faction.type"), TypeKey = "Enum", EnumOptions = ["Government", "Military", "Religious", "Criminal", "Guild", "Rebellion", "Other"] },
                new EntityFieldDescriptor { Key = "motto", DisplayName = _loc.T("entityType.faction.motto"), TypeKey = "String" },
                new EntityFieldDescriptor { Key = "founded", DisplayName = _loc.T("entityType.faction.founded"), TypeKey = "Date" },
                new EntityFieldDescriptor { Key = "memberCount", DisplayName = _loc.T("entityType.faction.memberCount"), TypeKey = "Int" }
            ],
            Features = new EntityTypeFeatures
            {
                IncludeImages = true,
                IncludeRelationships = true,
                IncludeSections = true
            }
        }
    ];

    // ── IPropertyTypeContributor ────────────────────────────────────

    public IReadOnlyList<PropertyTypeDescriptor> GetPropertyTypes() =>
    [
        new PropertyTypeDescriptor
        {
            TypeKey = "ext.writingtoolkit.wordcount",
            DisplayName = _loc.T("propertyType.wordCount"),
            DefaultValue = "0"
        }
    ];

    // ── IInlineActionContributor ────────────────────────────────────

    public IReadOnlyList<InlineActionDescriptor> GetInlineActions() =>
    [
        new InlineActionDescriptor
        {
            Id = "ext.writingtoolkit.uppercase",
            Label = _loc.T("inline.uppercase"),
            Group = _loc.T("group.writingToolkit"),
            Priority = 10
        },
        new InlineActionDescriptor
        {
            Id = "ext.writingtoolkit.wordcount",
            Label = _loc.T("inline.wordCount"),
            Group = _loc.T("group.writingToolkit"),
            Priority = 20
        }
    ];

    public Task<InlineActionResult> ExecuteAsync(string actionId, InlineActionRequest request, CancellationToken cancellationToken)
    {
        var text = request.SelectedText ?? string.Empty;
        return actionId switch
        {
            "ext.writingtoolkit.uppercase" => Task.FromResult(new InlineActionResult
            {
                Text = text.ToUpperInvariant(),
                Disposition = InlineActionDisposition.ReplaceSelection
            }),
            "ext.writingtoolkit.wordcount" => Task.FromResult(new InlineActionResult
            {
                Text = _loc.T("inline.wordCountResult",
                    text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length),
                Disposition = InlineActionDisposition.InsertAfterSelection
            }),
            _ => Task.FromResult(new InlineActionResult { Error = _loc.T("inline.unknownAction") })
        };
    }
}
