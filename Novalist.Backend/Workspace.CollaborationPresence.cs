#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed record SharedParticipantDto(Guid DeviceId, Guid ViewId, Guid ItemId, string Name,
    CursorSelection? Selection, string? SceneId);

public sealed partial class Workspace
{
    internal IReadOnlyList<SharedParticipantDto> DescribeSharedParticipants(IReadOnlyList<ParticipantPresence> participants)
    {
        var scenes = new Dictionary<Guid, string>();
        if (sharedRoot is { } root && Projects.ProjectRoot == root && Projects.ActiveBook is { } book && Projects.ScenesManifest is { } manifest)
        {
            foreach (var chapter in book.Chapters)
                foreach (var scene in manifest.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                {
                    var relative = Path.GetRelativePath(root, Projects.GetSceneFilePath(chapter, scene)).Replace('\\', '/');
                    if (sharedItems.TryGetValue(relative, out var item)) scenes[item] = scene.Id;
                }
        }
        return participants.Select(participant => new SharedParticipantDto(participant.DeviceId, participant.ViewId,
            participant.ItemId, participant.Name, participant.Selection, scenes.GetValueOrDefault(participant.ItemId))).ToArray();
    }
}
#endif
