#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private readonly AsyncLocal<ProjectionScope?> sharedProjection = new();
    private readonly Dictionary<string, byte[]?> sharedPendingWrites = new(StringComparer.Ordinal);
    private bool sharedWriteRejected;

    internal IDisposable BeginSharedProjection()
    {
        var previous = sharedProjection.Value;
        var scope = new ProjectionScope(previous, () => sharedProjection.Value = previous);
        sharedProjection.Value = scope;
        return scope;
    }

    private bool IsSharedProjection => sharedProjection.Value?.IsActive == true;

    private sealed class ProjectionScope(ProjectionScope? parent, Action restore) : IDisposable
    {
        private bool active = true;
        internal bool IsActive => active || parent?.IsActive == true;
        public void Dispose() { active = false; restore(); }
    }

    internal async Task ValidateSharedWriteAsync(string path, byte[]? content, bool structural)
    {
        if (sharedProjectFiles is null || IsSharedProjection || SharedWritePath(path) is not { } relative) return;
        var disk = await ReadFilteredSharedFileAsync(path, relative);
        var expected = sharedPendingWrites.TryGetValue(relative, out var pending) ? pending : sharedDiskFiles.GetValueOrDefault(relative);
        if (!structural && !SameFileBytes(disk, expected)) RejectSharedWrite("This file changed outside collaboration. Its contents will be preserved in Recovered edits before retrying.");
        if (content is not null && sharedDiskFiles.TryGetValue(relative, out var original) &&
            SameSharedFileContent(relative, original, await SharedProjectFiles.FilterContentAsync(relative, content, CancellationToken.None))) return;
        if (fieldSession?.IsConnected != true) RejectSharedWrite("Reconnect before changing shared project details or structure. Shared prose remains available offline.");
        if (Coordinator is not null && WorkspaceRequestContext.Lease is not { Held: true })
            RejectSharedWrite("Shared project changes must run through the workspace coordinator.");
        if (!structural && content is not null) await ValidateBoundContentAsync(path, relative, content);
    }

    private async Task ValidateBoundContentAsync(string path, string relative, byte[] content)
    {
        if (sharedItems.ContainsKey(relative)) RejectSharedWrite("Save shared prose through its collaboration editor.");
        var bindings = fieldBindings.Values.Where(binding => binding.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        var prose = sharedProse.Values.Where(binding => binding.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        if ((bindings.Length == 0 && prose.Length == 0) || !await FileService.ExistsAsync(path)) return;
        var previous = JsonNode.Parse(await FileService.ReadBytesAsync(path)) as JsonObject;
        var next = JsonNode.Parse(content) as JsonObject;
        if (previous is null || next is null) RejectSharedWrite("A shared Codex entry must remain valid metadata.");
        foreach (var binding in bindings)
        {
            if (binding.Metadata is not null && ((previous is not null && SharedMetadataFields.Target(previous, binding) is null) ||
                (next is not null && SharedMetadataFields.Target(next, binding) is null))) continue;
            var before = binding.Metadata is not null && previous is not null ? SharedMetadataFields.Read(previous, binding) : ReadBoundValue(previous, binding.Key, binding.Custom);
            var after = binding.Metadata is not null && next is not null ? SharedMetadataFields.Read(next, binding) : ReadBoundValue(next, binding.Key, binding.Custom);
            if (!JsonNode.DeepEquals(before, after)) RejectSharedWrite("Edit shared Codex fields through their focus lock.");
        }
        foreach (var binding in prose)
        {
            var field = RequireTextField(binding);
            var before = previous is null ? null : SharedProseFields.Target(previous, field);
            var after = next is null ? null : SharedProseFields.Target(next, field);
            if (before is not null && after is not null && !JsonNode.DeepEquals(before[field.Key], after[field.Key]))
                RejectSharedWrite("Save shared prose through its collaboration editor.");
        }
    }

    private static JsonNode? ReadBoundValue(JsonObject? entity, string key, bool custom)
    {
        if (entity is null) return null;
        var fields = custom ? entity[PropertyName(entity, "fields")] as JsonObject : entity;
        return fields?[custom ? key : PropertyName(fields, key)];
    }

    private static bool SameSharedFileContent(string path, byte[]? before, byte[]? after)
    {
        if (SameFileBytes(before, after)) return true;
        if (before is null || after is null || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        JsonNode? original;
        JsonNode? updated;
        try { original = JsonNode.Parse(before); updated = JsonNode.Parse(after); }
        catch (System.Text.Json.JsonException) { return false; }
        if (path == ".novalist/project.json" && original is JsonObject oldProject && updated is JsonObject newProject)
            PreserveNavigation(newProject, oldProject);
        return JsonNode.DeepEquals(original, updated);
    }

    private void RejectSharedWrite(string message)
    {
        sharedWriteRejected = true;
        throw new InvalidOperationException(message);
    }

    internal async Task RecordSharedWriteAsync(string path, byte[]? content)
    {
        if (sharedProjectFiles is null || SharedWritePath(path) is not { } relative) return;
        var filtered = content is null ? null : await SharedProjectFiles.FilterContentAsync(relative, content, CancellationToken.None);
        if (!IsSharedProjection) { sharedPendingWrites[relative] = filtered; return; }
        if (filtered is null) sharedDiskFiles.Remove(relative);
        else sharedDiskFiles[relative] = filtered;
        sharedPendingWrites.Remove(relative);
    }

    private string? SharedWritePath(string path)
    {
        if (sharedRoot is not { } root) return null;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)) return null;
        if (relative.StartsWith("Recovered edits/", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = relative.Split('/');
        if (parts.Any(part => part.Equals("extensions", StringComparison.OrdinalIgnoreCase) ||
            (part.StartsWith('.') && part is not ".novalist" and not ".book" and not ".nvchapter.json"))) return null;
        if (relative.StartsWith(".novalist/", StringComparison.Ordinal) &&
            relative is not ".novalist/project.json" and not ".novalist/settings.json" and not ".novalist/darlings.json" and not ".novalist/gallery.json" &&
            !new[] { ".novalist/analysis", ".novalist/wiki", ".novalist/narration/voices" }.Any(folder => relative == folder || relative.StartsWith(folder + '/', StringComparison.Ordinal))) return null;
        foreach (var book in Projects.CurrentProject?.Books ?? [])
        {
            if (IsWithin(relative, book.FolderName + '/' + book.SnapshotFolder)) return null;
            foreach (var draft in book.Drafts)
                if (IsWithin(relative, book.FolderName + "/Drafts/" + draft.FolderName + '/' + book.SnapshotFolder)) return null;
        }
        if (Projects.CurrentProject is { } project && IsWithin(relative, project.WorldBibleFolder + "/Snapshots")) return null;
        return relative;
    }

    private static bool IsWithin(string path, string folder) => path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + '/', StringComparison.OrdinalIgnoreCase);
}
#endif
