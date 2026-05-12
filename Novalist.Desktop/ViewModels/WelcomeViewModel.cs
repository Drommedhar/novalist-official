using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class WelcomeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    private string _newProjectLocation = string.Empty;

    [ObservableProperty]
    private string _newBookName = string.Empty;

    [ObservableProperty]
    private bool _isCreateFormOpen;

    [ObservableProperty]
    private string _selectedTemplateId = "blank";

    public IReadOnlyList<ProjectTemplate> Templates { get; }

    [RelayCommand]
    private void ToggleCreateForm() => IsCreateFormOpen = !IsCreateFormOpen;

    public ObservableCollection<RecentProjectCard> RecentProjects { get; } = new();

    public event Func<string, string, string, string, Task>? CreateProjectRequested;
    public event Func<string, Task>? OpenProjectRequested;
    public event Func<Task<string?>>? BrowseFolderRequested;
    public event Func<Task>? ImportPluginProjectRequested;
    public event Func<RecentProjectCard, Task>? RemoveRecentRequested;

    public WelcomeViewModel(IEnumerable<RecentProject> recentProjects, IReadOnlyList<ProjectTemplate>? templates = null)
    {
        Templates = templates ?? Array.Empty<ProjectTemplate>();
        foreach (var rp in recentProjects.OrderByDescending(r => r.LastOpened))
        {
            if (Directory.Exists(rp.Path))
                RecentProjects.Add(new RecentProjectCard(rp));
        }
    }

    [RelayCommand]
    private async Task BrowseLocation()
    {
        if (BrowseFolderRequested != null)
        {
            var folder = await BrowseFolderRequested.Invoke();
            if (!string.IsNullOrEmpty(folder))
                NewProjectLocation = folder;
        }
    }

    [RelayCommand]
    private async Task CreateProject()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName) || string.IsNullOrWhiteSpace(NewProjectLocation))
            return;

        var bookName = string.IsNullOrWhiteSpace(NewBookName) ? NewProjectName.Trim() : NewBookName.Trim();

        if (CreateProjectRequested != null)
            await CreateProjectRequested.Invoke(NewProjectLocation, NewProjectName.Trim(), bookName, SelectedTemplateId ?? "blank");
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        if (BrowseFolderRequested != null)
        {
            var folder = await BrowseFolderRequested.Invoke();
            if (!string.IsNullOrEmpty(folder) && OpenProjectRequested != null)
                await OpenProjectRequested.Invoke(folder);
        }
    }

    [RelayCommand]
    private async Task OpenRecentProject(RecentProjectCard card)
    {
        if (OpenProjectRequested != null)
            await OpenProjectRequested.Invoke(card.Project.Path);
    }

    [RelayCommand]
    private async Task ImportPluginProject()
    {
        if (ImportPluginProjectRequested != null)
            await ImportPluginProjectRequested.Invoke();
    }

    [RelayCommand]
    private async Task RemoveRecentProject(RecentProjectCard? card)
    {
        if (card == null) return;
        if (RemoveRecentRequested != null)
            await RemoveRecentRequested.Invoke(card);
        RecentProjects.Remove(card);
    }
}

public sealed class RecentProjectCard
{
    public RecentProject Project { get; }
    public string Name => Project.Name;
    public string Path => Project.Path;
    public Bitmap? CoverImage { get; }
    public bool HasCoverImage => CoverImage != null;

    public RecentProjectCard(RecentProject project)
    {
        Project = project;
        CoverImage = LoadCover(project.CoverImagePath);
    }

    private static Bitmap? LoadCover(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return null;

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 240);
        }
        catch
        {
            return null;
        }
    }
}
