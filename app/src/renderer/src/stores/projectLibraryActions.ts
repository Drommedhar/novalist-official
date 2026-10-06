import { rpc } from '../rpc/client'
import { useSettingsStore } from './settingsStore'
import { flushPendingWrites } from './pendingWrites'
import { type ProjectStateDto, type RecentProjectDto, type ProjectSlice } from './projectTypes'
import { useProjectStore } from './projectStore'
import { runSceneContext } from './projectSceneActions'

let recentRefresh: Promise<void> | null = null

let recentRefreshRequested = false

let recentRefreshVersion = 0

/** Fresh library data must be ready before an unloaded workspace is shown.
 * This must not join a pre-transition read held behind the resume barrier. */
export async function prepareProjectLibrary(): Promise<void> {
  const version = ++recentRefreshVersion
  recentRefreshRequested = false
  const [recents] = await Promise.all([
    rpc.request<RecentProjectDto[]>('project/recent', [true]),
    useSettingsStore.getState().load()
  ])
  if (version === recentRefreshVersion) useProjectStore.setState({ recentProjects: recents })
}

function sameRecentProjects(left: RecentProjectDto[], right: RecentProjectDto[]): boolean {
  return left.length === right.length && left.every((entry, index) => {
    const other = right[index]
    return entry.path === other.path && entry.name === other.name && entry.cover === other.cover &&
      entry.projectId === other.projectId && entry.hasWorldBible === other.hasWorldBible &&
      (entry.books === other.books || (entry.books != null && other.books != null &&
        entry.books.length === other.books.length && entry.books.every((book, i) =>
          book.id === other.books![i].id && book.name === other.books![i].name && book.cover === other.books![i].cover)))
  })
}

export const createProjectLibraryActions: ProjectSlice<'loadRecents' | 'openProject' | 'closeProject' | 'pickAndOpenProject'> = (set, get) => ({
  loadRecents: () => {
    if (get().closingProject) return Promise.resolve()
    recentRefreshRequested = true
    if (recentRefresh) return recentRefresh
    recentRefresh = (async () => {
      try {
        do {
          recentRefreshRequested = false
          const version = recentRefreshVersion
          const includeLibraryDetails = !get().isLoaded
          const recents = await rpc.request<RecentProjectDto[]>('project/recent', [includeLibraryDetails])
          if (version !== recentRefreshVersion) continue
          // Opening/closing a project during a refresh changes which data is
          // needed. Finish with a fresh response rather than applying stale data.
          if (includeLibraryDetails !== !get().isLoaded) {
            recentRefreshRequested = true
            continue
          }
          if (!sameRecentProjects(get().recentProjects, recents)) set({ recentProjects: recents })
        } while (recentRefreshRequested)
      } finally { recentRefresh = null }
    })()
    return recentRefresh
  },

  openProject: (path, bookId) => runSceneContext(async () => {
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
    await flushPendingWrites()
    await get().flushPendingSave()
    const state = await rpc.request<ProjectStateDto>('project/open', [target, bookId ?? null])
    get().applyState(state)
  }),

  closeProject: () => runSceneContext(async () => {
    if (get().closingProject) return
    set({ closingProject: true })
    recentRefreshVersion++
    recentRefreshRequested = false
    let closed: ProjectStateDto | null = null
    try {
      await flushPendingWrites()
      await get().flushPendingSave()
      closed = await rpc.request<ProjectStateDto>('project/close')
      // Keep the project inert until the library and global appearance are
      // ready. Publishing unloaded state first flashes menu-only entries in
      // the project's language and then rebuilds the visible bookshelf.
      await prepareProjectLibrary()
      get().applyState(closed, false, true)
    } catch (error) {
      // The backend may already be closed: never leave an editable stale
      // project behind if preparing the library fails.
      if (closed) get().applyState(closed)
      throw error
    } finally { set({ closingProject: false }) }
  }),

  pickAndOpenProject: async () => {
    const path = await window.novalist.pickFolder('Novalist')
    if (path) await get().openProject(path)
  }
})
