using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    /// <summary>
    /// The projects worth offering to reopen.
    ///
    /// An entry whose folder is no longer a project is dropped from the list and
    /// from the stored settings as it is found, rather than being offered and
    /// failing when it is clicked. The list is a set of ways back into work, and
    /// a row that cannot be opened is not one of those - it is a dead end the
    /// writer has to learn to skip past.
    ///
    /// Dropped only when the folder is provably gone, though. This used to
    /// forget any entry it could not read, which on iOS is every entry on every
    /// launch: the projects sit behind security-scoped grants that are not
    /// resumed until a project is opened, so building the list was enough to
    /// erase it - permanently, because the pruned list is written back. Now a
    /// path that cannot be read is first handed to the platform's
    /// <see cref="IStoredPathResolver"/>, which resumes the grant and follows
    /// the app container if an update moved it underneath the project, and only
    /// a folder still provably absent afterwards is forgotten. One that simply
    /// cannot be seen right now is offered anyway - opening it takes the same
    /// path, grant and all, and an offline volume is not a deleted book.
    /// </summary>
    public async Task<RecentProjectDto[]> GetRecentProjectsAsync(bool includeLibraryDetails = true)
    {
        await Settings.LoadAsync();
        var results = new List<RecentProjectDto>();
        var changed = false;

        // Over a copy: the loop removes from the list it is walking.
        foreach (var r in Settings.Settings.RecentProjects.ToList())
        {
            string? resolvedAccess = null;
            try
            {
                var path = r.Path;
                var presence = await Projects.ProbeProjectAsync(path);

                if (presence != ProjectPresence.Present && _storedPaths != null)
                {
                    var resolved = resolvedAccess = _storedPaths.Resolve(path);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        var after = await Projects.ProbeProjectAsync(resolved);
                        if (after == ProjectPresence.Present && await MatchesRecentIdentityAsync(r, resolved))
                        {
                            if (!string.Equals(resolved, path, StringComparison.Ordinal))
                            {
                                // Mutates the entry in place, so r.CoverImagePath below
                                // is the moved one.
                                Settings.RelocateRecentProject(path, resolved);
                                changed = true;
                            }
                            path = resolved;
                            presence = after;
                        }
                        else if (after == ProjectPresence.Present)
                            presence = ProjectPresence.Unknown;
                    }
                }

                if (presence == ProjectPresence.Absent)
                {
                    Settings.RemoveRecentProject(r.Path);
                    changed = true;
                    continue;
                }

                // The File menu needs names and paths, never megabytes of covers
                // or manifests from every other project while the writer works.
                // Unknown means the volume/grant is unavailable. Keep its entry,
                // without opening manifests or covers the presence probe could
                // not reach. These reads used to incur retry delays per project.
                var readDetails = includeLibraryDetails && presence == ProjectPresence.Present;
                var entry = new RecentProjectDto(r.Name, path, null);
                results.Add(readDetails ? await ReadLibrarySummaryAsync(entry) : entry);
            }
            finally
            {
                if (!string.IsNullOrEmpty(resolvedAccess)) _storedPaths?.Release(resolvedAccess);
            }
        }

        if (changed) await Settings.SaveAsync();
        return results.ToArray();
    }

    private async Task<bool> MatchesRecentIdentityAsync(RecentProject recent, string resolved)
    {
        if (string.IsNullOrEmpty(recent.ProjectId)) return true;
        try
        {
            var json = await FileService.ReadTextAsync(Path.Combine(resolved, ".novalist", "project.json"));
            var project = System.Text.Json.JsonSerializer.Deserialize<ProjectMetadata>(json);
            return project?.Id == recent.ProjectId;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Read the manifest and book covers; never open books or load their drafts.</summary>
    internal async Task<RecentProjectDto> ReadLibrarySummaryAsync(RecentProjectDto entry)
    {
        try
        {
            var json = await FileService.ReadTextAsync(Path.Combine(entry.Path, ".novalist", "project.json"));
            var metadata = System.Text.Json.JsonSerializer.Deserialize<ProjectMetadata>(json);
            if (metadata is not { Name.Length: > 0, Books: not null } ||
                metadata.Books.Any(book => book is not { Id.Length: > 0, Name.Length: > 0, FolderName: not null })) return entry;
            var books = new List<LibraryBookDto>();
            foreach (var book in metadata.Books)
            {
                // Legacy single-book projects can keep their cover on the project.
                // Never give an uncovered volume a different book's artwork.
                var cover = string.IsNullOrEmpty(book.CoverImage) && metadata.Books.Count == 1
                    ? metadata.CoverImage : book.CoverImage;
                var path = string.IsNullOrEmpty(cover) ? null
                    : Path.Combine(entry.Path, book.FolderName, cover.Replace('/', Path.DirectorySeparatorChar));
                books.Add(new LibraryBookDto(book.Id, book.Name, await LoadCoverDataUriAsync(path)));
            }
            return entry with
            {
                Name = metadata.Name,
                ProjectId = metadata.Id,
                Books = books.ToArray(),
                Cover = books.FirstOrDefault(book => book.Id == metadata.ActiveBookId)?.Cover,
                HasWorldBible = !string.IsNullOrWhiteSpace(metadata.WorldBibleFolder)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An offline project is still on its shelf. Unknown details are
            // deliberately distinct from an empty project.
            return entry;
        }
    }

    /// <summary>Absolute filesystem path of the active project's portrait cover
    /// image (book cover, falling back to the project cover), or null when none
    /// is set or no project is open.</summary>
    internal string? ActiveCoverAbsolutePath()
    {
        var rel = Projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(rel))
            rel = Projects.CurrentProject?.CoverImage;
        if (string.IsNullOrEmpty(rel) || Projects.ActiveBookRoot == null)
            return null;
        return Path.Combine(Projects.ActiveBookRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Refreshes the active project's name and cover in recents.</summary>
    internal async Task RefreshRecentProjectAsync()
    {
        var root = Projects.ProjectRoot;
        var project = Projects.CurrentProject;
        if (root == null || project == null) return;
        var recent = Settings.Settings.RecentProjects.FirstOrDefault(r => r.Path == root);
        if (recent == null) return;
        recent.Name = project.Name;
        recent.ProjectId = project.Id;
        recent.CoverImagePath = ActiveCoverAbsolutePath() ?? string.Empty;
        await Settings.SaveAsync();
    }

    /// <summary>Reads a recent project's cover file and returns it as a base64
    /// <c>data:</c> URI, or null when the path is empty or the file is absent.
    /// Recent projects are not the active project, so their cover cannot be
    /// served through <c>novalist-project://</c> (which resolves against the
    /// active root) — the bytes are inlined instead.</summary>
    internal static async Task<string?> LoadCoverDataUriAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            return $"data:{MimeForExtension(Path.GetExtension(path))};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cover must never take down the recents list. Under the macOS App
            // Sandbox a recent project we don't currently hold access to — e.g. one
            // in iCloud Drive, whose cover file may be dataless — passes File.Exists
            // but throws on read. Degrade to no thumbnail instead of failing
            // GetRecentProjectsAsync (which would leave the start screen empty).
            return null;
        }
    }

    internal static string MimeForExtension(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
    }
}
