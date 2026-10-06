import { create } from 'zustand'
import { paneLeaves, useShellStore } from './shellStore'
import { type ChapterDto, type EditorPaneState, type ProjectState } from './projectTypes'
import { reportEditingScenes } from './projectPersistence'
import { createProjectWorkspaceActions } from './projectWorkspaceActions'
import { createProjectLibraryActions } from './projectLibraryActions'
import { createProjectEditorActions } from './projectEditorActions'
import { createProjectSceneActions } from './projectSceneActions'

export const EMPTY_EDITOR: EditorPaneState = {
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

export const useProjectStore = create<ProjectState>((set, get) => ({
  isLoaded: false,
  closingProject: false,
  changingSceneStructure: false,
  workspaceBusy: false,
  workspaceSuspended: false,
  workspaceEpoch: 0,
  workspaceRecovering: false,
  workspaceRecoveryError: null,
  workspaceTransitionToken: null,
  activeDraftId: null,
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
  ...createProjectWorkspaceActions(set, get),
  ...createProjectLibraryActions(set, get),
  ...createProjectEditorActions(set, get),
  ...createProjectSceneActions(set, get)
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
  return useProjectStore((s) => `${s.projectPath ?? ''}|${s.activeBookId ?? ''}|${s.activeDraftId ?? ''}`)
}

/** Full editor reset used when the project, active book or draft changes. */
export function clearedEditorState(): Partial<ProjectState> {
  return {
    openChapterGuid: null,
    openSceneId: null,
    openSceneHtml: null,
    openScenePlainText: null,
    openTabs: [],
    editors: {},
    activeEditorPaneId: null,
    sceneHashes: {},
    sceneConflict: null,
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
export function mirror(
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

export function mapEditors(
  editors: Record<string, EditorPaneState>,
  fn: (editor: EditorPaneState) => EditorPaneState
): Record<string, EditorPaneState> {
  return Object.fromEntries(Object.entries(editors).map(([id, editor]) => [id, fn(editor)]))
}

/** Changes one pane's editor state and re-mirrors if it is the active one. */
export function patchEditor(
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
export function reconcileSceneEditors(
  state: ProjectState,
  chapters: ChapterDto[]
): Partial<ProjectState> {
  const chapterByScene = new Map(chapters.flatMap((chapter) =>
    chapter.scenes.map((scene) => [scene.id, chapter.guid] as const)))
  const editors = mapEditors(state.editors, (editor) => {
    const tabs = editor.tabs.flatMap((tab) => {
      const chapterGuid = chapterByScene.get(tab.sceneId)
      return chapterGuid ? [{ ...tab, chapterGuid }] : []
    })
    const chapterGuid = editor.sceneId ? chapterByScene.get(editor.sceneId) : null
    return editor.sceneId && !chapterGuid
      ? { ...EMPTY_EDITOR, tabs }
      : { ...editor, chapterGuid: chapterGuid ?? null, tabs }
  })
  return {
    editors,
    ...mirror(editors, state.activeEditorPaneId),
    dirtyMap: Object.fromEntries(Object.entries(state.dirtyMap).filter(([id]) => chapterByScene.has(id))),
    sceneHashes: Object.fromEntries(Object.entries(state.sceneHashes).filter(([id]) => chapterByScene.has(id)))
  }
}

/** Every pane currently holding the editor, in the order they appear. */
export function writePaneIds(): string[] {
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
export function targetEditorPane(): string {
  const shell = useShellStore.getState()
  const live = writePaneIds()
  if (live.includes(shell.activePaneId)) return shell.activePaneId
  const last = useProjectStore.getState().activeEditorPaneId
  if (last && live.includes(last)) return last
  return shell.activePaneId
}

/* A pane that closes, or stops showing the editor, must not leave its scene
 * behind in the store - and the shell has to keep following whichever editor
 * the writer moved into. The shell store knows nothing about scenes, so the
 * project store watches it rather than the other way round. */
useShellStore.subscribe((state, previous) => {
  if (state.panes === previous.panes && state.activePaneId === previous.activePaneId) return
  useProjectStore.getState().syncEditorPanes()
})

useProjectStore.subscribe(() => reportEditingScenes())

export type { SceneDto, ChapterDto, ProjectStateDto, RecentProjectDto, SceneTabRef, EditorPaneState, SceneStructureMethod, EditingSceneClaim } from './projectTypes'
export { prepareProjectLibrary } from './projectLibraryActions'
export { reportManuscriptEditing, reportEditingScenes } from './projectPersistence'
