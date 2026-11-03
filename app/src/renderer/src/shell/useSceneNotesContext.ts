import { useEffect } from 'react'
import type { SceneNotesContext } from '../../../shared/sceneNotesProtocol'
import { sameNotesScope } from '../../../shared/sceneNotesProtocol'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

/** Publish from workspace windows only. Keep the last selected scene when a
 * writer visits Research or Wiki; a different workspace clears that context. */
export function useSceneNotesContext(): void {
  useEffect(() => {
    if (!window.novalist.publishSceneNotesContext) return
    let previous: SceneNotesContext | null = null
    let last = ''
    const publish = (force = false): void => {
      const state = useProjectStore.getState()
      const scope = { projectPath: state.projectPath ?? '', bookId: state.activeBookId, draftId: state.activeDraftId, epoch: state.workspaceEpoch }
      const chapter = state.chapters.find((item) => item.guid === state.openChapterGuid)
      const scene = chapter?.scenes.find((item) => item.id === state.openSceneId)
      let next: SceneNotesContext | null = null
      if (state.isLoaded && !state.workspaceSuspended && !state.workspaceBusy && state.projectPath) {
        if (chapter && scene) next = { ...scope, chapterGuid: chapter.guid, sceneId: scene.id, title: scene.title }
        else if (previous && sameNotesScope(previous, { ...previous, ...scope }) &&
          state.chapters.some((item) => item.guid === previous?.chapterGuid && item.scenes.some((candidate) => candidate.id === previous?.sceneId))) next = previous
      }
      previous = next
      const key = JSON.stringify(next)
      if (!force && key === last) return
      last = key
      window.novalist.publishSceneNotesContext?.(next)
    }
    const activated = (): void => publish(true)
    const stop = useProjectStore.subscribe(() => publish())
    const stopState = window.novalist.onSceneNotesState?.((state) => useShellStore.setState({ notesDockVisible: state.visible }))
    window.addEventListener('focus', activated)
    publish(true)
    return () => { stop(); stopState?.(); window.removeEventListener('focus', activated) }
  }, [])
}
