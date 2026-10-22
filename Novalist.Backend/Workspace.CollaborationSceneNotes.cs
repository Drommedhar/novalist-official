#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private string? SharedSceneManifestPath() => Projects.ActiveDraftRoot is { } draft && sharedRoot is { } root
        ? Path.GetRelativePath(root, Path.Combine(draft, "scenes.json")).Replace('\\', '/') : null;

    internal SharedEntityFieldsDto? SharedSceneProse(string id)
    {
        var path = SharedSceneManifestPath();
        if (path is null || Projects.ProjectRoot != sharedRoot) return null;
        var fields = ActiveSharedProse.Where(binding => binding.Path == path && binding.TextField?.SceneId == id)
            .ToDictionary(binding => RequireTextField(binding).Key, binding => binding.ItemId, StringComparer.Ordinal);
        return fields.Count == 0 ? null : new(sharedSession, fields);
    }

    private void ProjectSceneNote(SharedDocumentState binding, SharedTextField field, string text)
    {
        if (binding.Path != SharedSceneManifestPath()) return;
        var scene = Projects.ScenesManifest?.Chapters.Values.SelectMany(scenes => scenes).SingleOrDefault(scene => scene.Id == field.SceneId);
        if (scene is null) return;
        switch (field.Key)
        {
            case "synopsis": scene.Synopsis = text; break;
            case "notes": scene.Notes = text; break;
            case "goal": scene.Goal = text; break;
            case "outcome": scene.Outcome = text; break;
        }
    }
}
#endif
