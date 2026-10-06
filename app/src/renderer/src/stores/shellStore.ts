import { create } from 'zustand'
import { parseSettingsDestination, setSettingsDestination } from '../views/settings/settingsNavigation'
import { isSettingsSectionKey } from '../views/settings/settingsRegistry'
import { HOME_VIEW, MODE_VIEWS, modeOf, type Mode } from '../shell/modes'
import { type MainView, type InspectorTab, type ShellState } from './shellTypes'
import { newLeaf, findPane, paneLeaves, splitPane, closePane, setPaneViewIn, resize, reidentify, persistLayouts, storedLayouts, initialPanes } from './shellPanes'
import { BINDER_MIN, BINDER_MAX, INSPECTOR_MIN, INSPECTOR_MAX, shellCapacityForWidth, clamp, savePanelSize, initialPanelSize, storedPanels, screenW } from './shellPanels'

/** Commit the active drawer field before a shortcut removes its React tree. */
function blurFocusPanel(): void {
  const active = document.activeElement
  if (active instanceof HTMLElement && active.closest('.focus-panel')) active.blur()
}

/** The active pane's view and the navigation beside it must change together. */
function viewState(
  state: Pick<ShellState, 'mode'>,
  mainView: MainView
): Pick<ShellState, 'mainView' | 'mode'> {
  const mode = modeOf(mainView)
  if (mode) lastInMode[mode] = mainView
  return { mainView, mode: mode ?? state.mode }
}

/**
 * Put a view in front of the writer.
 *
 * The content area is a tree of panes and the main area renders that tree, so
 * setting `mainView` alone changes a label and nothing on screen. Every
 * navigation has to land in the active pane - setMainView did, and the four
 * that navigate somewhere specific (a map pin, a research item, Settings) did
 * not, so clicking one of those quietly did nothing after panes shipped.
 */
function showView(
  state: ShellState,
  mainView: MainView
): Pick<
  ShellState,
  'mainView' | 'mode' | 'extView' | 'panes' | 'binderOverlayOpen' | 'inspectorOverlayOpen'
> {
  return {
    // A view carries its mode with it, so a deep link, a hotkey or the palette
    // lands the writer in the workspace that view belongs to rather than
    // leaving the rail pointing somewhere they no longer are. Dashboard,
    // Settings and About belong to no mode and leave the last one standing.
    ...viewState(state, mainView),
    extView: null,
    panes: setPaneViewIn(state.panes, state.activePaneId, mainView),
    binderOverlayOpen: false,
    inspectorOverlayOpen: false
  }
}

/**
 * The view a mode opens on.
 *
 * Remembered per mode, so going to Plan and back to World returns to the Codex
 * entry you were reading rather than to the top of the mode.
 */
const lastInMode: Partial<Record<Mode, MainView>> = {}

export const useShellStore = create<ShellState>((set, get) => ({
  mainView: 'write',
  mode: 'write',
  panes: initialPanes,
  activePaneId: initialPanes.id,
  layouts: storedLayouts,
  mobileTab: 'dashboard',
  // Compact until the native side says otherwise, so the desktop build and any
  // pre-announcement frame render the narrow layout rather than flashing panes.
  mobileLayout: 'phone',
  sidebarCollapsed: false,
  extView: null,
  binderTab: 'chapters',
  binderVisible: true,
  binderWidth: initialPanelSize(storedPanels.binderWidth, 0.17, BINDER_MIN, BINDER_MAX, screenW),
  binderOverlayOpen: false,
  modePanelDocked: storedPanels.modePanelDocked !== false,
  modePanelOpen: false,
  inspectorVisible: true,
  inspectorWidth: initialPanelSize(
    storedPanels.inspectorWidth,
    0.2,
    INSPECTOR_MIN,
    INSPECTOR_MAX,
    screenW
  ),
  inspectorOverlayOpen: false,
  shellWidth: screenW,
  shellCapacity: shellCapacityForWidth(screenW),
  inspectorTab: 'context',
  pendingFootnoteText: null,
  pendingSuggestion: null,
  notesDockVisible: false,
  settingsSearch: '',
  pendingMapNav: null,
  pendingExportChapter: null,
  mobileExportSelection: null,
  backendVersion: null,
  focusMode: false,
  focusPaneId: null,
  focusPanel: null,
  focusPanelTransient: false,
  revealFocusPanel: (focusPanel) => {
    const s = get()
    if (!s.focusMode || s.mainView !== 'write' || s.extView || (s.focusPanel && !s.focusPanelTransient)) return
    if (s.focusPanel !== focusPanel) set({ focusPanel, focusPanelTransient: true })
  },
  focusToolsVisible: false,
  setFocusToolsVisible: (focusToolsVisible) => {
    if (get().focusToolsVisible !== focusToolsVisible) set({ focusToolsVisible })
  },
  closeFocusPanel: () => { blurFocusPanel(); set({ focusPanel: null, focusPanelTransient: false }) },
  returnToFocus: () => get().guardLeave(() => {
    const s = get()
    const pane = s.focusPaneId && findPane(s.panes, s.focusPaneId)
    if (pane) s.setActivePane(pane.id)
    set((state) => ({ ...showView(state, 'write'), focusPanel: null, focusPanelTransient: false, focusToolsVisible: false }))
  }),
  findReplaceOpen: false,
  cleanupOpen: false,
  commandPaletteOpen: false,
  quickOpenOpen: false,
  quickCaptureOpen: false,
  pendingResearchId: null,
  pendingLanguageQuery: null,
  helpOpen: false,
  layoutsOpen: false,
  tourOpen: false,
  dialog: null,
  suggestionMode: false,
  unsavedGuards: {},
  pendingLeave: null,
  openDialog: (dialog) => set({ dialog }),
  closeDialog: () => set({ dialog: null }),
  toggleSuggestionMode: () => set((s) => ({ suggestionMode: !s.suggestionMode })),
  setSuggestionMode: (suggestionMode) => set({ suggestionMode }),
  registerUnsavedGuard: (guard) =>
    set((s) => ({ unsavedGuards: { ...s.unsavedGuards, [guard.id]: guard } })),

  clearUnsavedGuard: (id) =>
    set((s) => {
      if (!(id in s.unsavedGuards)) return {}
      const rest = { ...s.unsavedGuards }
      delete rest[id]
      return { unsavedGuards: rest }
    }),

  guardLeave: (proceed) => {
    blurFocusPanel()
    const dirty = Object.values(get().unsavedGuards).filter((g) => g.isDirty())
    if (dirty.length === 0) {
      proceed()
      return
    }
    set({ pendingLeave: { label: dirty[0].label, proceed } })
  },

  resolveLeave: async (action) => {
    const pending = get().pendingLeave
    if (!pending) return
    if (action === 'cancel') {
      set({ pendingLeave: null })
      return
    }
    const dirty = Object.values(get().unsavedGuards).filter((g) => g.isDirty())
    // Everything the move would have cost, not only the screen the prompt
    // happened to name.
    if (action === 'save') for (const guard of dirty) await guard.save()
    // The guards go before the move does, so the navigation being released is
    // not stopped a second time by what it is leaving.
    set({ pendingLeave: null, unsavedGuards: {} })
    pending.proceed()
  },

  setMainView: (mainView) =>
    get().guardLeave(() =>
      set((s) => showView(s, mainView))
    ),

  setMode: (mode) =>
    get().guardLeave(() =>
      set((s) => {
        const views = MODE_VIEWS[mode]
        const landing = lastInMode[mode] ?? views[0]
        return { ...showView(s, landing), mode }
      })
    ),

  goHome: () => get().guardLeave(() => set((s) => showView(s, HOME_VIEW))),

  setModePanelOpen: (modePanelOpen) =>
    set(modePanelOpen ? { modePanelOpen, binderOverlayOpen: false } : { modePanelOpen }),

  toggleModePanelDocked: () =>
    set((s) => {
      const modePanelDocked = !s.modePanelDocked
      savePanelSize({ modePanelDocked })
      // Undocking while it is on screen should not leave the overlay behind it.
      return { modePanelDocked, modePanelOpen: false }
    }),

  setActivePane: (activePaneId) =>
    set((s) => {
      const pane = findPane(s.panes, activePaneId)
      return pane && pane.kind === 'leaf'
        ? { activePaneId, ...viewState(s, pane.view) }
        : {}
    }),

  setPaneView: (id, view) =>
    set((s) => {
      const pane = findPane(s.panes, id)
      if (!pane || pane.kind !== 'leaf' || pane.view === view) return {}
      return {
        panes: setPaneViewIn(s.panes, id, view),
        // The label the toolbar and the palette read follows the pane the writer
        // is in, so retargeting some other pane must not move it.
        ...(id === s.activePaneId ? { ...viewState(s, view), extView: null } : {})
      }
    }),

  splitActivePane: (direction) =>
    set((s) => {
      const { root, created } = splitPane(s.panes, s.activePaneId, direction)
      return created ? { panes: root, activePaneId: created } : {}
    }),

  splitPaneById: (id, direction) => {
    const state = get()
    const { root, created } = splitPane(state.panes, id, direction)
    const pane = created && findPane(root, created)
    if (pane && pane.kind === 'leaf') {
      set({ panes: root, activePaneId: pane.id, ...viewState(state, pane.view) })
    }
    return created
  },

  closeActivePane: () => get().closePaneById(get().activePaneId),

  closePaneById: (id) =>
    set((s) => {
      // The last pane stays: a content area with nothing in it is not a layout,
      // it is a broken window.
      if (paneLeaves(s.panes).length < 2) return {}
      const root = closePane(s.panes, id)
      if (!root) return {}
      // Closing the pane you were in moves you somewhere real; closing another
      // one leaves you where you were.
      if (id !== s.activePaneId && findPane(root, s.activePaneId)) return { panes: root }
      const first = paneLeaves(root)[0]
      return { panes: root, activePaneId: first.id, ...viewState(s, first.view) }
    }),

  setPaneSizes: (splitId, sizes) =>
    set((s) => ({ panes: resize(s.panes, splitId, sizes) })),

  resetPanes: () =>
    set((s) => {
      if (s.panes.kind === 'leaf') return {}
      // The view you are on comes with you. Which view a pane shows is where you
      // are in the book rather than how the window is arranged, and going back
      // to one pane should not also navigate you somewhere you did not ask for.
      const here = findPane(s.panes, s.activePaneId)
      const view = here && here.kind === 'leaf' ? here.view : s.mainView
      const root = newLeaf(view)
      return { panes: root, activePaneId: root.id, ...viewState(s, view) }
    }),

  saveLayout: (name) =>
    set((s) => {
      const layouts = [
        ...s.layouts.filter((l) => l.name !== name),
        { name, root: s.panes }
      ]
      persistLayouts(layouts)
      return { layouts }
    }),

  applyLayout: (name) =>
    set((s) => {
      const layout = s.layouts.find((l) => l.name === name)
      if (!layout) return {}
      const root = reidentify(layout.root)
      const first = paneLeaves(root)[0]
      return { panes: root, activePaneId: first.id, ...viewState(s, first.view) }
    }),

  deleteLayout: (name) =>
    set((s) => {
      const layouts = s.layouts.filter((l) => l.name !== name)
      persistLayouts(layouts)
      return { layouts }
    }),

  setMobileTab: (mobileTab) => set({ mobileTab }),
  openExport: (chapterGuid) =>
    get().guardLeave(() =>
      set((s) => ({
        ...showView(s, 'export'),
        mobileTab: 'export',
        pendingExportChapter: chapterGuid ?? null,
        mobileExportSelection: null
      }))
    ),
  setMobileLayout: (mobileLayout) => set({ mobileLayout }),
  setSidebarCollapsed: (sidebarCollapsed) => set({ sidebarCollapsed }),
  navigateToMapPin: (mapId, pinId) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'maps'), pendingMapNav: { mapId, pinId } }))
    ),
  clearPendingMapNav: () => set({ pendingMapNav: null }),
  openSettings: (search = '') =>
    get().guardLeave(() => {
      const current = get()
      const parsed = parseSettingsDestination(search)
      const directSection = isSettingsSectionKey(search) ? { section: search } : null
      setSettingsDestination({
        ...(parsed ?? directSection ?? (search ? { query: search } : { section: 'appearance' })),
        ...(current.mainView !== 'settings'
          ? { origin: { view: current.mainView, labelKey: `shell.view.${current.mainView}` } }
          : {})
      })
      set((s) => ({ ...showView(s, 'settings'), settingsSearch: '' }))
    }),
  setExtView: (extView) => set({ extView }),
  setBinderTab: (binderTab) => set({ binderTab }),
  toggleFocusMode: () => { blurFocusPanel(); set((s) => ({
    focusMode: !s.focusMode,
    focusPaneId: s.focusMode ? null : s.activePaneId,
    focusPanel: null,
    focusPanelTransient: false,
    focusToolsVisible: false
  })) },
  setFindReplaceOpen: (findReplaceOpen) => set({ findReplaceOpen }),
  setCleanupOpen: (cleanupOpen) => set({ cleanupOpen }),
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  setQuickOpenOpen: (quickOpenOpen) => set({ quickOpenOpen }),
  setQuickCaptureOpen: (quickCaptureOpen) => set({ quickCaptureOpen }),
  navigateToResearch: (itemId) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'research'), pendingResearchId: itemId }))
    ),
  clearPendingResearch: () => set({ pendingResearchId: null }),
  navigateToLanguage: (word) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'languages'), pendingLanguageQuery: word }))
    ),
  clearPendingLanguage: () => set({ pendingLanguageQuery: null }),
  setHelpOpen: (helpOpen) => set({ helpOpen }),
  setLayoutsOpen: (layoutsOpen) => set({ layoutsOpen }),
  setTourOpen: (tourOpen) => set({ tourOpen }),
  toggleBinder: () => {
    blurFocusPanel()
    set((s) =>
      s.focusMode && s.mainView === 'write'
        ? { focusPanel: s.focusPanel === 'binder' ? null : 'binder', focusPanelTransient: false }
        : s.shellCapacity === 'compact'
        ? // One drawer at a time. Two of them stack against the same edge, so
          // opening the second would put it over the first.
          { binderOverlayOpen: !s.binderOverlayOpen, inspectorOverlayOpen: false, modePanelOpen: false }
        : { binderVisible: !s.binderVisible }
    )
  },
  setBinderWidth: (px) => set({ binderWidth: clamp(px, BINDER_MIN, BINDER_MAX) }),
  toggleInspector: () => {
    blurFocusPanel()
    set((s) =>
      s.focusMode && s.mainView === 'write'
        ? { focusPanel: s.focusPanel === 'inspector' ? null : 'inspector', focusPanelTransient: false }
        : s.shellCapacity === 'wide'
        ? { inspectorVisible: !s.inspectorVisible }
        : { inspectorOverlayOpen: !s.inspectorOverlayOpen, binderOverlayOpen: false }
    )
  },
  setInspectorWidth: (px) => set({ inspectorWidth: clamp(px, INSPECTOR_MIN, INSPECTOR_MAX) }),
  setInspectorTab: (inspectorTab) => set({ inspectorTab }),
  requestFootnoteText: (footnoteId) =>
    set((s) => ({
      // Wide enough for a sidebar means showing it; anything narrower has the
      // same drawer the inspector button opens, and one drawer at a time.
      ...(s.focusMode && s.mainView === 'write'
        ? { focusPanel: 'inspector' as const, focusPanelTransient: false }
        : s.shellCapacity === 'wide'
        ? { inspectorVisible: true }
        : { inspectorOverlayOpen: true, binderOverlayOpen: false }),
      inspectorTab: 'footnotes' as InspectorTab,
      pendingFootnoteText: footnoteId
    })),
  clearPendingFootnoteText: () => set({ pendingFootnoteText: null }),
  revealSuggestion: (sceneId, changeId) => set({ pendingSuggestion: { sceneId, changeId } }),
  clearPendingSuggestion: () => set({ pendingSuggestion: null }),
  toggleNotesDock: () => {
    blurFocusPanel()
    set((s) => s.focusMode && s.mainView === 'write'
      ? { focusPanel: s.focusPanel === 'notes' ? null : 'notes', focusPanelTransient: false }
      : { notesDockVisible: !s.notesDockVisible })
  },
  setBackendVersion: (backendVersion) => set({ backendVersion }),
  setShellMetrics: (rawWidth) =>
    set((s) => {
      const shellWidth = Math.max(1, Math.round(rawWidth))
      const shellCapacity = shellCapacityForWidth(shellWidth)
      return {
        shellWidth,
        shellCapacity,
        // A docked panel that is still flagged open would reopen as an overlay
        // the moment the window narrowed again.
        ...(shellCapacity !== 'compact' ? { binderOverlayOpen: false, modePanelOpen: false } : {}),
        ...(shellCapacity === 'wide' ? { inspectorOverlayOpen: false } : {}),
        // Crossing between constrained modes should never leave two drawers
        // stacked over the manuscript.
        ...(shellCapacity !== s.shellCapacity
          ? shellCapacity === 'compact'
            ? { inspectorOverlayOpen: false }
            : { binderOverlayOpen: false }
          : {})
      }
    })
}))

export type { MainView, ShellDialog, BinderTab, ActiveExtView, InspectorTab, MobileTab, PaneNode, SavedLayout, ShellCapacity, MobileLayout, UnsavedGuard, PendingLeave } from './shellTypes'
export { newLeaf, sameArrangement, DEFAULT_LAYOUT, matchingLayout, findPane, paneLeaves, anyPaneShows, splitPane, closePane, setPaneViewIn } from './shellPanes'
export { BINDER_MIN, BINDER_MAX, INSPECTOR_MIN, INSPECTOR_MAX, NOTES_DOCK_MIN, NOTES_DOCK_MAX, shellCapacityForWidth, panelWidthForShell, savePanelSize, NOTES_DOCK_DEFAULT } from './shellPanels'
