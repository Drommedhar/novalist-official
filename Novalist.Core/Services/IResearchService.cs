using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IResearchService
{
    IReadOnlyList<ResearchItem> GetAll();
    Task SaveAsync(ResearchItem item);
    Task DeleteAsync(string itemId);

    /// <summary>Copies an external file into the project's research folder and
    /// returns the project-relative path that should be stored in
    /// <see cref="ResearchItem.Content"/>.</summary>
    Task<string> ImportFileAsync(string sourcePath);

    /// <summary>Resolves a project-relative file path to absolute.</summary>
    string GetAbsolutePath(string relativePath);
}
