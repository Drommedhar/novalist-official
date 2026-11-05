import { useEffect } from 'react'
import { flushSync } from 'react-dom'
import { create } from 'zustand'
import type { SceneNotesWindowState } from '../../../shared/sceneNotesProtocol'
import { useShellStore, paneLeaves, type DockEdge } from '../stores/shellStore'
import { dockNotesPane } from '../stores/shellPanes'
import { flushPendingWrites } from '../stores/pendingWrites'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import i18n from '../i18n'
import { flushWindowWrites } from './useWorkspaceWindow'
import { readNotesPlacement, saveNotesPlacement } from './sceneNotesPlacement'

let placementRestored = false

export const useNotesDocking = create<{
  state: SceneNotesWindowState
  placing: boolean
  transferring: boolean
  released: boolean
}>(() => ({ state: { context: null, visible: false, pinned: true, docked: false, follow: true }, placing: false, transferring: false, released: false }))

const report = (): void => useHostBridgeStore.getState().pushToast(i18n.t('sceneChecklist.saveFailed'))
const hasNotes = (): boolean => paneLeaves(useShellStore.getState().panes).some((pane) => pane.view === 'sceneNotes')

async function restoreNotesLayout(): Promise<void> {
  if (useNotesDocking.getState().transferring) return
  useNotesDocking.setState({ transferring: true })
  try {
    await flushWindowWrites()
    if (!hasNotes()) return
    await window.novalist.dockSceneNotes?.()
    // A newer layout can remove the slot while the floating editor is saving.
    // Finish the handoff once, then obey the latest arrangement.
    if (!hasNotes()) await window.novalist.hideSceneNotes?.()
  } catch { report() }
  finally { useNotesDocking.setState({ transferring: false }) }
}

export function placeSceneNotes(): void {
  useShellStore.setState({ focusMode: false })
  useNotesDocking.setState({ placing: true })
}

export async function dockSceneNotesAt(targetId: string, edge: DockEdge): Promise<void> {
  if (useNotesDocking.getState().transferring) return
  useNotesDocking.setState({ transferring: true })
  try {
    await flushWindowWrites()
    await window.novalist.dockSceneNotes?.()
    const shell = useShellStore.getState()
    const content = paneLeaves(shell.panes).filter((pane) => pane.view !== 'sceneNotes')
    // Saved layouts reidentify every leaf. Resolve the destination again after
    // the asynchronous handoff so notes never disappear into an obsolete id.
    const target = content.find((pane) => pane.id === targetId) ?? content.find((pane) => pane.id === shell.activePaneId) ?? content[0]
    if (!target) { await window.novalist.hideSceneNotes?.(); return }
    useShellStore.setState({ panes: dockNotesPane(shell.panes, target.id, edge), notesDockVisible: true })
    useNotesDocking.setState({ placing: false })
  } catch { report() }
  finally { useNotesDocking.setState({ transferring: false }) }
}

export async function floatSceneNotes(): Promise<void> {
  if (useNotesDocking.getState().transferring) return
  useNotesDocking.setState({ transferring: true })
  try {
    await flushPendingWrites()
    // Remove the docked document before another renderer loads its saved text.
    flushSync(() => {
      useNotesDocking.setState({ released: true })
      useShellStore.setState({ notesDockVisible: false })
    })
    await flushPendingWrites()
    await window.novalist.floatSceneNotes?.()
  } catch { useShellStore.setState({ notesDockVisible: true }); report() }
  finally { useNotesDocking.setState({ transferring: false, released: false }) }
}

export async function hideDockedNotes(): Promise<void> {
  try { await flushPendingWrites(); await window.novalist.hideSceneNotes?.() }
  catch { report() }
}

/** Only the main shell owns docked notes. Floating/secondary windows publish context. */
export function useSceneNotesDocking(): void {
  useEffect(() => {
    if (window.novalist.isMobile) return
    if (!placementRestored) {
      placementRestored = true
      const saved = readNotesPlacement()
      if (saved) {
        useShellStore.setState(saved)
        useShellStore.getState().setActivePane(saved.activePaneId)
      }
    }
    let active = true
    let received = false
    const update = (state: SceneNotesWindowState): void => {
      if (!active) return
      useNotesDocking.setState({ state })
      useShellStore.setState({ notesDockVisible: state.visible })
      if (state.docked && state.visible && !hasNotes() && !useNotesDocking.getState().transferring) placeSceneNotes()
    }
    const stopState = window.novalist.onSceneNotesState?.((state) => { received = true; update(state) })
    void window.novalist.sceneNotesWindowState?.().then((state) => { if (!received) update(state) })
    const stopRequest = window.novalist.onSceneNotesDockRequest?.(placeSceneNotes)
    const stopLayout = useShellStore.subscribe((next, previous) => {
      // Dock insertion happens during a transfer; resizing and view changes
      // happen afterward. Persist both, without requiring a named layout.
      if (next.panes !== previous.panes || next.activePaneId !== previous.activePaneId) saveNotesPlacement(next.panes, next.activePaneId)
      if (next.panes === previous.panes || useNotesDocking.getState().transferring) return
      const before = paneLeaves(previous.panes).find((pane) => pane.view === 'sceneNotes')
      const after = paneLeaves(next.panes).find((pane) => pane.view === 'sceneNotes')
      if (after && before?.id !== after.id) void restoreNotesLayout()
      if (before && !after && useNotesDocking.getState().state.docked) void hideDockedNotes()
    })
    return () => { active = false; stopState?.(); stopRequest?.(); stopLayout() }
  }, [])
}
