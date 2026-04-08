using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;

namespace Novalist.Desktop.Services;

/// <summary>
/// View model for the extension management overlay.
/// </summary>
public partial class ExtensionsViewModel : ObservableObject
{
    private readonly ExtensionManager _manager;
    private IExtensionGalleryService? _galleryService;

    public ObservableCollection<ExtensionItemViewModel> Items { get; } = [];
    public bool HasExtensions => Items.Count > 0;

    /// <summary>
    /// Number of extensions with available updates.
    /// </summary>
    [ObservableProperty]
    private int _updateCount;

    /// <summary>
    /// 0 = Installed tab, 1 = Browse Store tab.
    /// </summary>
    [ObservableProperty]
    private int _selectedTab;

    [ObservableProperty]
    private ExtensionStoreViewModel? _store;

    public ExtensionsViewModel(ExtensionManager manager, IExtensionGalleryService? galleryService = null)
    {
        _manager = manager;
        _galleryService = galleryService;
        if (galleryService != null)
        {
            Store = new ExtensionStoreViewModel(galleryService, manager);
            Store.ExtensionInstalled += OnStoreExtensionInstalled;
        }
        Refresh();
    }

    private async void OnStoreExtensionInstalled(object? sender, string extensionId)
    {
        await _manager.DiscoverAndEnableAsync(extensionId);
        Refresh();
    }

    partial void OnSelectedTabChanged(int value)
    {
        if (value == 1 && Store is { Items.Count: 0, IsLoading: false })
        {
            _ = Store.LoadAsync();
        }
    }

    [RelayCommand]
    private void SelectTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
            SelectedTab = idx;
    }

    public void Refresh()
    {
        Items.Clear();
        foreach (var info in _manager.Extensions)
        {
            var item = new ExtensionItemViewModel(info, _manager, _galleryService);

            // Check if this is a gallery-installed extension and populate store meta
            if (_galleryService != null)
            {
                var meta = _galleryService.ReadStoreMeta(info.Manifest.Id);
                item.IsFromGallery = meta is { InstalledFromGallery: true };
            }

            Items.Add(item);
        }
        OnPropertyChanged(nameof(HasExtensions));
    }

    /// <summary>
    /// Checks for available updates for all installed gallery extensions.
    /// Returns the number of extensions with updates.
    /// </summary>
    public async Task<int> CheckForExtensionUpdatesAsync(CancellationToken ct = default)
    {
        if (_galleryService is null)
            return 0;

        try
        {
            var updates = await _galleryService.CheckForUpdatesAsync(ct);
            var updateDict = updates.ToDictionary(u => u.ExtensionId, StringComparer.OrdinalIgnoreCase);

            foreach (var item in Items)
            {
                if (updateDict.TryGetValue(item.Id, out var updateInfo))
                {
                    item.AvailableVersion = updateInfo.AvailableVersion;
                    item.UpdateRelease = updateInfo.Release;
                    item.UpdateEntry = updateInfo.Entry;
                    item.HasUpdate = true;
                }
            }

            UpdateCount = updates.Count;
            return updates.Count;
        }
        catch
        {
            return 0;
        }
    }

    [RelayCommand]
    private void OpenExtensionsFolder()
    {
        var path = ExtensionLoader.GetExtensionsDirectory();
        Directory.CreateDirectory(path);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo("explorer.exe", path));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"\"{path}\"", UseShellExecute = false });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = path, UseShellExecute = false });
    }
}

/// <summary>
/// View model for a single extension in the management list.
/// </summary>
public partial class ExtensionItemViewModel : ObservableObject
{
    private readonly ExtensionInfo _info;
    private readonly ExtensionManager _manager;
    private readonly IExtensionGalleryService? _galleryService;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string? _availableVersion;

    [ObservableProperty]
    private bool _isFromGallery;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _needsRestart;

    public GalleryRelease? UpdateRelease { get; set; }
    public GalleryEntry? UpdateEntry { get; set; }

    public string Id => _info.Manifest.Id;
    public string Name => _info.Manifest.Name;
    public string Description => _info.Manifest.Description;
    public string Version => _info.Manifest.Version;
    public string Author => _info.Manifest.Author;
    public bool HasError => !string.IsNullOrWhiteSpace(_info.LoadError);
    public string ErrorMessage => _info.LoadError ?? string.Empty;
    public bool IsLoaded => _info.IsLoaded;
    public bool HasSettings => _info.Instance is ISettingsContributor;
    public string SettingsKey => $"ext_{Name}";

    public ExtensionItemViewModel(ExtensionInfo info, ExtensionManager manager, IExtensionGalleryService? galleryService = null)
    {
        _info = info;
        _manager = manager;
        _galleryService = galleryService;
        _isEnabled = info.IsEnabled;
    }

    private bool _suppressEnabledHandler;

    async partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressEnabledHandler) return;

        if (value)
            await _manager.EnableExtensionAsync(Id);
        else
            await _manager.DisableExtensionAsync(Id);

        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    [RelayCommand]
    private async Task UpdateExtensionAsync()
    {
        if (_galleryService is null || UpdateRelease is null || UpdateEntry is null || IsUpdating)
            return;

        IsUpdating = true;
        try
        {
            // Unload extension so DLLs can be replaced
            await _manager.DisableExtensionAsync(Id);

            var tempZip = await _galleryService.DownloadExtensionZipAsync(UpdateRelease);
            await _galleryService.InstallExtensionAsync(tempZip, UpdateEntry, UpdateRelease);

            // Re-persist enabled state so it's enabled on next restart
            // (DisableExtensionAsync saved IsEnabled=false to settings)
            await _manager.EnableExtensionAsync(Id);

            // Keep the toggle on without triggering the handler again
            _suppressEnabledHandler = true;
            IsEnabled = true;
            _suppressEnabledHandler = false;
            HasUpdate = false;
            AvailableVersion = null;
            UpdateRelease = null;
            UpdateEntry = null;
            NeedsRestart = true;
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
