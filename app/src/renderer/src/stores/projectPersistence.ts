import { rpc } from '../rpc/client'
import { type EditorPaneState, type EditingSceneClaim } from './projectTypes'
import { useProjectStore, mirror, mapEditors } from './projectStore'

const AUTOSAVE_DELAY_MS = 2000

export const autosaveTimers = new Map<string, ReturnType<typeof setTimeout>>()

/** A timer disappears as soon as it starts its write, so keep the write itself
 *  visible until the backend has answered. A shutdown flush can then await it
 *  instead of issuing the same scene write again with the same disk hash. */
export const autosaveWrites = new Map<string, Promise<void>>()

/** Writes a pane's unsaved edit, if it has one. */
export async function flushEditor(editor: EditorPaneState | undefined): Promise<void> {
  if (!editor?.isDirty || !editor.chapterGuid || !editor.sceneId || editor.html === null) return
  await saveScene(editor.chapterGuid, editor.sceneId, editor.html, editor.plainText ?? '')
}

/** Strips HTML tags and decodes entities to plain text for live statistics.
 * Mirrors the desktop EditorViewModel.StripHtmlForStats fast path. */
export function stripHtml(html: string): string {
  if (!html) return ''
  if (!html.trimStart().startsWith('<')) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  return doc.body.textContent ?? ''
}

export function scheduleSave(
  pane: string,
  chapterGuid: string,
  sceneId: string,
  html: string,
  plainText: string
): void {
  const existing = autosaveTimers.get(pane)
  if (existing) clearTimeout(existing)
  autosaveTimers.set(
    pane,
    setTimeout(() => {
      autosaveTimers.delete(pane)
      const write = saveScene(chapterGuid, sceneId, html, plainText)
      autosaveWrites.set(pane, write)
      void write.then(
        () => {
          if (autosaveWrites.get(pane) === write) autosaveWrites.delete(pane)
        },
        () => {
          if (autosaveWrites.get(pane) === write) autosaveWrites.delete(pane)
        }
      )
    }, AUTOSAVE_DELAY_MS)
  )
}

async function saveScene(
  chapterGuid: string,
  sceneId: string,
  html: string,
  plainText: string
): Promise<void> {
  const result = await rpc.request<{
    sceneId: string
    wordCount: number
    hash: string
    conflicted: boolean
    diskHtml: string | null
  }>('scenes/write', [
    chapterGuid,
    sceneId,
    html,
    plainText,
    useProjectStore.getState().sceneHashes[sceneId] ?? null
  ])

  // Refused: the file changed under us and nothing was written. The scene stays
  // dirty so the writer's text is still in the editor while they decide.
  if (result.conflicted) {
    useProjectStore.setState({
      sceneConflict: {
        chapterGuid,
        sceneId,
        mine: html,
        theirs: result.diskHtml ?? '',
        plainText
      }
    })
    return
  }

  useProjectStore.setState((state) => {
    // Only the exact content acknowledged by the backend is clean. The writer
    // may have typed again while this request was in flight; clearing that newer
    // edit here would make a shutdown flush believe there was nothing to save.
    const editors = mapEditors(state.editors, (editor) =>
      editor.sceneId === sceneId &&
      editor.isDirty &&
      editor.html === html &&
      (editor.plainText ?? '') === plainText
        ? { ...editor, isDirty: false }
        : editor
    )
    const stillDirty = Object.values(editors).some(
      (editor) => editor.sceneId === sceneId && editor.isDirty
    )
    return {
      sceneHashes: { ...state.sceneHashes, [sceneId]: result.hash },
      editors,
      ...mirror(editors, state.activeEditorPaneId),
      dirtyMap:
        state.dirtyMap[sceneId] !== stillDirty
          ? { ...state.dirtyMap, [sceneId]: stillDirty }
          : state.dirtyMap,
      chapters: state.chapters.map((c) =>
        c.guid === chapterGuid
          ? {
              ...c,
              scenes: c.scenes.map((s) =>
                s.id === sceneId ? { ...s, wordCount: result.wordCount } : s
              )
            }
          : c
      )
    }
  })
}

let manuscriptClaims: EditingSceneClaim[] = []

let reported = ''

export function reportManuscriptEditing(claims: EditingSceneClaim[]): void {
  manuscriptClaims = claims
  reportEditingScenes()
}

export function reportEditingScenes(force = false): void {
  const state = useProjectStore.getState()
  if (state.workspaceSuspended && !force) return
  const claims = new Map<string, EditingSceneClaim>()
  for (const editor of Object.values(state.editors)) {
    if (!editor.sceneId || !editor.chapterGuid) continue
    const prior = claims.get(editor.sceneId)
    claims.set(editor.sceneId, { chapterGuid: editor.chapterGuid, sceneId: editor.sceneId, dirty: editor.isDirty || !!prior?.dirty })
  }
  for (const claim of manuscriptClaims) {
    const prior = claims.get(claim.sceneId)
    claims.set(claim.sceneId, { ...claim, dirty: claim.dirty || !!prior?.dirty })
  }
  const scenes = [...claims.values()].sort((left, right) => left.sceneId.localeCompare(right.sceneId))
  const next = JSON.stringify(scenes)
  if (!force && next === reported) return
  reported = next
  const request = scenes.length > 1
    ? rpc.request('scenes/setEditingMany', [scenes])
    : rpc.request('scenes/setEditing', [scenes[0]?.chapterGuid ?? null, scenes[0]?.sceneId ?? null, scenes[0]?.dirty ?? false])
  void request.catch(() => { if (reported === next) reported = '' })
}
