import type { BinderState } from './binderState'
import { useSelectionStore } from '../stores/selectionStore'
type Chapter = BinderState['chapters'][number]
export function sceneMenuContext(state: BinderState, chapter: Chapter, scene: Chapter['scenes'][number]) {
  const { t, chapters } = state

      // Touch has no drag-reorder, so mobile gets explicit Move up/down entries
      // (using the neighbour's order, matching the desktop drag semantics).
      const sceneIndex = chapter.scenes.findIndex((s) => s.id === scene.id)
      const selection = useSelectionStore.getState().sceneIds
      const targets: { chapterGuid: string; sceneId: string }[] =
        selection.length > 1 && selection.includes(scene.id)
          ? chapters.flatMap((c) =>
              c.scenes
                .filter((sc) => selection.includes(sc.id))
                .map((sc) => ({ chapterGuid: c.guid, sceneId: sc.id }))
            )
          : [{ chapterGuid: chapter.guid, sceneId: scene.id }]

      /** Appends "(N scenes)" so a menu row never silently does more than it says. */
      const scoped = (label: string): string =>
        targets.length > 1 ? `${label} (${t('bulk.scopeCount', { count: targets.length })})` : label
  return { state, chapter, scene, sceneIndex, targets, scoped }
}
export type SceneMenuContext = ReturnType<typeof sceneMenuContext>
