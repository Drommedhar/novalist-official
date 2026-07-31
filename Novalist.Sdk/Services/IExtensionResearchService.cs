namespace Novalist.Sdk.Services;

/// <summary>One research item as an extension sees it.</summary>
public sealed class ResearchItemInfo
{
    /// <summary>Empty when creating; the host fills it in and returns it.</summary>
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>"Note", "Link", "Image", "Pdf", "Audio", "Video" or "File".</summary>
    public string Type { get; init; } = "Note";

    /// <summary>Prose for a note, the URL for a link, a project-relative path for a file.</summary>
    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Codex entries this item is about.</summary>
    public IReadOnlyList<string> EntityRefs { get; init; } = [];
}

/// <summary>
/// Research items, readable and writable.
///
/// A networked capture extension - fetch a page, keep the readable text - had
/// nowhere to put what it fetched: it could read a project and not add a note
/// to it. This is the smallest surface that makes that kind of extension
/// possible without handing out the project file.
/// </summary>
public interface IExtensionResearchService
{
    /// <summary>Every research item in the open project.</summary>
    IReadOnlyList<ResearchItemInfo> GetAll();

    /// <summary>
    /// Creates or updates an item. An empty <see cref="ResearchItemInfo.Id"/>
    /// creates; anything else updates the item with that id, or creates one
    /// under it if there is none. Returns the id it was stored under.
    /// </summary>
    Task<string> SaveAsync(ResearchItemInfo item);

    /// <summary>Deletes an item. False when the id is unknown.</summary>
    Task<bool> DeleteAsync(string itemId);

    /// <summary>
    /// Copies a file into the project's research folder and returns the
    /// project-relative path to store as an item's content. The file is copied
    /// rather than referenced so the project stays self-contained.
    /// </summary>
    Task<string> ImportFileAsync(string sourcePath);

    /// <summary>
    /// Where a File, Image or Pdf item's content actually is on disk.
    ///
    /// An item stores a project-relative path so the project can be moved or
    /// shared without breaking, which leaves an extension holding a path it
    /// cannot open. Empty when there is no project.
    /// </summary>
    string GetFullPath(string relativePath);
}
