using System.Globalization;
using System.Text.RegularExpressions;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class DashboardRpc
{
    /// <summary>
    /// Returns the active book's cover image as a path relative to the project
    /// root so the renderer can serve it through <c>novalist-project://</c>, or
    /// null when no cover is set. Prefers the book cover, falling back to the
    /// project-level cover.
    /// </summary>
    [JsonRpcMethod("dashboard/getCover")]
    public Task<string?> GetCoverAsync()
    {
        var projects = _workspace.Projects;
        var stored = projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.CoverImage;
        return Task.FromResult(Resolve(stored));
    }

    /// <summary>
    /// Imports the picked image into the active book's image folder and records
    /// the resulting book-relative path as both the project and active-book
    /// portrait cover. A null or blank path clears the cover instead.
    /// </summary>
    [JsonRpcMethod("dashboard/setCover")]
    public async Task SetCoverAsync(string? path)
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var relative = await ImportOrClearAsync(path);
        project.CoverImage = relative;
        if (projects.ActiveBook != null)
            projects.ActiveBook.CoverImage = relative;
        await projects.SaveProjectAsync();
        // Keep the welcome-screen thumbnail in step with the new/removed cover.
        await _workspace.RefreshRecentProjectAsync();
    }

    /// <summary>
    /// Returns the active book's wide Dashboard banner as a project-relative
    /// path, or null when none. Prefers the book banner, then the project
    /// banner, then falls back to the book / project portrait cover so
    /// pre-split projects keep rendering their existing banner.
    /// </summary>
    [JsonRpcMethod("dashboard/getBanner")]
    public Task<string?> GetBannerAsync()
    {
        var projects = _workspace.Projects;
        var stored = projects.ActiveBook?.BannerImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.BannerImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.CoverImage;
        return Task.FromResult(Resolve(stored));
    }

    /// <summary>
    /// Imports the picked image and records the resulting book-relative path as
    /// both the project and active-book banner. A null or blank path clears the
    /// banner (the Dashboard then falls back to the portrait cover).
    /// </summary>
    [JsonRpcMethod("dashboard/setBanner")]
    public async Task SetBannerAsync(string? path)
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var relative = await ImportOrClearAsync(path);
        project.BannerImage = relative;
        if (projects.ActiveBook != null)
            projects.ActiveBook.BannerImage = relative;
        await projects.SaveProjectAsync();
    }

    private string? Resolve(string? stored)
        => string.IsNullOrEmpty(stored) ? null : _entities.ResolveProjectRelativeImage(stored);

    private async Task<string> ImportOrClearAsync(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : await _entities.ImportImageAsync(path);
}
