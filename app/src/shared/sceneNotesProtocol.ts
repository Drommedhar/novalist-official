export interface SceneNotesContext {
  projectPath: string
  bookId: string | null
  draftId: string | null
  epoch: number
  chapterGuid: string
  sceneId: string
  title: string
}

export interface SceneNotesWindowState {
  context: SceneNotesContext | null
  visible: boolean
  pinned: boolean
  docked: boolean
  follow: boolean
}

export interface NotesBounds { x: number; y: number; width: number; height: number }

export function sameNotesScope(a: SceneNotesContext, b: SceneNotesContext): boolean {
  return a.projectPath === b.projectPath && a.bookId === b.bookId && a.draftId === b.draftId && a.epoch === b.epoch
}

export function selectNotesContext(held: SceneNotesContext | null, incoming: SceneNotesContext | null, follow: boolean): SceneNotesContext | null {
  return follow || !held || !incoming || !sameNotesScope(held, incoming) ? incoming : held
}

export function clampNotesBounds(bounds: NotesBounds, area: NotesBounds): NotesBounds {
  const width = Math.min(Math.max(bounds.width, 320), area.width)
  const height = Math.min(Math.max(bounds.height, 260), area.height)
  return { width, height,
    x: Math.max(area.x, Math.min(bounds.x, area.x + area.width - width)),
    y: Math.max(area.y, Math.min(bounds.y, area.y + area.height - height)) }
}
