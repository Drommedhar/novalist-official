using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    private ResearchService Research => new(_projectService, _fileService);

    // ── Research (IExtensionResearchService) ───────────────────────

    IReadOnlyList<ResearchItemInfo> IExtensionResearchService.GetAll()
        => [.. Research.GetAll().Select(ToInfo)];

    async Task<string> IExtensionResearchService.SaveAsync(ResearchItemInfo item)
    {
        var research = Research;
        var existing = string.IsNullOrEmpty(item.Id)
            ? null
            : research.GetAll().FirstOrDefault(i => i.Id == item.Id);

        var data = existing ?? new ResearchItem();
        if (!string.IsNullOrEmpty(item.Id)) data.Id = item.Id;
        data.Title = item.Title ?? string.Empty;
        data.Type = Enum.TryParse<ResearchItemType>(item.Type, true, out var type)
            ? type
            : ResearchItemType.Note;
        data.Content = item.Content ?? string.Empty;
        data.Tags = [.. item.Tags ?? []];
        data.EntityRefs = [.. item.EntityRefs ?? []];
        data.UpdatedAt = DateTime.UtcNow;

        await research.SaveAsync(data);
        return data.Id;
    }

    async Task<bool> IExtensionResearchService.DeleteAsync(string itemId)
    {
        var research = Research;
        if (research.GetAll().All(i => i.Id != itemId)) return false;
        await research.DeleteAsync(itemId);
        return true;
    }

    Task<string> IExtensionResearchService.ImportFileAsync(string sourcePath)
        => Research.ImportFileAsync(sourcePath);

    string IExtensionResearchService.GetFullPath(string relativePath)
    {
        var root = _projectService.ProjectRoot;
        if (root == null || string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        // A stored path that climbs out of the project would let an extension
        // read anything on the machine through a research item.
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : string.Empty;
    }

    private static ResearchItemInfo ToInfo(ResearchItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Type = item.Type.ToString(),
        Content = item.Content,
        Tags = [.. item.Tags],
        EntityRefs = [.. item.EntityRefs]
    };
}
