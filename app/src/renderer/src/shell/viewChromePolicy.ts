import type { MainView } from '../stores/shellStore'

/**
 * The shell surrounding a view.
 *
 * Keeping this policy in one exhaustive registry prevents each surface from
 * independently deciding whether writing controls, project panels, or project
 * statistics make sense. A command can stay available from the palette while
 * the persistent chrome remains focused on the work in front of the writer.
 */
export interface ViewChromePolicy {
  /** The project tree is part of the normal layout for this view. */
  binder: boolean
  /** The scene context/footnote sidebar is meaningful for this view. */
  inspector: boolean
  /** Chapter/scene creation and scene tools belong in the top bar. */
  writingActions: boolean
  /** Book and draft selectors belong in the top bar. */
  bookSelectors: boolean
  /** Project/editor status belongs at the bottom of the window. */
  status: boolean
}

const WRITING: ViewChromePolicy = {
  binder: true,
  inspector: true,
  writingActions: true,
  bookSelectors: true,
  status: true
}

const PROJECT: ViewChromePolicy = {
  binder: false,
  inspector: false,
  writingActions: false,
  bookSelectors: true,
  status: true
}

const SYSTEM: ViewChromePolicy = {
  binder: false,
  inspector: false,
  writingActions: false,
  bookSelectors: false,
  status: false
}

export const VIEW_CHROME: Record<MainView, ViewChromePolicy> = {
  write: WRITING,
  dashboard: { ...WRITING, inspector: false },
  manuscript: WRITING,
  timeline: PROJECT,
  plotGrid: PROJECT,
  calendar: PROJECT,
  relationships: PROJECT,
  dialogue: PROJECT,
  style: PROJECT,
  canvas: PROJECT,
  codex: PROJECT,
  wiki: PROJECT,
  maps: PROJECT,
  languages: PROJECT,
  series: PROJECT,
  research: PROJECT,
  gallery: PROJECT,
  expose: PROJECT,
  export: PROJECT,
  git: PROJECT,
  extensions: SYSTEM,
  settings: SYSTEM
}

export function chromeFor(view: MainView): ViewChromePolicy {
  return VIEW_CHROME[view]
}
