using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Backend.Extensions;

public sealed partial class ExtensionManager
{
    /// <summary>
    /// Enumerates every contributed settings page together with its owning
    /// extension id, for the renderer's extension-settings surface.
    /// </summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, SettingsPage Page)> EnumerateSettingsPages()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not ISettingsContributor contributor) continue;
            foreach (var page in contributor.GetSettingsPages())
                yield return (e.Manifest.Id, e.Manifest.Name, page);
        }
    }

    /// <summary>
    /// Enumerates every contributed wizard together with its owning extension id.
    /// </summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, Novalist.Sdk.Models.Wizards.WizardDefinition Def)> EnumerateWizards()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not IWizardContributor contributor) continue;
            foreach (var def in contributor.GetWizards())
                yield return (e.Manifest.Id, e.Manifest.Name, def);
        }
    }

    /// <summary>Finds a contributed wizard definition by extension + wizard id, or
    /// null when not found. Returns a freshly built definition whose runtime
    /// callbacks are live.</summary>
    public Novalist.Sdk.Models.Wizards.WizardDefinition? FindWizard(string extensionId, string wizardId)
    {
        var extension = Extensions.FirstOrDefault(e =>
            e.IsLoaded && e.Manifest.Id == extensionId && e.Instance is IWizardContributor);
        if (extension?.Instance is not IWizardContributor contributor)
            return null;
        return contributor.GetWizards().FirstOrDefault(w => w.Id == wizardId);
    }

    // ── Contribution surfacing + execution (Electron host bridges) ───────
    // These enumerate the live contributor instances (so runtime closures stay
    // valid) and assign each surfaced item a stable, opaque id the renderer
    // echoes back to invoke the matching callback across the RPC boundary.

    /// <summary>Extension-contributed inline actions, flattened across all
    /// registered <see cref="IInlineActionContributor"/>s and ordered by
    /// priority (lower first), matching the desktop editor's ordering.</summary>
    public IReadOnlyList<Novalist.Sdk.Hooks.InlineActionDescriptor> GetInlineActionDescriptors()
        => _hostServices.GetInlineActionContributors()
            .SelectMany(c => c.GetInlineActions())
            .OrderBy(a => a.Priority)
            .ToList();

    /// <summary>Runs the inline action with the given id against the request, by
    /// locating the contributor that owns it. Null when no contributor claims the
    /// id.</summary>
    public async Task<Novalist.Sdk.Hooks.InlineActionResult?> ExecuteInlineActionAsync(
        string actionId, Novalist.Sdk.Hooks.InlineActionRequest request, CancellationToken cancellationToken)
    {
        foreach (var contributor in _hostServices.GetInlineActionContributors())
        {
            if (contributor.GetInlineActions().Any(a => a.Id == actionId))
                return await contributor.ExecuteAsync(actionId, request, cancellationToken);
        }
        return null;
    }

    /// <summary>Whether any loaded extension offers an enabled article generator
    /// (drives the Wiki's "Generate summary" affordance).</summary>
    public bool IsArticleGeneratorAvailable
        => ArticleGenerators.Any(g => g.IsArticleGeneratorEnabled);

    /// <summary>Generates an article summary using the first enabled generator,
    /// or null when none is available.</summary>
    public async Task<Novalist.Sdk.Hooks.ArticleGenerationResult?> GenerateArticleAsync(
        Novalist.Sdk.Hooks.ArticleGenerationRequest request, CancellationToken cancellationToken)
    {
        var generator = ArticleGenerators.FirstOrDefault(g => g.IsArticleGeneratorEnabled);
        if (generator == null) return null;
        return await generator.GenerateAsync(request, cancellationToken);
    }

    /// <summary>Whether any loaded extension offers an enabled entity extractor
    /// (drives the Inspector's "scan this scene" affordance).</summary>
    public bool IsEntityExtractorAvailable
        => EntityExtractors.Any(e => e.IsEntityExtractorEnabled);

    /// <summary>Proposes Codex entries for a passage using the first enabled
    /// extractor, or null when none is available. Proposals only — the caller
    /// decides what (if anything) is written.</summary>
    public async Task<Novalist.Sdk.Hooks.EntityExtractionResult?> ExtractEntitiesAsync(
        Novalist.Sdk.Hooks.EntityExtractionRequest request, CancellationToken cancellationToken)
    {
        var extractor = EntityExtractors.FirstOrDefault(e => e.IsEntityExtractorEnabled);
        if (extractor == null) return null;
        return await extractor.ExtractAsync(request, cancellationToken);
    }

    /// <summary>Enumerates contributed context-menu items with a stable id
    /// ("{extensionId}#ctx#{index}") for round-trip execution.</summary>
    public IEnumerable<(string Id, string ExtensionId, ContextMenuItem Item)> EnumerateContextMenuItemsWithIds()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not IContextMenuContributor contributor) continue;
            var items = contributor.GetContextMenuItems();
            for (var i = 0; i < items.Count; i++)
                yield return ($"{e.Manifest.Id}#ctx#{i}", e.Manifest.Id, items[i]);
        }
    }

    /// <summary>Invokes a contributed context-menu item's click handler with the
    /// supplied context object (respecting its visibility guard).</summary>
    public void ExecuteContextMenuItem(string id, object? context)
    {
        var match = EnumerateContextMenuItemsWithIds().FirstOrDefault(x => x.Id == id).Item;
        if (match == null) return;
        if (match.IsVisible != null && !match.IsVisible(context)) return;
        match.OnClick?.Invoke(context);
    }

    /// <summary>Enumerates contributed status-bar items with a stable id
    /// ("{extensionId}#sb#{itemId}") plus their current text/tooltip.</summary>
    public IEnumerable<(string Id, string ExtensionId, StatusBarItem Item)> EnumerateStatusBarItemsWithIds()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not IStatusBarContributor contributor) continue;
            foreach (var item in contributor.GetStatusBarItems())
                yield return ($"{e.Manifest.Id}#sb#{item.Id}", e.Manifest.Id, item);
        }
    }

    /// <summary>Invokes a contributed status-bar item's click handler.</summary>
    public void ExecuteStatusBarItem(string id)
    {
        var match = EnumerateStatusBarItemsWithIds().FirstOrDefault(x => x.Id == id).Item;
        match?.OnClick?.Invoke();
    }

    /// <summary>Enumerates contributed theme overrides with their owning extension
    /// id and folder. The folder is what a theme's
    /// <see cref="ThemeOverride.ResourcePath"/> resolves against.</summary>
    public IEnumerable<(string ExtensionId, string FolderPath, ThemeOverride Theme)> EnumerateThemes()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not IThemeContributor contributor) continue;
            foreach (var theme in contributor.GetThemeOverrides())
                yield return (e.Manifest.Id, e.FolderPath, theme);
        }
    }

    /// <summary>Enumerates contributors of a declarative settings schema.</summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, Novalist.Sdk.Hooks.ISettingsSchemaContributor Contributor)> EnumerateSettingsSchemas()
    {
        foreach (var e in Extensions)
        {
            if (!e.IsLoaded || e.Instance is not ISettingsSchemaContributor contributor) continue;
            yield return (e.Manifest.Id, e.Manifest.Name, contributor);
        }
    }

    /// <summary>Applies edited settings values for the schema-contributing
    /// extension with the given id. No-op when the id is unknown.</summary>
    public async Task ApplySettingsSchemaAsync(string extensionId, IReadOnlyDictionary<string, string> values)
    {
        var match = EnumerateSettingsSchemas().FirstOrDefault(x => x.ExtensionId == extensionId);
        if (match.Contributor == null) return;
        await match.Contributor.ApplySettingsAsync(values);
    }

    /// <summary>Runs a schema action button for the given extension and returns
    /// the refreshed schema (or null when the id is unknown or the action made no
    /// change).</summary>
    public async Task<Novalist.Sdk.Models.SettingsSchema?> ExecuteSchemaActionAsync(
        string extensionId, string actionKey, IReadOnlyDictionary<string, string> values)
    {
        var match = EnumerateSettingsSchemas().FirstOrDefault(x => x.ExtensionId == extensionId);
        if (match.Contributor == null) return null;
        return await match.Contributor.ExecuteSchemaActionAsync(actionKey, values);
    }

    /// <summary>
    /// Returns the <see cref="HostServices"/> instance for event wiring.
    /// </summary>
    internal HostServices Host => _hostServices;
}
