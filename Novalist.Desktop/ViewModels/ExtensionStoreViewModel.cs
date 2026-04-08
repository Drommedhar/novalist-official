using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.Services;

/// <summary>
/// View model for the extension store browse tab.
/// </summary>
public partial class ExtensionStoreViewModel : ObservableObject
{
    private readonly IExtensionGalleryService _gallery;
    private readonly ExtensionManager _manager;
    private List<GalleryEntry> _allEntries = [];
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<StoreExtensionItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    // ── Detail view state ──

    [ObservableProperty]
    private bool _isDetailVisible;

    [ObservableProperty]
    private StoreExtensionItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _detailReadme = string.Empty;

    [ObservableProperty]
    private string _detailReleaseNotes = string.Empty;

    [ObservableProperty]
    private string _detailHtml = string.Empty;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    [ObservableProperty]
    private bool _isDetailLoading;

    /// <summary>
    /// Raised after an extension is successfully installed from the store.
    /// The string argument is the extension ID.
    /// </summary>
    public event EventHandler<string>? ExtensionInstalled;

    public ExtensionStoreViewModel(IExtensionGalleryService gallery, ExtensionManager manager)
    {
        _gallery = gallery;
        _manager = manager;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            _allEntries = await _gallery.FetchGalleryIndexAsync(ct);

            // For each entry, fetch latest compatible release (in parallel, batched)
            var tasks = _allEntries.Select(async entry =>
            {
                try
                {
                    var release = await _gallery.GetLatestCompatibleReleaseAsync(entry, ct);
                    return (entry, release, error: (string?)null);
                }
                catch
                {
                    return (entry, release: (GalleryRelease?)null, error: (string?)null);
                }
            });

            var results = await Task.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();

            Items.Clear();
            foreach (var (entry, release, _) in results)
            {
                var meta = _gallery.ReadStoreMeta(entry.Id);
                var isInstalled = meta is { InstalledFromGallery: true };
                var installedVersion = meta?.InstalledVersion;
                var hasUpdate = isInstalled && release != null &&
                                !string.IsNullOrEmpty(installedVersion) &&
                                IsNewer(release.Version, installedVersion);

                Items.Add(new StoreExtensionItemViewModel(entry, release, this)
                {
                    IsInstalled = isInstalled,
                    InstalledVersion = installedVersion,
                    HasUpdate = hasUpdate,
                    IsCompatible = release != null
                });
            }

            IsEmpty = Items.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Cancelled, ignore
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ShowDetailAsync(StoreExtensionItemViewModel item)
    {
        SelectedItem = item;
        IsDetailVisible = true;
        IsDetailLoading = true;
        DetailReadme = string.Empty;
        DetailReleaseNotes = item.ReleaseBody;

        try
        {
            DetailReadme = await _gallery.FetchReadmeAsync(item.Repo);
        }
        catch
        {
            DetailReadme = "*Could not load README.*";
        }
        finally
        {
            IsDetailLoading = false;
            BuildDetailHtml();
        }
    }

    [RelayCommand]
    public void HideDetail()
    {
        IsDetailVisible = false;
        SelectedItem = null;
        DetailHtml = string.Empty;
    }

    private void BuildDetailHtml()
    {
        var readmeHtml = string.IsNullOrWhiteSpace(DetailReadme)
            ? ""
            : Markdown.ToHtml(DetailReadme, MarkdownPipeline);

        var releaseHtml = string.IsNullOrWhiteSpace(DetailReleaseNotes)
            ? ""
            : $"<hr><h2>{Localization.Loc.Instance["extensions.store.releaseNotes"]}</h2>"
              + Markdown.ToHtml(DetailReleaseNotes, MarkdownPipeline);

        DetailHtml = $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
                * { box-sizing: border-box; }
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    font-size: 13px;
                    line-height: 1.6;
                    color: #ddd;
                    background: transparent;
                    margin: 0;
                    padding: 8px 4px;
                    word-wrap: break-word;
                }
                h1 { font-size: 1.5em; border-bottom: 1px solid #444; padding-bottom: 0.3em; }
                h2 { font-size: 1.3em; border-bottom: 1px solid #333; padding-bottom: 0.2em; }
                h3 { font-size: 1.1em; }
                a { color: #58a6ff; text-decoration: none; }
                a:hover { text-decoration: underline; }
                code {
                    background: rgba(255,255,255,0.08);
                    padding: 0.15em 0.4em;
                    border-radius: 3px;
                    font-size: 0.9em;
                }
                pre {
                    background: rgba(255,255,255,0.06);
                    padding: 12px;
                    border-radius: 6px;
                    overflow-x: auto;
                }
                pre code { background: none; padding: 0; }
                img { max-width: 100%; height: auto; border-radius: 4px; }
                blockquote {
                    border-left: 3px solid #444;
                    margin: 0;
                    padding: 0 1em;
                    color: #aaa;
                }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #444; padding: 6px 12px; text-align: left; }
                th { background: rgba(255,255,255,0.06); }
                hr { border: none; border-top: 1px solid #444; margin: 1.5em 0; }
                ul, ol { padding-left: 1.5em; }
            </style>
            </head>
            <body>
            {{readmeHtml}}
            {{releaseHtml}}
            </body>
            </html>
            """;
    }

    [RelayCommand]
    public async Task InstallAsync(StoreExtensionItemViewModel item)
    {
        if (item.LatestRelease is null) return;

        item.IsInstalling = true;
        try
        {
            var zipPath = await _gallery.DownloadExtensionZipAsync(item.LatestRelease, new Progress<double>(p =>
            {
                item.InstallProgress = p;
            }));

            await _gallery.InstallExtensionAsync(zipPath, item.Entry, item.LatestRelease);

            // Handle dependencies
            var meta = _gallery.ReadStoreMeta(item.Entry.Id);
            if (meta != null)
            {
                await InstallDependenciesAsync(item.Entry.Id);
            }

            item.IsInstalled = true;
            item.InstalledVersion = item.LatestRelease.Version;
            item.HasUpdate = false;

            ExtensionInstalled?.Invoke(this, item.Id);
        }
        catch (Exception ex)
        {
            item.InstallError = ex.Message;
        }
        finally
        {
            item.IsInstalling = false;
            item.InstallProgress = 0;
        }
    }

    [RelayCommand]
    public async Task UninstallAsync(StoreExtensionItemViewModel item)
    {
        try
        {
            // Shutdown the extension if it's loaded
            if (_manager.Extensions.FirstOrDefault(e =>
                    string.Equals(e.Manifest.Id, item.Id, StringComparison.OrdinalIgnoreCase)) is { } info)
            {
                await _manager.DisableExtensionAsync(item.Id);
            }

            await _gallery.UninstallExtensionAsync(item.Id);
            item.IsInstalled = false;
            item.InstalledVersion = null;
            item.HasUpdate = false;
        }
        catch (Exception ex)
        {
            item.InstallError = ex.Message;
        }
    }

    [RelayCommand]
    public async Task UpdateAsync(StoreExtensionItemViewModel item)
    {
        if (item.LatestRelease is null) return;

        // Shutdown extension before updating
        if (_manager.Extensions.FirstOrDefault(e =>
                string.Equals(e.Manifest.Id, item.Id, StringComparison.OrdinalIgnoreCase)) is { } info)
        {
            await _manager.DisableExtensionAsync(item.Id);
        }

        await InstallAsync(item);
        item.NeedsRestart = true;
    }

    private async Task InstallDependenciesAsync(string extensionId)
    {
        var extensionsDir = ExtensionLoader.GetExtensionsDirectory();
        var manifestPath = System.IO.Path.Combine(extensionsDir, extensionId, "extension.json");
        if (!System.IO.File.Exists(manifestPath))
            return;

        var json = await System.IO.File.ReadAllTextAsync(manifestPath);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<Sdk.ExtensionManifest>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest?.Dependencies is not { Count: > 0 })
            return;

        foreach (var depId in manifest.Dependencies)
        {
            // Skip if already installed
            var depDir = System.IO.Path.Combine(extensionsDir, depId);
            if (System.IO.Directory.Exists(depDir))
                continue;

            // Find in gallery
            var depEntry = _allEntries.FirstOrDefault(e =>
                string.Equals(e.Id, depId, StringComparison.OrdinalIgnoreCase));
            if (depEntry is null) continue;

            var depRelease = await _gallery.GetLatestCompatibleReleaseAsync(depEntry);
            if (depRelease is null) continue;

            var zipPath = await _gallery.DownloadExtensionZipAsync(depRelease);
            await _gallery.InstallExtensionAsync(zipPath, depEntry, depRelease);

            // Recurse for transitive dependencies
            await InstallDependenciesAsync(depId);
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var item in Items)
                item.IsVisible = true;
            return;
        }

        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in Items)
        {
            item.IsVisible = terms.All(t =>
                item.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                item.Author.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static bool IsNewer(string remote, string current)
    {
        var remoteParts = ParseVersionParts(remote);
        var currentParts = ParseVersionParts(current);

        for (var i = 0; i < 3; i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }

        return false;
    }

    private static int[] ParseVersionParts(string version)
    {
        var dash = version.IndexOf('-');
        if (dash >= 0) version = version[..dash];

        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }
}

/// <summary>
/// View model for a single extension in the store browse list.
/// </summary>
public partial class StoreExtensionItemViewModel : ObservableObject
{
    private readonly ExtensionStoreViewModel _parent;

    public GalleryEntry Entry { get; }
    public GalleryRelease? LatestRelease { get; }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Author => Entry.Author;
    public string Repo => Entry.Repo;
    public List<string> Tags => Entry.Tags;
    public string? Icon => LatestRelease?.Icon ?? null;
    public string LatestVersion => LatestRelease?.Version ?? "—";
    public string ReleaseBody => LatestRelease?.Body ?? string.Empty;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isCompatible = true;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _needsRestart;

    public string StatusText
    {
        get
        {
            if (NeedsRestart) return Localization.Loc.Instance["extensions.restart"];
            if (!IsCompatible) return "Incompatible";
            if (HasUpdate) return $"Update to {LatestVersion}";
            if (IsInstalled) return "Installed";
            return "Install";
        }
    }

    public StoreExtensionItemViewModel(GalleryEntry entry, GalleryRelease? latestRelease, ExtensionStoreViewModel parent)
    {
        Entry = entry;
        LatestRelease = latestRelease;
        _parent = parent;
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnHasUpdateChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnIsCompatibleChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnNeedsRestartChanged(bool value) => OnPropertyChanged(nameof(StatusText));
}
