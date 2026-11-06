import { metadataBinding } from '../collaboration/SharedMetadataField'
import { writeSharedMetadata } from '../collaboration/metadataCommands'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

interface SceneTarget { chapterGuid: string; sceneId: string }

export async function setSceneMetadata<T>(targets: SceneTarget[], key: string, value: T,
  writeUnshared: (target: SceneTarget) => Promise<unknown>): Promise<void> {
  let refresh = false
  for (const target of targets) {
    const scene = useProjectStore.getState().chapters.find(chapter => chapter.guid === target.chapterGuid)?.scenes.find(scene => scene.id === target.sceneId)
    const binding = metadataBinding(scene, key)
    if (binding) await writeSharedMetadata(binding, value)
    else { await writeUnshared(target); refresh = true }
  }
  if (refresh) useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
}
