import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useShellStore } from './shellStore'

export interface SceneDto {
  id: string
  title: string
  order: number
  wordCount: number
  labelColor: string | null
  isFavorite: boolean
  synopsis: string | null
}

export interface ChapterDto {
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
}

/** One open scene in an editor pane's tab strip. Title is resolved from
 * `chapters` at render time so renames stay live. */
export interface SceneTabRef {
  chapterGuid: string
  sceneId: string
}

export type EditorPane = 'primary' | 'split'

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
  openSceneHtml: string | null
  openScenePlainText: string | null
  openTabs: SceneTabRef[]
  splitChapterGuid: string | null
  splitSceneId: string | null
  splitSceneHtml: string | null
  splitTabs: SceneTabRef[]
  /** Per-scene unsaved-edit flags, keyed by sceneId (drives the tab dirty dot). */
  dirtyMap: Record<string, boolean>
  isDirty: boolean
  applyState(state: ProjectStateDto): void
  loadRecents(): Promise<void>
  openProject(path: string): Promise<void>
  pickAndOpenProject(): Promise<void>
  openScene(chapterGuid: string, sceneId: string): Promise<void>
  openSceneInSplit(chapterGuid: string, sceneId: string): Promise<void>
  closeSplit(): void
  closeTab(pane: EditorPane, sceneId: string): Promise<void>
  moveTabToOtherPane(pane: EditorPane, sceneId: string): Promise<void>
  onEditorContentChanged(html: string, plainText: string): void
  onSplitContentChanged(html: string, plainText: string): void
  flushPendingSave(): Promise<void>
  createChapter(title: string): Promise<void>
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
  openSceneHtml: null,
  openScenePlainText: null,
  openTabs: [],
  splitChapterGuid: null,
  splitSceneId: null,
  splitSceneHtml: null,
  splitTabs: [],
  dirtyMap: {},
  isDirty: false,

  applyState: (state) => {
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
    const state = await rpc.request<ProjectStateDto>('project/open', [path])
    get().applyState(state)
  },

  pickAndOpenProject: async () => {
    const path = await window.novalist.pickFolder('Novalist')
    if (path) await get().openProject(path)
  },

  openScene: async (chapterGuid, sceneId) => {
    await get().flushPendingSave()
    const content = await rpc.request<{ sceneId: string; html: string }>('scenes/read', [
      chapterGuid,
      sceneId
    ])
    const tabs = get().openTabs
    set({
      openChapterGuid: chapterGuid,
      openSceneId: sceneId,
      openSceneHtml: content.html,
      openScenePlainText: stripHtml(content.html),
      openTabs: tabs.some((t) => t.sceneId === sceneId) ? tabs : [...tabs, { chapterGuid, sceneId }]
    })
    useShellStore.getState().setMainView('write')
  },

  openSceneInSplit: async (chapterGuid, sceneId) => {
    const content = await rpc.request<{ sceneId: string; html: string }>('scenes/read', [
      chapterGuid,
      sceneId
    ])
    const tabs = get().splitTabs
    set({
      splitChapterGuid: chapterGuid,
      splitSceneId: sceneId,
      splitSceneHtml: content.html,
      splitTabs: tabs.some((t) => t.sceneId === sceneId) ? tabs : [...tabs, { chapterGuid, sceneId }]
    })
    useShellStore.getState().setMainView('write')
  },

  closeSplit: () =>
    set({ splitChapterGuid: null, splitSceneId: null, splitSceneHtml: null, splitTabs: [] }),

  closeTab: async (pane, sceneId) => {
    const s = get()
    if (pane === 'primary') {
      const idx = s.openTabs.findIndex((t) => t.sceneId === sceneId)
      if (idx < 0) return
      const isActive = s.openSceneId === sceneId
      if (isActive) await get().flushPendingSave()
      const remaining = get().openTabs.filter((t) => t.sceneId !== sceneId)
      if (!isActive) {
        set({ openTabs: remaining })
        return
      }
      if (remaining.length === 0) {
        set({
          openTabs: [],
          openChapterGuid: null,
          openSceneId: null,
          openSceneHtml: null,
          openScenePlainText: null,
          isDirty: false
        })
        // No scenes left open in the editor — fall back to the dashboard
        // (unless a scene is still open in the split pane).
        if (get().splitSceneId === null) useShellStore.getState().setMainView('dashboard')
        return
      }
      const next = remaining[Math.min(idx, remaining.length - 1)]
      set({ openTabs: remaining })
      await get().openScene(next.chapterGuid, next.sceneId)
    } else {
      const idx = s.splitTabs.findIndex((t) => t.sceneId === sceneId)
      if (idx < 0) return
      const isActive = s.splitSceneId === sceneId
      const remaining = s.splitTabs.filter((t) => t.sceneId !== sceneId)
      if (!isActive) {
        set({ splitTabs: remaining })
        return
      }
      if (remaining.length === 0) {
        get().closeSplit()
        return
      }
      const next = remaining[Math.min(idx, remaining.length - 1)]
      set({ splitTabs: remaining })
      await get().openSceneInSplit(next.chapterGuid, next.sceneId)
    }
  },

  moveTabToOtherPane: async (pane, sceneId) => {
    const srcTabs = pane === 'primary' ? get().openTabs : get().splitTabs
    const tab = srcTabs.find((t) => t.sceneId === sceneId)
    if (!tab) return
    await get().closeTab(pane, sceneId)
    if (pane === 'primary') await get().openSceneInSplit(tab.chapterGuid, tab.sceneId)
    else await get().openScene(tab.chapterGuid, tab.sceneId)
  },

  onEditorContentChanged: (html, plainText) => {
    const { openChapterGuid, openSceneId } = get()
    if (!openChapterGuid || !openSceneId) return
    set((state) => ({
      openSceneHtml: html,
      openScenePlainText: plainText,
      isDirty: true,
      dirtyMap: { ...state.dirtyMap, [openSceneId]: true }
    }))
    scheduleSave('primary', openChapterGuid, openSceneId, html, plainText)
  },

  onSplitContentChanged: (html, plainText) => {
    const { splitChapterGuid, splitSceneId } = get()
    if (!splitChapterGuid || !splitSceneId) return
    set((state) => ({
      splitSceneHtml: html,
      dirtyMap: { ...state.dirtyMap, [splitSceneId]: true }
    }))
    scheduleSave('split', splitChapterGuid, splitSceneId, html, plainText)
  },

  flushPendingSave: async () => {
    const { isDirty, openChapterGuid, openSceneId, openSceneHtml } = get()
    for (const timer of autosaveTimers.values()) clearTimeout(timer)
    autosaveTimers.clear()
    if (isDirty && openChapterGuid && openSceneId && openSceneHtml !== null) {
      await saveScene(openChapterGuid, openSceneId, openSceneHtml, '')
    }
  },

  createChapter: async (title) => {
    const state = await rpc.request<ProjectStateDto>('project/createChapter', [title])
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
    set((s) => ({
      openTabs: s.openTabs.filter((t) => t.chapterGuid !== chapterGuid),
      splitTabs: s.splitTabs.filter((t) => t.chapterGuid !== chapterGuid),
      ...(s.openChapterGuid === chapterGuid
        ? {
            openChapterGuid: null,
            openSceneId: null,
            openSceneHtml: null,
            openScenePlainText: null,
            isDirty: false
          }
        : {}),
      ...(s.splitChapterGuid === chapterGuid
        ? { splitChapterGuid: null, splitSceneId: null, splitSceneHtml: null }
        : {})
    }))
    get().applyState(await rpc.request<ProjectStateDto>('project/deleteChapter', [chapterGuid]))
  },

  deleteScene: async (chapterGuid, sceneId) => {
    set((s) => ({
      openTabs: s.openTabs.filter((t) => t.sceneId !== sceneId),
      splitTabs: s.splitTabs.filter((t) => t.sceneId !== sceneId),
      ...(s.openSceneId === sceneId
        ? {
            openChapterGuid: null,
            openSceneId: null,
            openSceneHtml: null,
            openScenePlainText: null,
            isDirty: false
          }
        : {}),
      ...(s.splitSceneId === sceneId
        ? { splitChapterGuid: null, splitSceneId: null, splitSceneHtml: null }
        : {})
    }))
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

/** Full editor reset used when the active book/draft changes. */
function clearedEditorState(): Partial<ProjectState> {
  return {
    openChapterGuid: null,
    openSceneId: null,
    openSceneHtml: null,
    openScenePlainText: null,
    openTabs: [],
    splitChapterGuid: null,
    splitSceneId: null,
    splitSceneHtml: null,
    splitTabs: [],
    dirtyMap: {},
    isDirty: false
  }
}

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
  const result = await rpc.request<{ sceneId: string; wordCount: number }>('scenes/write', [
    chapterGuid,
    sceneId,
    html,
    plainText
  ])
  useProjectStore.setState((state) => ({
    isDirty: state.openSceneId === sceneId ? false : state.isDirty,
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
  }))
}
