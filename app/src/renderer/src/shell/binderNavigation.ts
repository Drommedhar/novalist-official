import { useNarrationStore } from '../stores/narrationStore'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

/** All binder tabs follow the active reading workspace; explicit editor commands don't. */
export function openBinderScene(chapterGuid: string, sceneId: string): void {
  if (useShellStore.getState().mainView === 'narration') {
    useNarrationStore.getState().navigateToScene(sceneId)
  } else {
    void useProjectStore.getState().openScene(chapterGuid, sceneId)
  }
}
