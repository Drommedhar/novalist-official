using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class ResearchService : IResearchService
{
    private const string ResearchFolderName = "Research";

    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;

    public ResearchService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    public IReadOnlyList<ResearchItem> GetAll()
    {
        var p = _projectService.CurrentProject;
        if (p == null) return Array.Empty<ResearchItem>();
        return p.ResearchItems.OrderBy(r => r.Order).ThenBy(r => r.CreatedAt).ToList();
    }

    public async Task SaveAsync(ResearchItem item)
    {
        var p = _projectService.CurrentProject;
        if (p == null) return;
        item.UpdatedAt = DateTime.UtcNow;
        var idx = p.ResearchItems.FindIndex(r => r.Id == item.Id);
        if (idx >= 0) p.ResearchItems[idx] = item;
        else
        {
            item.Order = p.ResearchItems.Count;
            p.ResearchItems.Add(item);
        }
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(string itemId)
    {
        var p = _projectService.CurrentProject;
        if (p == null) return;
        p.ResearchItems.RemoveAll(r => r.Id == itemId);
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task<string> ImportFileAsync(string sourcePath)
    {
        var root = _projectService.ProjectRoot
            ?? throw new InvalidOperationException("No project loaded.");
        var dir = _fileService.CombinePath(root, ResearchFolderName);
        await _fileService.CreateDirectoryAsync(dir).ConfigureAwait(false);

        var fileName = _fileService.GetFileName(sourcePath);
        var dest = _fileService.CombinePath(dir, fileName);

        // Avoid clobbering existing files: append numeric suffix.
        var attempt = 1;
        while (await _fileService.ExistsAsync(dest).ConfigureAwait(false))
        {
            var stem = _fileService.GetFileNameWithoutExtension(sourcePath);
            var ext = System.IO.Path.GetExtension(sourcePath);
            dest = _fileService.CombinePath(dir, $"{stem} ({attempt}){ext}");
            attempt++;
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(sourcePath).ConfigureAwait(false);
        await System.IO.File.WriteAllBytesAsync(dest, bytes).ConfigureAwait(false);

        return _fileService.CombinePath(ResearchFolderName, _fileService.GetFileName(dest)).Replace('\\', '/');
    }

    public string GetAbsolutePath(string relativePath)
    {
        var root = _projectService.ProjectRoot;
        if (root == null || string.IsNullOrEmpty(relativePath)) return string.Empty;
        return _fileService.CombinePath(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
