using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class AndroidProjectAccess : MobileProjectAccess
{
    public override IFileService Files { get; } = new FileService();
    public override string DefaultRoot { get; } = Path.Combine(FileSystem.AppDataDirectory, "Projects");
    public override string? Resolve(string storedPath) => new PrivateProjectPaths(DefaultRoot).Resolve(storedPath);
    public override bool BeginAccess(string path) => Resolve(path) != null;
    public override void Release(string path) { }
    public override void Dispose() { }

    public override Task SetActiveAsync(string? path)
    {
        if (!string.IsNullOrEmpty(path) && !BeginAccess(path))
            throw new UnauthorizedAccessException("This project is outside the app's project directory.");
        return Task.CompletedTask;
    }

    public override Task<string> ReadAssetAsync(string path, string mimeType)
    {
        if (Resolve(path) == null) throw new UnauthorizedAccessException("Project access is required.");
        return Task.Run(() => MobileAssetReader.ReadDataUri(path, mimeType));
    }

    public override Task<string?> PickFolderAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        Directory.CreateDirectory(DefaultRoot);
        var projects = Directory.GetDirectories(DefaultRoot)
            .Where(path => File.Exists(Path.Combine(path, ".novalist", "project.json")))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCulture).ToArray();
        var page = Application.Current?.Windows.FirstOrDefault()?.Page
            ?? throw new InvalidOperationException("No window is available for choosing a project.");
        if (projects.Length == 0)
        {
            await page.DisplayAlertAsync("Open project", "Create a project first. External and cloud folders are not available in this development build.", "OK");
            return null;
        }
        var labels = projects.Select(path => Path.GetFileName(path)).ToArray();
        var selected = await page.DisplayActionSheetAsync("Open project", "Cancel", null, labels);
        var index = Array.IndexOf(labels, selected);
        return index < 0 ? null : projects[index];
    });
}
