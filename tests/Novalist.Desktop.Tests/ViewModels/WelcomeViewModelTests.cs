using Novalist.Core.Models;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class WelcomeViewModelTests
{
    [AvaloniaFact]
    public void Ctor_FiltersRecentByExistingPath_OrdersByLastOpened()
    {
        using var dir = new TempDir();
        var existing = new RecentProject { Name = "A", Path = dir.Path, LastOpened = DateTime.UtcNow };
        var missing = new RecentProject { Name = "B", Path = Path.Combine(dir.Path, "gone"), LastOpened = DateTime.UtcNow.AddDays(-1) };

        var vm = new WelcomeViewModel(new[] { existing, missing },
            new List<ProjectTemplate> { new() { Id = "blank" } });

        Assert.Single(vm.RecentProjects);
        Assert.Equal("A", vm.RecentProjects[0].Name);
        Assert.Single(vm.Templates);
    }

    [AvaloniaFact]
    public void ToggleCreateForm()
    {
        var vm = new WelcomeViewModel(Array.Empty<RecentProject>());
        Assert.False(vm.IsCreateFormOpen);
        vm.ToggleCreateFormCommand.Execute(null);
        Assert.True(vm.IsCreateFormOpen);
    }

    [AvaloniaFact]
    public async Task BrowseLocation_SetsLocation_AndHandlesNullFolderAndNoHandler()
    {
        var vm = new WelcomeViewModel(Array.Empty<RecentProject>());
        await vm.BrowseLocationCommand.ExecuteAsync(null); // no handler -> no-op

        vm.BrowseFolderRequested += () => Task.FromResult<string?>("/picked");
        await vm.BrowseLocationCommand.ExecuteAsync(null);
        Assert.Equal("/picked", vm.NewProjectLocation);

        var vm2 = new WelcomeViewModel(Array.Empty<RecentProject>());
        vm2.BrowseFolderRequested += () => Task.FromResult<string?>(null); // null folder -> not set
        await vm2.BrowseLocationCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm2.NewProjectLocation);
    }

    [AvaloniaFact]
    public async Task CreateProject_ValidatesAndInvokesEvent_WithBookNameFallback()
    {
        var vm = new WelcomeViewModel(Array.Empty<RecentProject>());
        (string loc, string name, string book, string tmpl)? captured = null;
        vm.CreateProjectRequested += (l, n, b, t) => { captured = (l, n, b, t); return Task.CompletedTask; };

        await vm.CreateProjectCommand.ExecuteAsync(null); // empty -> no event
        Assert.Null(captured);

        vm.NewProjectName = "  Proj  ";
        vm.NewProjectLocation = "/loc";
        // NewBookName empty -> book falls back to trimmed project name.
        await vm.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal(("/loc", "Proj", "Proj", "blank"), captured);
    }

    [AvaloniaFact]
    public async Task OpenProject_BrowsesThenOpens()
    {
        var vm = new WelcomeViewModel(Array.Empty<RecentProject>());
        string? opened = null;
        vm.BrowseFolderRequested += () => Task.FromResult<string?>("/p");
        vm.OpenProjectRequested += p => { opened = p; return Task.CompletedTask; };
        await vm.OpenProjectCommand.ExecuteAsync(null);
        Assert.Equal("/p", opened);
    }

    [AvaloniaFact]
    public async Task OpenRecent_Import_Wizard_InvokeEvents()
    {
        using var dir = new TempDir();
        var vm = new WelcomeViewModel(new[] { new RecentProject { Name = "A", Path = dir.Path, LastOpened = DateTime.UtcNow } });
        string? opened = null; var imported = false; var wizard = false;
        vm.OpenProjectRequested += p => { opened = p; return Task.CompletedTask; };
        vm.ImportPluginProjectRequested += () => { imported = true; return Task.CompletedTask; };
        vm.RunProjectWizardRequested += () => { wizard = true; return Task.CompletedTask; };

        await vm.OpenRecentProjectCommand.ExecuteAsync(vm.RecentProjects[0]);
        await vm.ImportPluginProjectCommand.ExecuteAsync(null);
        await vm.RunProjectWizardCommand.ExecuteAsync(null);
        Assert.Equal(dir.Path, opened);
        Assert.True(imported);
        Assert.True(wizard);
    }

    [AvaloniaFact]
    public async Task RemoveRecent_RemovesCardAndInvokesEvent_NullCardNoOp()
    {
        using var dir = new TempDir();
        var vm = new WelcomeViewModel(new[] { new RecentProject { Name = "A", Path = dir.Path, LastOpened = DateTime.UtcNow } });
        var card = vm.RecentProjects[0];
        RecentProjectCard? removed = null;
        vm.RemoveRecentRequested += c => { removed = c; return Task.CompletedTask; };

        await vm.RemoveRecentProjectCommand.ExecuteAsync(null); // null -> no-op
        await vm.RemoveRecentProjectCommand.ExecuteAsync(card);
        Assert.Same(card, removed);
        Assert.Empty(vm.RecentProjects);
    }

    [AvaloniaFact]
    public void RecentProjectCard_LoadsCover_AndHandlesMissing()
    {
        using var dir = new TempDir();
        var img = Path.Combine(dir.Path, "cover.png");
        File.WriteAllBytes(img, new byte[] { 1, 2, 3 });
        var withCover = new RecentProjectCard(new RecentProject { Name = "A", Path = dir.Path, CoverImagePath = img });
        Assert.True(withCover.HasCoverImage);
        Assert.Equal(dir.Path, withCover.Path);

        var noCover = new RecentProjectCard(new RecentProject { Name = "B", Path = dir.Path, CoverImagePath = null! });
        Assert.False(noCover.HasCoverImage);
    }

    [AvaloniaFact]
    public void RecentProjectCard_CoverLoadFailure_NoImage()
    {
        if (!OperatingSystem.IsWindows())
            return; // exclusive locks don't block reads on Unix
        using var dir = new TempDir();
        var img = Path.Combine(dir.Path, "locked.png");
        File.WriteAllBytes(img, new byte[] { 1, 2, 3 });
        using var hold = new FileStream(img, FileMode.Open, FileAccess.Read, FileShare.None);
        // File.OpenRead inside LoadCover throws -> catch -> null cover.
        var card = new RecentProjectCard(new RecentProject { Name = "C", Path = dir.Path, CoverImagePath = img });
        Assert.False(card.HasCoverImage);
    }
}
