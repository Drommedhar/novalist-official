using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Hooks;

namespace Novalist.Desktop.Services;

/// <summary>
/// View model for the extension management overlay.
/// </summary>
public partial class ExtensionsViewModel : ObservableObject
{
    private readonly ExtensionManager _manager;

    public ObservableCollection<ExtensionItemViewModel> Items { get; } = [];
    public bool HasExtensions => Items.Count > 0;

    public ExtensionsViewModel(ExtensionManager manager)
    {
        _manager = manager;
        Refresh();
    }

    public void Refresh()
    {
        Items.Clear();
        foreach (var info in _manager.Extensions)
        {
            Items.Add(new ExtensionItemViewModel(info, _manager));
        }
        OnPropertyChanged(nameof(HasExtensions));
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

    [ObservableProperty]
    private bool _isEnabled;

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

    public ExtensionItemViewModel(ExtensionInfo info, ExtensionManager manager)
    {
        _info = info;
        _manager = manager;
        _isEnabled = info.IsEnabled;
    }

    async partial void OnIsEnabledChanged(bool value)
    {
        if (value)
            await _manager.EnableExtensionAsync(Id);
        else
            await _manager.DisableExtensionAsync(Id);

        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
    }
}
