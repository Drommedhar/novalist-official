using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    /// <summary>The headless extension host, created on first use.</summary>
    public Extensions.ExtensionManager ExtensionsHost
    {
        get
        {
            if (_extensions == null)
            {
                _uiPump = new Extensions.UiPump();
                _hostServices = new Extensions.HostServices(
                    FileService, Projects, new EntityService(Projects), Settings, _uiPump, Editing);
                _hostServices.NotificationRequested += UiBridge.ShowNotification;
                // An extension's write has to reach the screen, or it changed a
                // file and nothing the writer can see.
                _hostServices.EntityRefreshRequested += UiBridge.EntitiesChanged;
                _hostServices.ProjectStructureChanged += UiBridge.ProjectStructureChanged;
                _hostServices.BusyProgressFactory = UiBridge.CreateProgress;
                _hostServices.WizardLauncher = UiBridge.RunWizardAsync;
                _hostServices.Picker = UiBridge.PickAsync;
                _hostServices.CoordinateWorkspace = (reason, action) => Coordinator?.RunAsync(reason, action) ?? action();
                _extensions = new Extensions.ExtensionManager(Settings, _hostServices, ExtensionsLoaderOverride);
                _hostServices.ExtensionManager = _extensions;
            }
            return _extensions;
        }
    }

    /// <summary>The host-services event raiser, or null when no extension host
    /// has been created yet (no extension has been touched this session).</summary>
    internal Extensions.HostServices? HostServices => _hostServices;

    /// <summary>The extension manager, or null when no extension host has been
    /// created yet. Lets callers consult contributions without force-creating a
    /// host (and its pump thread) for projects that never touched extensions.</summary>
    internal Extensions.ExtensionManager? ExtensionHostOrNull => _extensions;

    /// <summary>
    /// Ensures every extension-contributed <see cref="EntityTypeDescriptor"/> is
    /// present in the loaded project's custom entity types (as a
    /// <see cref="Source"/>="extension" definition). Idempotent and a no-op when
    /// no extension host exists or no project is loaded.
    /// </summary>
    internal async Task RegisterExtensionEntityTypesAsync()
    {
        if (_extensions == null || Projects.CurrentProject == null) return;
        var service = new EntityService(Projects);
        foreach (var descriptor in _extensions.EntityTypes)
        {
            if (service.GetCustomEntityTypes().Any(t =>
                    string.Equals(t.TypeKey, descriptor.TypeKey, StringComparison.Ordinal)))
                continue;
            await service.SaveCustomEntityTypeAsync(MapExtensionEntityType(descriptor));
        }
    }

    private static CustomEntityTypeDefinition MapExtensionEntityType(EntityTypeDescriptor d) => new()
    {
        TypeKey = d.TypeKey,
        DisplayName = d.DisplayName,
        DisplayNamePlural = string.IsNullOrWhiteSpace(d.DisplayNamePlural) ? d.DisplayName : d.DisplayNamePlural,
        Icon = d.Icon,
        FolderName = string.IsNullOrWhiteSpace(d.FolderName) ? d.TypeKey : d.FolderName,
        Source = "extension",
        DefaultFields = d.DefaultFields.Select(f => new CustomEntityFieldDefinition
        {
            Key = f.Key,
            DisplayName = f.DisplayName,
            Type = WellKnownPropertyTypes.TryToEnum(f.TypeKey, out var enumType) ? enumType : CustomPropertyType.String,
            TypeKey = WellKnownPropertyTypes.TryToEnum(f.TypeKey, out _) ? null : f.TypeKey,
            DefaultValue = f.DefaultValue,
            EnumOptions = f.EnumOptions,
            Required = f.Required,
        }).ToList(),
        Features = new CustomEntityFeatures
        {
            IncludeImages = d.Features.IncludeImages,
            IncludeRelationships = d.Features.IncludeRelationships,
            IncludeSections = d.Features.IncludeSections,
        },
    };

    // ── Extension host-event raisers ────────────────────────────────
    // No-ops until an extension host has been created (extensions/load). They
    // never force-create it, so projects without extensions pay nothing.

    internal void RaiseProjectLoaded()
    {
        if (_hostServices == null) return;
        var project = Projects.CurrentProject;
        if (project == null) return;
        _hostServices.RaiseProjectLoaded(project.Name, Projects.ProjectRoot ?? string.Empty);
    }

    internal void RaiseSceneOpened(ChapterData chapter, SceneData scene)
        => _hostServices?.RaiseSceneOpened(scene.Id, scene.Title, chapter.Guid, chapter.Title, scene.WordCount);

    internal void RaiseSceneSaved(ChapterData chapter, SceneData scene)
        => _hostServices?.RaiseSceneSaved(scene.Id, scene.Title, chapter.Guid, chapter.Title, scene.WordCount);

    internal void RaiseBookChanged()
    {
        if (_hostServices == null) return;
        var book = Projects.ActiveBook;
        if (book == null) return;
        _hostServices.RaiseBookChanged(book.Id, book.Name);
    }

    /// <summary>Syncs the extension-facing language and fires LanguageChanged.</summary>
    internal void RaiseLanguageChanged(string language)
    {
        Extensions.Loc.Instance.CurrentLanguage = language;
        _hostServices?.RaiseLanguageChanged(language);
    }

    /// <summary>Keeps the extension-facing current language in step with settings
    /// without firing the change event (used when the host is first created).</summary>
    internal void SyncExtensionLanguage()
        => Extensions.Loc.Instance.CurrentLanguage = Settings.Effective.Language;
}
