import { create } from 'zustand'
import { useProjectStore } from './projectStore'

/**
 * The set of scenes the writer has selected, shared by every surface that lists
 * scenes: the binder, the corkboard, the outliner and the calendar. One store
 * rather than one per view, so a selection made in the binder is still there
 * when they switch to the corkboard to act on it.
 *
 * Selections are small (tens of scenes, not thousands), so a plain array keeps
 * the store shallowly comparable and avoids the re-render traps a Set brings.
 */
interface SelectionState {
  sceneIds: string[]
  /** Where a shift-click range starts. Set by every plain or toggling click. */
  anchorId: string | null

  clear: () => void
  selectOnly: (sceneId: string) => void
  toggle: (sceneId: string) => void
  extendTo: (sceneId: string, ordered: string[]) => void
  selectMany: (sceneIds: string[]) => void
  /** Drops ids that no longer exist, after a delete or archive. */
  prune: (existing: string[]) => void
}

export const useSelectionStore = create<SelectionState>((set, get) => ({
  sceneIds: [],
  anchorId: null,

  clear: () => set({ sceneIds: [], anchorId: null }),

  selectOnly: (sceneId) => set({ sceneIds: [sceneId], anchorId: sceneId }),

  toggle: (sceneId) => {
    const current = get().sceneIds
    const next = current.includes(sceneId)
      ? current.filter((id) => id !== sceneId)
      : [...current, sceneId]
    // The anchor follows the last scene touched, so a shift-click after a
    // ctrl-click ranges from where the writer just clicked.
    set({ sceneIds: next, anchorId: sceneId })
  },

  extendTo: (sceneId, ordered) => {
    const anchor = get().anchorId
    if (anchor === null) {
      set({ sceneIds: [sceneId], anchorId: sceneId })
      return
    }
    const from = ordered.indexOf(anchor)
    const to = ordered.indexOf(sceneId)
    // Either end missing means the list changed under the selection; fall back
    // to a plain select rather than guessing a range.
    if (from === -1 || to === -1) {
      set({ sceneIds: [sceneId], anchorId: sceneId })
      return
    }
    const [lo, hi] = from <= to ? [from, to] : [to, from]
    // The anchor stays put so dragging the shift-click back and forth grows and
    // shrinks the same range instead of walking it down the list.
    set({ sceneIds: ordered.slice(lo, hi + 1) })
  },

  selectMany: (sceneIds) =>
    set({ sceneIds, anchorId: sceneIds.length > 0 ? sceneIds[sceneIds.length - 1] : null }),

  prune: (existing) => {
    const alive = new Set(existing)
    const next = get().sceneIds.filter((id) => alive.has(id))
    if (next.length === get().sceneIds.length) return
    set({ sceneIds: next, anchorId: next.includes(get().anchorId ?? '') ? get().anchorId : null })
  }
}))

/**
 * Every scene id in the open book, in the order the binder shows them. This is
 * what a shift-click range is measured against.
 */
export function sceneOrder(): string[] {
  return useProjectStore
    .getState()
    .chapters.flatMap((chapter) => chapter.scenes.map((scene) => scene.id))
}

/**
 * Turns a click on a scene row into the right selection gesture.
 *
 * Returns true when the click was about selecting, so the caller knows not to
 * also open the scene. A plain click is not a selection gesture: it opens the
 * scene and clears whatever was selected, which is what the binder always did.
 */
export function handleSceneClick(
  sceneId: string,
  event: { ctrlKey: boolean; metaKey: boolean; shiftKey: boolean }
): boolean {
  const store = useSelectionStore.getState()
  if (event.shiftKey) {
    store.extendTo(sceneId, sceneOrder())
    return true
  }
  if (event.ctrlKey || event.metaKey) {
    store.toggle(sceneId)
    return true
  }
  store.clear()
  return false
}
