#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<bool> RecordInactiveSharedSceneAsync(string root, string relative, string html)
    {
        foreach (var book in Projects.CurrentProject?.Books ?? [])
        {
            var drafts = book.Drafts.Count == 0 ? [book.FolderName] :
                book.Drafts.Select(draft => book.FolderName + "/Drafts/" + draft.FolderName).ToArray();
            foreach (var draft in drafts)
            {
                if (!relative.StartsWith(draft + '/' + book.ChapterFolder + '/', StringComparison.OrdinalIgnoreCase)) continue;
                var manifest = draft + (book.Drafts.Count == 0 ? "/.book/scenes.json" : "/scenes.json");
                return await SaveInactiveSceneCountAsync(root, relative, manifest, html);
            }
        }
        return false;
    }

    private async Task<bool> SaveInactiveSceneCountAsync(string root, string relative, string manifest, string html)
    {
        var path = SharedProjectPath.Resolve(root, manifest);
        if (!await FileService.ExistsAsync(path)) return false;
        await PrepareSharedProjectionPathAsync(manifest);
        var content = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject
            ?? throw new InvalidDataException("Invalid shared scenes manifest.");
        var id = sharedSceneFiles.Values.Single(file => file.Relative == relative).SceneId;
        var scene = (content["chapters"] as JsonObject)?.SelectMany(chapter => chapter.Value as JsonArray ?? [])
            .OfType<JsonObject>().SingleOrDefault(scene => scene["id"]?.GetValue<string>() == id);
        if (scene is null) return false;
        var count = CountWords(Core.Utilities.TextDiff.StripHtml(SharedBlockBoundary().Replace(html, "\n")));
        if (scene["wordCount"]?.GetValue<int>() != count)
        {
            scene["wordCount"] = count;
            await FileService.WriteTextAsync(path, content.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return true;
    }
}
#endif
