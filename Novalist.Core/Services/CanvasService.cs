using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ICanvasService
{
    Task<CanvasData> CreateAsync(string name);
    Task<CanvasData?> LoadAsync(string canvasId);
    Task SaveAsync(CanvasData canvas);
    Task<bool> DeleteAsync(string canvasId);
    IReadOnlyList<CanvasReference> List();
}

/// <summary>
/// Planning boards: loose cards and author-drawn connectors on an infinite
/// surface, stored one JSON file per board beside the maps.
///
/// A board is deliberately outside the manuscript structure. Nothing on it
/// affects chapters, scenes or word counts until the writer promotes a card,
/// which is a separate, explicit act.
/// </summary>
public sealed class CanvasService : ICanvasService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;

    public CanvasService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    /// <summary>
    /// Folder the board files live in, beside Maps under the draft. Blank roots
    /// are treated as absent, not as the current directory - combining onto ""
    /// would write boards to a relative path wherever the process happens to be.
    /// </summary>
    public string GetCanvasRoot()
    {
        var root = _projectService.ActiveDraftRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = _projectService.ActiveBookRoot;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("No active book.");

        return _fileService.CombinePath(root, "Canvases");
    }

    public IReadOnlyList<CanvasReference> List() =>
        _projectService.ActiveBook?.Canvases ?? [];

    public async Task<CanvasData> CreateAsync(string name)
    {
        var book = _projectService.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var id = $"canvas-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var canvas = new CanvasData { Id = id, Name = name };

        await _fileService.CreateDirectoryAsync(GetCanvasRoot());
        await SaveAsync(canvas);

        book.Canvases.Add(new CanvasReference
        {
            Id = id,
            Name = name,
            FileName = $"{id}.json",
            CreatedAt = DateTime.UtcNow
        });
        await _projectService.SaveProjectAsync();
        return canvas;
    }

    public async Task<CanvasData?> LoadAsync(string canvasId)
    {
        var reference = Find(canvasId);
        if (reference == null)
            return null;

        var path = _fileService.CombinePath(GetCanvasRoot(), reference.FileName);
        if (!await _fileService.ExistsAsync(path))
            return null;

        try
        {
            var json = await _fileService.ReadTextAsync(path);
            var canvas = JsonSerializer.Deserialize<CanvasData>(json);
            if (canvas == null)
                return null;

            // The reference is the naming authority: renaming a board updates the
            // book, and the file should follow rather than fight it.
            canvas.Name = reference.Name;
            return canvas;
        }
        catch (JsonException)
        {
            // A board file edited by hand into invalid JSON reads as "not there"
            // rather than taking the view down.
            return null;
        }
    }

    public async Task SaveAsync(CanvasData canvas)
    {
        var root = GetCanvasRoot();
        await _fileService.CreateDirectoryAsync(root);

        var reference = Find(canvas.Id);
        var fileName = reference?.FileName ?? $"{canvas.Id}.json";
        await _fileService.WriteTextAsync(
            _fileService.CombinePath(root, fileName),
            JsonSerializer.Serialize(canvas, JsonOptions));

        if (reference != null && !string.Equals(reference.Name, canvas.Name, StringComparison.Ordinal))
        {
            reference.Name = canvas.Name;
            await _projectService.SaveProjectAsync();
        }
    }

    public async Task<bool> DeleteAsync(string canvasId)
    {
        var book = _projectService.ActiveBook;
        var reference = Find(canvasId);
        if (book == null || reference == null)
            return false;

        var path = _fileService.CombinePath(GetCanvasRoot(), reference.FileName);
        if (await _fileService.ExistsAsync(path))
            await _fileService.DeleteFileAsync(path);

        book.Canvases.Remove(reference);
        await _projectService.SaveProjectAsync();
        return true;
    }

    private CanvasReference? Find(string canvasId) =>
        _projectService.ActiveBook?.Canvases
            .FirstOrDefault(c => string.Equals(c.Id, canvasId, StringComparison.OrdinalIgnoreCase));
}
