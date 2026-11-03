import { useEffect, useMemo, useSyncExternalStore } from 'react'
import type { SceneNotesContext } from '../../../shared/sceneNotesProtocol'
import { rpc } from '../rpc/client'
import { registerPendingWrite, retainPendingWrite } from '../stores/pendingWrites'
import { useProjectStore } from '../stores/projectStore'
import { SceneNotesDocument } from './sceneNotesDocument'
import { SharedSceneNotesDocument } from './sharedSceneNotesDocument'
import { openSharedMarkdown } from '../collaboration/sharedMarkdown'
import { normalizeChecklist } from './sceneChecklist'

// A scene reopened while its last save is pending must rejoin that same
// document, not load an older disk value and create a competing writer.
const sessions = new Map<string, { document: SceneNotesDocument | SharedSceneNotesDocument; mounted: number }>()

export function useSceneNotesDocument(context: SceneNotesContext) {
  const { projectPath, bookId, draftId, chapterGuid, sceneId } = context
  const prose = useProjectStore(state => state.chapters.find(chapter => chapter.guid === chapterGuid)?.scenes.find(scene => scene.id === sceneId)?.sharedProse)
  const shared = useMemo(() => prose?.fields.notes ? { sessionId: prose.sessionId, itemId: prose.fields.notes } : undefined,
    [prose?.sessionId, prose?.fields.notes])
  // Transport epochs change on recovery; the authored document does not.
  const key = JSON.stringify(['scene-notes', projectPath, bookId, draftId, chapterGuid, sceneId, shared?.sessionId, shared?.itemId])
  const session = useMemo(() => {
    const existing = sessions.get(key)
    if (existing) return existing
    const created = { document: shared ? new SharedSceneNotesDocument(changed => openSharedMarkdown(shared, changed)) : new SceneNotesDocument(
      async () => (await rpc.request<{ notes: string | null }>('scenes/getMeta', [chapterGuid, sceneId])).notes ?? '',
      async (text) => {
        const workspace = useProjectStore.getState()
        if (workspace.projectPath !== projectPath || workspace.activeBookId !== bookId || workspace.activeDraftId !== draftId ||
          !workspace.chapters.some((chapter) => chapter.guid === chapterGuid && chapter.scenes.some((scene) => scene.id === sceneId))) {
          throw new Error('The scene workspace changed. Reopen its notes before saving.')
        }
        // The document serializes its own writes. Retain the live document on
        // unmount, never a second retry queue holding an obsolete text payload.
        await rpc.request('scenes/setNotes', [chapterGuid, sceneId, text])
      }
    ), mounted: 0 }
    sessions.set(key, created)
    return created
  }, [key, projectPath, bookId, draftId, chapterGuid, sceneId, shared])
  const { document } = session
  const state = useSyncExternalStore(document.subscribe, document.snapshot)

  useEffect(() => {
    session.mounted++
    sessions.set(key, session)
    const stop = registerPendingWrite(() => document.flush())
    void (document.state.loaded && !shared ? Promise.resolve() : document.load()).then(() => {
      if (!document.state.loaded) return
      // Merely opening shared notes must not concurrently insert duplicate
      // checkbox markers into another writer's plain Markdown lists.
      if (!shared) document.update(normalizeChecklist(document.state.text))
      void document.flush().catch(() => {})
    })
    return () => {
      document.cancelLoad()
      session.mounted--
      stop()
      if (session.mounted) return
      // Retain the live document, including debounce-only edits and ambiguous
      // failed acknowledgements. A later edit supersedes its retry payload.
      if (document.dirty) retainPendingWrite(`${key}:surface`, async () => {
        await document.flush()
        if (!session.mounted && sessions.get(key) === session) { sessions.delete(key); document.dispose() }
      })
      else if (sessions.get(key) === session) { sessions.delete(key); document.dispose() }
    }
  }, [document, key, session, shared])

  useEffect(() => {
    if (!document.dirty) return
    const timer = setTimeout(() => { void document.flush().catch(() => {}) }, 400)
    return () => clearTimeout(timer)
  }, [document, state.text])

  return { document, state, shared, markdownSession: document instanceof SharedSceneNotesDocument ? document.markdownSession : undefined }
}
