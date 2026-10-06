import type { BinderState } from './binderState'
import type { ContextMenuItem } from './ContextMenu'
import { sceneMenuContext } from './binderSceneMenuContext'
import { sceneLabelItems, sceneStageItems } from './binderSceneProperties'
import { sceneMergeItems, sceneExportItems, sceneBookmarkItems, sceneActivityItems, sceneLifecycleItems } from './binderSceneActions'
import { chapterMenuItems } from './binderChapterMenu'
import { reorderItems } from './binderMenuMovement'

export function binderMenuItems(state: BinderState): ContextMenuItem[] {
  const { menu, chapters, store } = state
  if (!menu) return []
  const chapter = chapters.find((item) => item.guid === menu.chapterGuid)
  if (!chapter) return []
  if (!menu.sceneId) return chapterMenuItems(state, chapter)
  const scene = chapter.scenes.find((item) => item.id === menu.sceneId)
  if (!scene) return []
  const context = sceneMenuContext(state, chapter, scene)
  return [
    ...reorderItems(state, context.sceneIndex, chapter.scenes.map((item) => item.order),
      (order) => { void store.getState().reorderScene(chapter.guid, scene.id, order) }),
    ...sceneLabelItems(context),
    ...sceneStageItems(context),
    ...sceneMergeItems(context),
    ...sceneExportItems(context),
    ...sceneBookmarkItems(context),
    ...sceneActivityItems(context),
    ...sceneLifecycleItems(context)
  ]
}
