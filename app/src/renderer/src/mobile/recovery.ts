import i18n from '../i18n'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { pendingManuscriptRecovery, hasPendingManuscriptWrites } from '../stores/manuscriptStore'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { readRecoveryJournal, recordRecovery, removeRecovery, remapRecoveryEntry, sameRecoveryScope, sameResearchValue, type RecoveryEntry, type RecoveryScope, type ResearchRecoveryValue, type SceneRecovery } from './recoveryJournal'

let restoring = false
let startupJournal: RecoveryEntry[] | null = null
let activeSceneRecovery: RecoveryEntry | null = null
const researchCaptures = new Set<() => void>()

export function mobileRecoveryScope(): RecoveryScope | null {
  if (!window.novalist.isMobile) return null
  const state = useProjectStore.getState()
  const draftId = state.activeDraftId ?? state.drafts.find((draft) => draft.isActive)?.id
  return state.projectPath && state.activeBookId && draftId
    ? { projectPath: state.projectPath, bookId: state.activeBookId, draftId } : null
}

export function registerResearchRecovery(capture: () => void): () => void {
  researchCaptures.add(capture)
  return () => { researchCaptures.delete(capture) }
}

export function retainMobileSceneRecovery(scene: Omit<SceneRecovery, 'kind'>): void {
  if (!window.novalist.isMobile) return
  try {
    const scope = mobileRecoveryScope()
    if (!scope) throw new Error(i18n.t('update.workspaceBusy'))
    recordRecovery({ ...scene, kind: 'scene', scope })
  } catch (error) { reportRecoveryError(error) }
}

export function captureMobileRecovery(): RecoveryEntry[] {
  if (!window.novalist.isMobile) return []
  const scope = mobileRecoveryScope()
  const state = useProjectStore.getState()
  if (!scope) {
    if (state.isDirty || Object.values(state.dirtyMap).some(Boolean)) throw new Error(i18n.t('update.workspaceBusy'))
    return []
  }
  for (const [pane, editor] of Object.entries(state.editors)) {
    if (!editor.isDirty || !editor.chapterGuid || !editor.sceneId || editor.html === null) continue
    recordRecovery({ scope, kind: 'scene', source: `pane:${pane}`, chapterGuid: editor.chapterGuid, sceneId: editor.sceneId,
      html: editor.html, plainText: editor.plainText ?? '', hash: editor.hash ?? '' })
  }
  for (const scene of pendingManuscriptRecovery()) recordRecovery({ scope, kind: 'scene', source: `manuscript:${scene.sceneId}`, ...scene })
  for (const capture of researchCaptures) capture()
  return readRecoveryJournal().filter((entry) => sameRecoveryScope(entry.scope, scope))
}

export function acknowledgeMobileSceneRecovery(sceneId: string, html: string, originalMine?: string, acknowledgedSource?: string): void {
  const scope = mobileRecoveryScope()
  if (!scope) return
  try {
    for (const entry of readRecoveryJournal()) {
      if (entry.kind !== 'scene' || entry.sceneId !== sceneId || !sameRecoveryScope(entry.scope, scope)) continue
      if (entry.html === html || entry.html === originalMine || entry.source === acknowledgedSource || (activeSceneRecovery && JSON.stringify(entry) === JSON.stringify(activeSceneRecovery))) removeRecovery(entry)
    }
    if (activeSceneRecovery?.kind === 'scene' && activeSceneRecovery.sceneId === sceneId && sameRecoveryScope(activeSceneRecovery.scope, scope)) activeSceneRecovery = null
  } catch (error) { reportRecoveryError(error) }
}

export function reportRecoveryError(error: unknown): void {
  useHostBridgeStore.getState().pushToast(i18n.t('toast.saveFailed').replace('{0}', String(error)))
}

function scopeStillCurrent(scope: RecoveryScope): boolean {
  const current = mobileRecoveryScope()
  return !!current && sameRecoveryScope(current, scope) && !useProjectStore.getState().workspaceBusy
}

async function recoverResearch(entry: Extract<RecoveryEntry, { kind: 'research' }>): Promise<void> {
  const items = await rpc.request<ResearchRecoveryValue[]>('research/list')
  if (!scopeStillCurrent(entry.scope)) return
  const current = items.find((item) => item.id === entry.draft.id)
  if (current && sameResearchValue(current, entry.draft)) { removeRecovery(entry); return }
  const unchanged = current && sameResearchValue(current, entry.base)
  const title = unchanged ? entry.draft.title : `${entry.draft.title} (${i18n.t('mobileRecovery.recovered')})`
  const duplicate = !unchanged && items.some((item) => sameResearchValue(item, { ...entry.draft, id: item.id, title }))
  if (!duplicate) await rpc.request('research/save', [unchanged ? entry.draft.id : null, title, entry.draft.type, entry.draft.content, entry.draft.tags, entry.draft.entityRefs])
  removeRecovery(entry)
}

async function recoverScene(entry: Extract<RecoveryEntry, { kind: 'scene' }>): Promise<boolean> {
  const state = useProjectStore.getState()
  if (!state.chapters.some((chapter) => chapter.guid === entry.chapterGuid && chapter.scenes.some((scene) => scene.id === entry.sceneId))) return true
  const disk = await rpc.request<{ html: string; hash: string }>('scenes/read', [entry.chapterGuid, entry.sceneId])
  if (!scopeStillCurrent(entry.scope)) return false
  if (disk.html === entry.html) { removeRecovery(entry); return true }
  if (Object.values(useProjectStore.getState().editors).some((editor) => editor.isDirty)) return false
  await useProjectStore.getState().openScene(entry.chapterGuid, entry.sceneId)
  const current = useProjectStore.getState()
  const pane = current.activeEditorPaneId
  if (!scopeStillCurrent(entry.scope) || !pane || current.editors[pane]?.sceneId !== entry.sceneId || current.editors[pane].isDirty) return false
  activeSceneRecovery = entry
  useProjectStore.setState({ editors: { ...current.editors, [pane]: { ...current.editors[pane], hash: entry.hash || 'unknown-recovery-base' } } })
  useProjectStore.getState().onEditorContentChanged(pane, entry.html, entry.plainText)
  await useProjectStore.getState().flushPane(pane)
  return !useProjectStore.getState().sceneConflict
}

async function remapStartupJournal(scope: RecoveryScope): Promise<void> {
  const resolve = window.novalist.resolveStoredProjectPath
  if (!resolve || !startupJournal) return
  for (let index = 0; index < startupJournal.length; index++) {
    const entry = startupJournal[index]
    if (sameRecoveryScope(entry.scope, scope) || entry.scope.bookId !== scope.bookId || entry.scope.draftId !== scope.draftId) continue
    const resolved = await resolve(entry.scope.projectPath)
    if (!scopeStillCurrent(scope)) return
    if (resolved !== scope.projectPath) continue
    const updated = remapRecoveryEntry(entry, scope)
    if (updated) startupJournal[index] = updated
  }
}

export async function restoreMobileRecovery(): Promise<void> {
  if (!window.novalist.isMobile) return
  try { startupJournal ??= readRecoveryJournal() } catch (error) { reportRecoveryError(error); return }
  const scope = mobileRecoveryScope()
  const state = useProjectStore.getState()
  if (!scope || restoring || state.workspaceBusy || state.sceneConflict || hasPendingManuscriptWrites() || Object.values(state.editors).some((editor) => editor.isDirty)) return
  restoring = true
  try {
    await remapStartupJournal(scope)
    if (!scopeStillCurrent(scope)) return
    const present = new Set(readRecoveryJournal().map((entry) => JSON.stringify(entry)))
    for (const entry of startupJournal.filter((item) => sameRecoveryScope(item.scope, scope) && present.has(JSON.stringify(item)))) {
      if (!scopeStillCurrent(scope)) return
      if (entry.kind === 'research') await recoverResearch(entry)
      else if (!await recoverScene(entry)) return
    }
  } catch (error) { reportRecoveryError(error) }
  finally { restoring = false }
}

export function clearAcknowledgedRecovery(entries: RecoveryEntry[]): void {
  for (const entry of entries) removeRecovery(entry)
}
