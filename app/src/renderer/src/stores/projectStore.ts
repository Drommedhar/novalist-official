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

// Matches the Avalonia EditorViewModel.AutoSaveDelayMs default.
const AUTOSAVE_DELAY_MS = 2000

let autosaveTimer: ReturnType<typeof setTimeout> | null = null

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
  isDirty: boolean
  applyState(state: ProjectStateDto): void
  loadRecents(): Promise<void>
  openProject(path: string): Promise<void>
  pickAndOpenProject(): Promise<void>
  openScene(chapterGuid: string, sceneId: string): Promise<void>
  onEditorContentChanged(html: string, plainText: string): void
  flushPendingSave(): Promise<void>
  createChapter(title: string): Promise<void>
  createScene(chapterGuid: string, title: string): Promise<void>
  switchBook(bookId: string): Promise<void>
  createBook(name: string): Promise<void>
  loadDrafts(): Promise<void>
  createDraft(name: string): Promise<void>
  switchDraft(draftId: string): Promise<void>
  renameChapter(chapterGuid: string, title: string): Promise<void>
  renameScene(chapterGuid: string, sceneId: string, title: string): Promise<void>
  deleteChapter(chapterGuid: string): Promise<void>
  deleteScene(chapterGuid: string, sceneId: string): Promise<void>
  setChapterStatus(chapterGuid: string, status: string): Promise<void>
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
    set({ openChapterGuid: null, openSceneId: null, openSceneHtml: null, isDirty: false })
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
    set({ openChapterGuid: null, openSceneId: null, openSceneHtml: null, isDirty: false })
    get().applyState(await rpc.request<ProjectStateDto>('project/switchDraft', [draftId]))
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
    set({ openChapterGuid: chapterGuid, openSceneId: sceneId, openSceneHtml: content.html })
    useShellStore.getState().setMainView('write')
  },

  onEditorContentChanged: (html, plainText) => {
    const { openChapterGuid, openSceneId } = get()
    if (!openChapterGuid || !openSceneId) return
    set({ openSceneHtml: html, isDirty: true })
    if (autosaveTimer) clearTimeout(autosaveTimer)
    autosaveTimer = setTimeout(() => {
      void saveScene(openChapterGuid, openSceneId, html, plainText)
    }, AUTOSAVE_DELAY_MS)
  },

  flushPendingSave: async () => {
    const { isDirty, openChapterGuid, openSceneId, openSceneHtml } = get()
    if (autosaveTimer) {
      clearTimeout(autosaveTimer)
      autosaveTimer = null
    }
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
    if (get().openChapterGuid === chapterGuid) {
      set({ openChapterGuid: null, openSceneId: null, openSceneHtml: null, isDirty: false })
    }
    get().applyState(await rpc.request<ProjectStateDto>('project/deleteChapter', [chapterGuid]))
  },

  deleteScene: async (chapterGuid, sceneId) => {
    if (get().openSceneId === sceneId) {
      set({ openChapterGuid: null, openSceneId: null, openSceneHtml: null, isDirty: false })
    }
    get().applyState(
      await rpc.request<ProjectStateDto>('project/deleteScene', [chapterGuid, sceneId])
    )
  },

  setChapterStatus: async (chapterGuid, status) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/setChapterStatus', [chapterGuid, status])
    )
  }
}))

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
