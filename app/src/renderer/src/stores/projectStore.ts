import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { paneLeaves, useShellStore } from './shellStore'
import { useSettingsStore } from './settingsStore'
import { useCodexStore } from './codexStore'

export interface SceneDto {
  id: string
  title: string
  order: number
  wordCount: number
  labelColor: string | null
  isFavorite: boolean
  synopsis: string | null
  /** Key of the scene's stage; null when the writer has not set one. */
  stage: string | null
  /** True while the writer is holding this scene back from exports. */
  excludeFromExport: boolean
  /** True when the scene is out of the book but still in the plan: it stays
   *  here and in every planning view, and leaves word totals and exports. */
  inactive: boolean
  /** Colours of the threads this scene serves, in the book's plotline order. */
  plotlineColors: string[]
  /** The same threads by id, so the binder can narrow to one of them. */
  plotlineIds: string[]
}

export interface ChapterDto {
  /** A second line under the chapter title in the finished book. */
  subtitle: string | null
  /** True when the chapter opens straight into its prose. */
  hideHeading: boolean
  /** What the chapter is - a chapter, a prologue, a part. Empty is a chapter. */
  sectionTypeKey: string
  /** What the chapter is for, in your own words. Never printed. */
  description: string | null
  guid: string
  title: string
  order: number
  status: string
  act: string
  isFavorite: boolean
  scenes: SceneDto[]
}

export interface ProjectStateDto {
  isLoaded: boolean
  projectName: string | null
  projectPath: string | null
  activeBookId: string | null
  books: { id: string; name: string }[]
  chapters: ChapterDto[]
}

export interface RecentProjectDto {
  name: string
  path: string
  /** Portrait book cover as a base64 data: URI, or null when none is set. */
  cover?: string | null
}

/** One open scene in an editor pane's tab strip. Title is resolved from
 * `chapters` at render time so renames stay live. */
export interface SceneTabRef {
  chapterGuid: string
  sceneId: string
}

/**
 * One editor pane's own scene.
 *
 * The editor used to be a fixed pair of slots - a primary one and a "split" one
 * - which meant splitting the content area twice gave you two panes showing the
 * same scene, because the scene lived in the store rather than in the pane.
 * Keyed by the shell's pane id instead, so every pane holding the editor has its
 * own scene, its own tabs and its own unsaved state, and splitting a third time
 * costs nothing.
 */
export interface EditorPaneState {
  chapterGuid: string | null
  sceneId: string | null
  html: string | null
  plainText: string | null
  tabs: SceneTabRef[]
  isDirty: boolean
}

const EMPTY_EDITOR: EditorPaneState = {
  chapterGuid: null,
  sceneId: null,
  html: null,
  plainText: null,
  tabs: [],
  isDirty: false
}

/** An editor pane's state, or the empty one for a pane nothing is open in. */
export function editorPane(state: ProjectState, paneId: string | null): EditorPaneState {
  return (paneId && state.editors[paneId]) || EMPTY_EDITOR
}

// Matches the Avalonia EditorViewModel.AutoSaveDelayMs default.
const AUTOSAVE_DELAY_MS = 2000

const autosaveTimers = new Map<string, ReturnType<typeof setTimeout>>()

interface ProjectState {
  isLoaded: boolean
  projectName: string | null
  projectPath: string | null
  activeBookId: string | null
  books: { id: string; name: string }[]
  drafts: { id: string; name: string; isActive: boolean }[]
  chapters: ChapterDto[]
  recentProjects: RecentProjectDto[]
  openChapterGuid: string | null
  openSceneId: string | null
  /**
   * Points the shell at a scene without loading it into an editor pane.
   *
   * The Manuscript view is its own editor over every scene at once, so the
   * context sidebar has to follow the caret inside it. Going through
   * openScene would pull the scene into the editor pane and take the focus
   * away from the paragraph being typed in.
   */
  setContextScene(chapterGuid: string, sceneId: string): void
  openSceneHtml: string | null
  /** Fingerprint of what was read from disk, per scene id. A save carries it so
   *  the backend can refuse to overwrite an edit that arrived meanwhile. */
  sceneHashes: Record<string, string>
  /** A save the backend refused because the file changed underneath. Drives the
   *  merge dialog; null when there is nothing to resolve. */
  sceneConflict: {
    chapterGuid: string
    sceneId: string
    mine: string
    theirs: string
    plainText: string
  } | null
  openScenePlainText: string | null
  openTabs: SceneTabRef[]
  /** Every editor pane's own scene, keyed by the shell's pane id. The five
   *  fields above mirror whichever of these the writer is working in, so the
   *  inspector, the status bar and the dialogs keep following one scene. */
  editors: Record<string, EditorPaneState>
  /** The editor pane the rest of the shell follows. Null when none is open. */
  activeEditorPaneId: string | null
  /** Per-scene unsaved-edit flags, keyed by sceneId (drives the tab dirty dot). */
  dirtyMap: Record<string, boolean>
  isDirty: boolean
  applyState(state: ProjectStateDto): void
  loadRecents(): Promise<void>
  openProject(path: string): Promise<void>
  pickAndOpenProject(): Promise<void>
  /** Lets go of the open project, back to the screen the app starts on. */
  closeProject(): Promise<void>
  openScene(chapterGuid: string, sceneId: string): Promise<void>
  /** Opens a scene in one named pane, turning that pane into an editor. */
  openSceneIn(paneId: string, chapterGuid: string, sceneId: string): Promise<void>
  /** Splits the content area and opens the scene in the pane that appears. */
  openSceneInSplit(chapterGuid: string, sceneId: string): Promise<void>
  closeTab(paneId: string, sceneId: string): Promise<void>
  /** Moves a tab to the next editor pane, wrapping round. */
  moveTabToOtherPane(paneId: string, sceneId: string): Promise<void>
  onEditorContentChanged(paneId: string, html: string, plainText: string): void
  /** Drops editor state for panes that closed or stopped showing the editor,
   *  and keeps the mirrored fields pointed at the pane the writer is in. */
  syncEditorPanes(): void
  /** Writes one pane's unsaved edit now, cancelling its autosave timer. */
  flushPane(paneId: string): Promise<void>
  flushPendingSave(): Promise<void>
  /** @param insertAtOrder where the chapter goes, one-based; omit to append. */
  createChapter(title: string, insertAtOrder?: number): Promise<void>
  createScene(chapterGuid: string, title: string): Promise<void>
  switchBook(bookId: string): Promise<void>
  createBook(name: string): Promise<void>
  loadDrafts(): Promise<void>
  createDraft(name: string): Promise<void>
  switchDraft(draftId: string): Promise<void>
  deleteDraft(draftId: string): Promise<void>
  renameChapter(chapterGuid: string, title: string): Promise<void>
  renameScene(chapterGuid: string, sceneId: string, title: string): Promise<void>
  deleteChapter(chapterGuid: string): Promise<void>
  deleteScene(chapterGuid: string, sceneId: string): Promise<void>
  setChapterStatus(chapterGuid: string, status: string): Promise<void>
  setChapterAct(chapterGuid: string, act: string): Promise<void>
  reorderChapter(chapterGuid: string, newOrder: number): Promise<void>
  reorderScene(chapterGuid: string, sceneId: string, newOrder: number): Promise<void>
  moveScenes(sceneIds: string[], targetChapterGuid: string, targetIndex: number): Promise<void>
  /** Writes the writer's chosen text and clears the conflict. */
  resolveSceneConflict(html: string): Promise<void>
  /** Leaves the file alone and keeps the writer's text in the editor, still
   *  unsaved, so dismissing the dialog never decides anything for them. */
  dismissSceneConflict(): void
}

export const useProjectStore = create<ProjectState>((set, get) => ({
  isLoaded: false,
  projectName: null,
  projectPath: null,
  activeBookId: null,
  books: [],
  drafts: [],
  chapters: [],
  recentProjects: [],
  openChapterGuid: null,
  openSceneId: null,

  setContextScene: (chapterGuid, sceneId) => {
    const state = get()
    if (state.openChapterGuid === chapterGuid && state.openSceneId === sceneId) return
    set({ openChapterGuid: chapterGuid, openSceneId: sceneId })
  },
  openSceneHtml: null,
  sceneHashes: {},
  sceneConflict: null,
  openScenePlainText: null,
  openTabs: [],
  editors: {},
  activeEditorPaneId: null,
  dirtyMap: {},
  isDirty: false,

  applyState: (state) => {
    const prevPath = get().projectPath
    const prevBookId = get().activeBookId
    set({
      isLoaded: state.isLoaded,
      projectName: state.projectName,
      projectPath: state.projectPath,
      activeBookId: state.activeBookId,
      books: state.books,
      chapters: state.chapters
    })
    window.novalist.setProjectRoot(state.projectPath)
    if (state.isLoaded) void get().loadDrafts()
    // The effective language/theme can carry a per-project override, so re-apply
    // settings whenever the active project changes - otherwise a project opened
    // with a non-default language stays on the global language until Settings is
    // opened (which reloads settings as a side effect).
    if (state.projectPath !== prevPath) void useSettingsStore.getState().load()
    // The Codex is the active book's, and its entry count is shown outside the
    // Codex view, so it cannot wait for that view to be mounted again. Anything
    // selected belonged to the book being left, so the selection goes with it.
    if (state.activeBookId !== prevBookId) {
      useCodexStore.setState({ selectedId: null, selectedRecord: null })
      if (state.isLoaded) void useCodexStore.getState().refresh()
    }
  },

  switchBook: async (bookId) => {
    await get().flushPendingSave()
    set({ ...clearedEditorState() })
    get().applyState(await rpc.request<ProjectStateDto>('project/switchBook', [bookId]))
  },

  createBook: async (name) => {
    get().applyState(await rpc.request<ProjectStateDto>('project/createBook', [name]))
  },

  loadDrafts: async () => {
    set({ drafts: await rpc.request<{ id: string; name: string; isActive: boolean }[]>('project/drafts') })
  },

  createDraft: async (name) => {
    const active = get().drafts.find((d) => d.isActive)
    set({ drafts: await rpc.request<{ id: string; name: string; isActive: boolean }[]>('project/createDraft', [name, active?.id ?? null]) })
  },

  switchDraft: async (draftId) => {
    await get().flushPendingSave()
    set({ ...clearedEditorState() })
    get().applyState(await rpc.request<ProjectStateDto>('project/switchDraft', [draftId]))
  },

  deleteDraft: async (draftId) => {
    const wasActive = get().drafts.find((d) => d.id === draftId)?.isActive ?? false
    const drafts = await rpc.request<{ id: string; name: string; isActive: boolean }[]>(
      'project/deleteDraft',
      [draftId]
    )
    set({ drafts })
    // Deleting the active draft makes the backend switch to another draft and
    // reload its chapters/scenes, so refresh project state and reset the editor.
    if (wasActive) {
      await get().flushPendingSave()
      set({ ...clearedEditorState() })
      get().applyState(await rpc.request<ProjectStateDto>('project/getState'))
    }
  },

  loadRecents: async () => {
    const recents = await rpc.request<RecentProjectDto[]>('project/recent')
    set({ recentProjects: recents })
  },

  openProject: async (path) => {
    // On the sandboxed Mac App Store build, a project reopened from a stored path
    // (e.g. a recent-project card) needs its security-scoped bookmark resolved
    // before the backend can touch the files. beginProjectAccess returns true
    // immediately on every non-MAS build, so this is a no-op there. If it fails
    // (no usable bookmark), re-prompt for the folder to regrant access.
    let target = path
    if (!(await window.novalist.beginProjectAccess(target))) {
      const repicked = await window.novalist.pickFolder('Novalist')
      if (!repicked) return
      target = repicked
    }
    const state = await rpc.request<ProjectStateDto>('project/open', [target])
    get().applyState(state)
  },

  closeProject: async () => {
    // There was no way back to no project short of restarting. That was
    // survivable while the welcome screen was somewhere else; now that it is
    // what this window holds until a project is open, there was somewhere to go
    // back to and no way to get there.
    const state = await rpc.request<ProjectStateDto>('project/close')
    get().applyState(state)
  },

  pickAndOpenProject: async () => {
    const path = await window.novalist.pickFolder('Novalist')
    if (path) await get().openProject(path)
  },

  openScene: async (chapterGuid, sceneId) => {
    await get().openSceneIn(targetEditorPane(), chapterGuid, sceneId)
  },

  openSceneIn: async (paneId, chapterGuid, sceneId) => {
    const shell = useShellStore.getState()
    // The pane holds the editor from here on, and it is the one the writer is
    // now in: opening a scene somewhere the caret is not is how the old split
    // pane lost people.
    shell.setPaneView(paneId, 'write')
    shell.setActivePane(paneId)
    await get().flushPane(paneId)
    const content = await rpc.request<{ sceneId: string; html: string; hash: string }>(
      'scenes/read',
      [chapterGuid, sceneId]
    )
    set((s) => {
      const previous = editorPane(s, paneId)
      const editors = {
        ...s.editors,
        [paneId]: {
          chapterGuid,
          sceneId,
          html: content.html,
          plainText: stripHtml(content.html),
          tabs: previous.tabs.some((t) => t.sceneId === sceneId)
            ? previous.tabs
            : [...previous.tabs, { chapterGuid, sceneId }],
          isDirty: false
        }
      }
      return {
        editors,
        activeEditorPaneId: paneId,
        sceneHashes: { ...s.sceneHashes, [sceneId]: content.hash },
        ...mirror(editors, paneId)
      }
    })
  },

  openSceneInSplit: async (chapterGuid, sceneId) => {
    // "Open in split" now means a real second pane rather than the editor's own
    // two-slot arrangement, so the scene can sit beside the Codex or a third
    // scene just as easily as beside another editor.
    const shell = useShellStore.getState()
    const target = shell.splitPaneById(targetEditorPane(), 'row')
    if (target) await get().openSceneIn(target, chapterGuid, sceneId)
  },

  resolveSceneConflict: async (html) => {
    const conflict = get().sceneConflict
    if (!conflict) return
    const result = await rpc.request<{ wordCount: number; hash: string }>(
      'scenes/resolveConflict',
      [conflict.chapterGuid, conflict.sceneId, html, stripHtml(html)]
    )
    set((state) => {
      // The resolved text belongs to every pane holding that scene, not just the
      // one the writer resolved it from.
      const editors = mapEditors(state.editors, (editor) =>
        editor.sceneId === conflict.sceneId ? { ...editor, html, isDirty: false } : editor
      )
      return {
        sceneConflict: null,
        sceneHashes: { ...state.sceneHashes, [conflict.sceneId]: result.hash },
        editors,
        dirtyMap: { ...state.dirtyMap, [conflict.sceneId]: false },
        ...mirror(editors, state.activeEditorPaneId),
        chapters: state.chapters.map((c) =>
          c.guid === conflict.chapterGuid
            ? {
                ...c,
                scenes: c.scenes.map((sc) =>
                  sc.id === conflict.sceneId ? { ...sc, wordCount: result.wordCount } : sc
                )
              }
            : c
        )
      }
    })
  },

  dismissSceneConflict: () => set({ sceneConflict: null }),

  closeTab: async (paneId, sceneId) => {
    const editor = editorPane(get(), paneId)
    const idx = editor.tabs.findIndex((t) => t.sceneId === sceneId)
    if (idx < 0) return
    const isActive = editor.sceneId === sceneId
    if (isActive) await get().flushPane(paneId)
    const remaining = editorPane(get(), paneId).tabs.filter((t) => t.sceneId !== sceneId)

    if (!isActive) {
      set((s) => patchEditor(s, paneId, { tabs: remaining }))
      return
    }
    if (remaining.length === 0) {
      set((s) => patchEditor(s, paneId, { ...EMPTY_EDITOR }))
      // Nothing left in this pane. With another editor still open the writer is
      // mid-work elsewhere, so only a shell with no scene open anywhere falls
      // back to the dashboard.
      if (!Object.values(get().editors).some((e) => e.sceneId)) {
        useShellStore.getState().setMainView('dashboard')
      }
      return
    }
    const next = remaining[Math.min(idx, remaining.length - 1)]
    set((s) => patchEditor(s, paneId, { tabs: remaining }))
    await get().openSceneIn(paneId, next.chapterGuid, next.sceneId)
  },

  moveTabToOtherPane: async (paneId, sceneId) => {
    const tab = editorPane(get(), paneId).tabs.find((t) => t.sceneId === sceneId)
    if (!tab) return
    const others = writePaneIds().filter((id) => id !== paneId)
    await get().closeTab(paneId, sceneId)
    // With no second pane to move to, "the other pane" is one that has to exist
    // first - which is what a writer asking for this means anyway.
    if (others.length === 0) await get().openSceneInSplit(tab.chapterGuid, tab.sceneId)
    else await get().openSceneIn(others[0], tab.chapterGuid, tab.sceneId)
  },

  onEditorContentChanged: (paneId, html, plainText) => {
    const editor = editorPane(get(), paneId)
    const { chapterGuid, sceneId } = editor
    if (!chapterGuid || !sceneId) return
    set((s) => ({
      ...patchEditor(s, paneId, { html, plainText, isDirty: true }),
      dirtyMap: { ...s.dirtyMap, [sceneId]: true }
    }))
    scheduleSave(paneId, chapterGuid, sceneId, html, plainText)
  },

  syncEditorPanes: () => {
    const shell = useShellStore.getState()
    // A pane that has gone away and a pane that is showing something else are
    // two different things, and treating them as one is how a trip to the
    // Timeline closed the scene the writer was in. Only the first forgets an
    // editor; the second is a writer looking at their outline for a moment.
    const leaves = paneLeaves(shell.panes)
    const present = leaves.map((leaf) => leaf.id)
    const showingEditor = leaves.filter((leaf) => leaf.view === 'write').map((leaf) => leaf.id)
    const s = get()
    let editors = s.editors
    const stale = Object.keys(editors).filter((id) => !present.includes(id))
    if (stale.length > 0) {
      editors = { ...editors }
      for (const id of stale) {
        // A pane that goes away takes its editor with it, but not the writer's
        // last keystrokes: closing a split must never be a way to lose words.
        void flushEditor(editors[id])
        delete editors[id]
      }
    }
    // A pane that has turned into something else has no editor on screen to
    // finish the pending save, so it is written out now rather than left to a
    // timer nobody can see. The scene stays open behind it either way.
    for (const id of Object.keys(editors)) {
      if (!showingEditor.includes(id)) void get().flushPane(id)
    }

    // The shell follows the pane the writer is in when that pane is an editor,
    // and otherwise stays on the editor they were last in - which, while every
    // pane is showing something else, is what the inspector and the status bar
    // go on describing.
    const active = showingEditor.includes(shell.activePaneId)
      ? shell.activePaneId
      : s.activeEditorPaneId && present.includes(s.activeEditorPaneId)
        ? s.activeEditorPaneId
        : (showingEditor[0] ?? null)
    if (editors === s.editors && active === s.activeEditorPaneId) return
    set({ editors, activeEditorPaneId: active, ...mirror(editors, active) })
  },

  flushPane: async (paneId) => {
    const timer = autosaveTimers.get(paneId)
    if (timer) clearTimeout(timer)
    autosaveTimers.delete(paneId)
    await flushEditor(get().editors[paneId])
  },

  flushPendingSave: async () => {
    for (const timer of autosaveTimers.values()) clearTimeout(timer)
    autosaveTimers.clear()
    for (const editor of Object.values(get().editors)) await flushEditor(editor)
  },

  createChapter: async (title, insertAtOrder) => {
    const state = await rpc.request<ProjectStateDto>('project/createChapter', [
      title,
      insertAtOrder ?? null
    ])
    get().applyState(state)
  },

  createScene: async (chapterGuid, title) => {
    const state = await rpc.request<ProjectStateDto>('project/createScene', [chapterGuid, title])
    get().applyState(state)
  },

  renameChapter: async (chapterGuid, title) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/renameChapter', [chapterGuid, title])
    )
  },

  renameScene: async (chapterGuid, sceneId, title) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/renameScene', [chapterGuid, sceneId, title])
    )
  },

  deleteChapter: async (chapterGuid) => {
    set((s) => forgetScenes(s, (tab) => tab.chapterGuid !== chapterGuid))
    get().applyState(await rpc.request<ProjectStateDto>('project/deleteChapter', [chapterGuid]))
  },

  deleteScene: async (chapterGuid, sceneId) => {
    set((s) => forgetScenes(s, (tab) => tab.sceneId !== sceneId))
    get().applyState(
      await rpc.request<ProjectStateDto>('project/deleteScene', [chapterGuid, sceneId])
    )
  },

  setChapterStatus: async (chapterGuid, status) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/setChapterStatus', [chapterGuid, status])
    )
  },

  setChapterAct: async (chapterGuid, act) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/setChapterAct', [chapterGuid, act])
    )
  },

  reorderChapter: async (chapterGuid, newOrder) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/reorderChapter', [chapterGuid, newOrder])
    )
  },

  reorderScene: async (chapterGuid, sceneId, newOrder) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/reorderScene', [chapterGuid, sceneId, newOrder])
    )
  },

  moveScenes: async (sceneIds, targetChapterGuid, targetIndex) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/moveScenes', [
        sceneIds,
        targetChapterGuid,
        targetIndex
      ])
    )
  }
}))

/**
 * A key that changes whenever the data a book-scoped panel shows could have
 * changed underneath it - a different project, or a different book inside the
 * same project.
 *
 * Panels that fetch their own rows do it in an effect, and keying that effect
 * on `projectPath` alone is wrong: switching books leaves the path identical,
 * so the effect never re-runs and the panel keeps painting the previous book's
 * collections, smart lists, labels, stages, plotlines or Codex entries until
 * something happens to unmount it. Every one of those RPCs resolves against
 * `Projects.ActiveBook` on the backend, so the fetch has to follow the book.
 *
 * Use this instead of `projectPath` in the dependency array of any effect whose
 * request is answered from the active book.
 */
export function useBookScope(): string {
  return useProjectStore((s) => `${s.projectPath ?? ''}|${s.activeBookId ?? ''}`)
}

/** Full editor reset used when the active book/draft changes. */
function clearedEditorState(): Partial<ProjectState> {
  return {
    openChapterGuid: null,
    openSceneId: null,
    openSceneHtml: null,
    openScenePlainText: null,
    openTabs: [],
    editors: {},
    dirtyMap: {},
    isDirty: false
  }
}

/**
 * The five fields the rest of the shell reads, taken from one pane.
 *
 * The inspector, the status bar, find-and-replace and the scene-notes dock all
 * describe "the scene you are writing", which with several editors open means
 * the one in the pane you are in. Mirroring it here is what let those surfaces
 * stay as they were when the editor stopped being a single slot.
 */
function mirror(
  editors: Record<string, EditorPaneState>,
  activePaneId: string | null
): Pick<
  ProjectState,
  'openChapterGuid' | 'openSceneId' | 'openSceneHtml' | 'openScenePlainText' | 'openTabs' | 'isDirty'
> {
  const editor = (activePaneId && editors[activePaneId]) || EMPTY_EDITOR
  return {
    openChapterGuid: editor.chapterGuid,
    openSceneId: editor.sceneId,
    openSceneHtml: editor.html,
    openScenePlainText: editor.plainText,
    openTabs: editor.tabs,
    isDirty: editor.isDirty
  }
}

function mapEditors(
  editors: Record<string, EditorPaneState>,
  fn: (editor: EditorPaneState) => EditorPaneState
): Record<string, EditorPaneState> {
  return Object.fromEntries(Object.entries(editors).map(([id, editor]) => [id, fn(editor)]))
}

/** Changes one pane's editor state and re-mirrors if it is the active one. */
function patchEditor(
  state: ProjectState,
  paneId: string,
  patch: Partial<EditorPaneState>
): Partial<ProjectState> {
  const editors = {
    ...state.editors,
    [paneId]: { ...editorPane(state, paneId), ...patch }
  }
  return { editors, ...mirror(editors, state.activeEditorPaneId) }
}

/** Drops scenes that no longer exist from every pane's tabs and content. */
function forgetScenes(
  state: ProjectState,
  keep: (tab: SceneTabRef) => boolean
): Partial<ProjectState> {
  const editors = mapEditors(state.editors, (editor) => {
    const tabs = editor.tabs.filter(keep)
    const gone =
      editor.chapterGuid !== null &&
      editor.sceneId !== null &&
      !keep({ chapterGuid: editor.chapterGuid, sceneId: editor.sceneId })
    return gone ? { ...EMPTY_EDITOR, tabs } : { ...editor, tabs }
  })
  return { editors, ...mirror(editors, state.activeEditorPaneId) }
}

/** Every pane currently holding the editor, in the order they appear. */
function writePaneIds(): string[] {
  return paneLeaves(useShellStore.getState().panes)
    .filter((leaf) => leaf.view === 'write')
    .map((leaf) => leaf.id)
}

/**
 * Where a scene opens.
 *
 * The pane the writer is in when that pane is already an editor, so clicking a
 * scene beside the Codex does not turn the Codex into an editor; otherwise the
 * editor they were last in; otherwise this pane becomes one, which is what a
 * single-pane window has always done.
 */
function targetEditorPane(): string {
  const shell = useShellStore.getState()
  const live = writePaneIds()
  if (live.includes(shell.activePaneId)) return shell.activePaneId
  const last = useProjectStore.getState().activeEditorPaneId
  if (last && live.includes(last)) return last
  return shell.activePaneId
}

/** Writes a pane's unsaved edit, if it has one. */
async function flushEditor(editor: EditorPaneState | undefined): Promise<void> {
  if (!editor?.isDirty || !editor.chapterGuid || !editor.sceneId || editor.html === null) return
  await saveScene(editor.chapterGuid, editor.sceneId, editor.html, editor.plainText ?? '')
}

/* A pane that closes, or stops showing the editor, must not leave its scene
 * behind in the store - and the shell has to keep following whichever editor
 * the writer moved into. The shell store knows nothing about scenes, so the
 * project store watches it rather than the other way round. */
useShellStore.subscribe((state, previous) => {
  if (state.panes === previous.panes && state.activePaneId === previous.activePaneId) return
  useProjectStore.getState().syncEditorPanes()
})

/** Strips HTML tags and decodes entities to plain text for live statistics.
 * Mirrors the desktop EditorViewModel.StripHtmlForStats fast path. */
function stripHtml(html: string): string {
  if (!html) return ''
  if (!html.trimStart().startsWith('<')) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  return doc.body.textContent ?? ''
}

function scheduleSave(
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
      void saveScene(chapterGuid, sceneId, html, plainText)
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
    // The scene is on disk, so every pane holding it is clean - two panes on one
    // scene must not leave the second one claiming unsaved work forever.
    const editors = mapEditors(state.editors, (editor) =>
      editor.sceneId === sceneId && editor.isDirty ? { ...editor, isDirty: false } : editor
    )
    return {
      sceneHashes: { ...state.sceneHashes, [sceneId]: result.hash },
      editors,
      ...mirror(editors, state.activeEditorPaneId),
      dirtyMap: state.dirtyMap[sceneId] ? { ...state.dirtyMap, [sceneId]: false } : state.dirtyMap,
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

/**
 * Tells the backend which scene the editor holds, so an extension writing
 * prose can refuse to land on it.
 *
 * A pass over the manuscript - a cleanup, an import, a generated draft - used
 * to write straight over whichever scene was open, and the editor's next
 * autosave wrote back over that. Whichever landed second won and the other
 * side's words were gone, with no error anywhere. Only the renderer knows what
 * is open, so it says so.
 *
 * One subscription rather than a call at each transition: dirty is set in half
 * a dozen places and a missed one is a silent hole in the guard.
 */
let reported = ''
useProjectStore.subscribe((state) => {
  const { openChapterGuid, openSceneId, isDirty } = state
  const next = `${openChapterGuid ?? ''}|${openSceneId ?? ''}|${isDirty}`
  if (next === reported) return
  reported = next
  // A failure here is not worth surfacing: the guard degrades to the old
  // behaviour, and everything else the editor does still works.
  void rpc.request('scenes/setEditing', [openChapterGuid, openSceneId, isDirty]).catch(() => {})
})
