import type { WorkspaceSnapshot } from '../../../shared/workspaceProtocol'

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
  projectId?: string | null
  books?: { id: string; name: string; cover: string | null }[] | null
  hasWorldBible?: boolean | null
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
  /** Version this pane's document is based on, independent of other panes. */
  hash?: string
}

export type SceneStructureMethod =
  | 'sceneBulk/archive'
  | 'sceneBulk/delete'
  | 'sceneBulk/moveToChapter'
  | 'project/deleteChapter'
  | 'project/deleteScene'
  | 'project/moveScenes'

export interface ProjectState {
  isLoaded: boolean
  closingProject: boolean
  changingSceneStructure: boolean
  workspaceBusy: boolean
  workspaceSuspended: boolean
  workspaceEpoch: number
  workspaceRecovering: boolean
  workspaceRecoveryError: string | null
  workspaceTransitionToken: string | null
  activeDraftId: string | null
  applyWorkspaceSnapshot(snapshot: WorkspaceSnapshot, resetEditors?: boolean): void
  refreshWorkspaceEditors(preserveDirty?: boolean): Promise<void>
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
  applyState(state: ProjectStateDto, resetEditors?: boolean, applicationStateReady?: boolean, deferHydration?: boolean): void
  loadRecents(): Promise<void>
  openProject(path: string, bookId?: string): Promise<void>
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
  /** Applies a manuscript-editor acknowledgement without blessing a divergent
   *  dirty EditorFrame with the manuscript's newer disk hash. */
  applyManuscriptSceneWrite(
    chapterGuid: string,
    sceneId: string,
    html: string,
    plainText: string,
    wordCount: number,
    hash: string
  ): void
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
  mutateSceneStructure(method: SceneStructureMethod, args: unknown[]): Promise<void>
  /** Writes the writer's chosen text and clears the conflict. */
  resolveSceneConflict(html: string): Promise<void>
  /** Leaves the file alone and keeps the writer's text in the editor, still
   *  unsaved, so dismissing the dialog never decides anything for them. */
  dismissSceneConflict(): void
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
export interface EditingSceneClaim { chapterGuid: string; sceneId: string; dirty: boolean }

export type ProjectSlice<K extends keyof ProjectState> = (set: import('zustand').StoreApi<ProjectState>['setState'], get: () => ProjectState) => Pick<ProjectState, K>
