import { pushEditorEntityNames } from './editorEntities'
import { useRef } from 'react'
import { type EditorWindow } from './editorBridge'
import { useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { useWikiStore } from '../../stores/wikiStore'
import { useEntityPeek, type PeekScope } from './PeekCard'

export function useEditorPeek(pane: string, openSceneId: string | null) {
  const chapters = useProjectStore((s) => s.chapters)
  const entityIndexRef = useRef<
    Map<
      string,
      { id: string; name: string; detail: string; imagePath: string | null; type: string }
    >
  >(new Map())
  const openHoveredEntity = (entityType: string, entityId: string): void => {
    // A coined word has no Wiki article; it has a dictionary entry.
    if (entityType === 'conlang') {
      const word = conlangWordsRef.current.get(entityId)
      if (word) useShellStore.getState().navigateToLanguage(word)
      return
    }
    useShellStore.getState().setMainView('wiki')
    void useWikiStore.getState().openArticle(entityType, entityId)
  }

  // The pane's open chapter/scene, resolved from the live chapter list so a peek
  // over a character shows the values overridden for the scope currently in view.
  const scopeChapter = chapters.find((c) => c.scenes.some((s) => s.id === openSceneId))
  const peekScope: PeekScope = {
    chapterGuid: scopeChapter?.guid ?? null,
    chapterTitle: scopeChapter?.title ?? null,
    sceneTitle: scopeChapter?.scenes.find((s) => s.id === openSceneId)?.title ?? null,
    sceneId: openSceneId
  }
  // Shared focus-peek overlay: owns the show/hide debounce, the pointer-over-card
  // guard, pin state, and viewport-clamped positioning. Driven here by the iframe's
  // entity hover/exit messages; the context sidebar drives the same hook itself.
  /** Coined word id -> the word, so clicking a peek can search for it. */
  const conlangWordsRef = useRef(new Map<string, string>())

  const peek = useEntityPeek({ scope: peekScope, onOpen: openHoveredEntity })
  // Read the latest controls from inside the (rarely re-created) listener effect.
  const peekRef = useRef(peek)
  peekRef.current = peek

  // Same reason: the listener outlives a scene switch, and a cut filed under
  // the scene the writer was in twenty minutes ago is a cut they cannot place.
  const sceneTitleRef = useRef(peekScope.sceneTitle)
  sceneTitleRef.current = peekScope.sceneTitle

  const pushEntityNames = (editor: EditorWindow): Promise<void> =>
    pushEditorEntityNames(editor, pane, entityIndexRef, conlangWordsRef)
  return { entityIndexRef, peekScope, peek, peekRef, sceneTitleRef, pushEntityNames }
}
