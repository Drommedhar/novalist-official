import { type ShellCapacity, type PanelSizes } from './shellTypes'

/* ===== Panel geometry =====
 * Sizes the user drags survive a restart, and the first-run defaults are taken
 * from the window rather than fixed, because the app opens maximised: a flat
 * 240px binder that reads fine on a laptop is a sliver on a 2560px display.
 * Persisted to localStorage alongside the other view-state preferences (see
 * ContextPanel's section-collapse flags) rather than through the settings
 * backend - this is view state, not configuration. */
const PANEL_STORAGE_KEY = 'nl.shell.panels'

/* The floors are what a panel has to be to do its job, not the narrowest it can
 * be drawn. At 180 the binder left about 120px for a scene title - the app runs
 * on a 17px serif, so that is a dozen characters - and every chapter and every
 * scene in the tree read as an ellipsis, with the three scene-filter chips
 * coming out as "In the ...", "Every..." and "Out o...". 290 is what an
 * ordinary twenty-five-character scene title measures once the indent, the
 * status dot and the word count have taken their share; the inspector wraps its
 * text rather than clipping it, so it needs less. */
export const BINDER_MIN = 290

export const BINDER_MAX = 640

export const INSPECTOR_MIN = 280

export const INSPECTOR_MAX = 720

export const NOTES_DOCK_MIN = 80

export const NOTES_DOCK_MAX = 640

export function shellCapacityForWidth(width: number): ShellCapacity {
  if (width < 900) return 'compact'
  if (width < 1240) return 'medium'
  return 'wide'
}

/**
 * A remembered drag width is a preference, not permission to squeeze the
 * editor out of the window. Runtime width is capped against the shell itself;
 * the stored preference remains untouched and comes back on a wider monitor.
 */
export function panelWidthForShell(
  preferred: number,
  shellWidth: number,
  min: number,
  max: number
): number {
  const capacityCap = Math.max(min, Math.floor(shellWidth * 0.28))
  return clamp(preferred, min, Math.min(max, capacityCap))
}

export function clamp(px: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.round(px)))
}

function readPanelSizes(): PanelSizes {
  try {
    return JSON.parse(localStorage.getItem(PANEL_STORAGE_KEY) || '{}') as PanelSizes
  } catch {
    return {}
  }
}

/** Merges one measurement into the stored set. Called on drag end, not on every
 *  pointer move, so a drag writes once. */
export function savePanelSize(patch: PanelSizes): void {
  try {
    localStorage.setItem(PANEL_STORAGE_KEY, JSON.stringify({ ...readPanelSizes(), ...patch }))
  } catch {
    // Private mode or a full quota: the session still works, it just forgets.
  }
}

/** A stored size if the user has ever set one, else a share of the window. */
export function initialPanelSize(
  stored: number | undefined,
  fraction: number,
  min: number,
  max: number,
  basis: number
): number {
  if (typeof stored === 'number' && Number.isFinite(stored)) return clamp(stored, min, max)
  return clamp(basis * fraction, min, max)
}

export const storedPanels = readPanelSizes()

/* Initial guesses only. AppShell immediately replaces the basis with its own
 * ResizeObserver measurement, which is the width that actually matters after
 * OS DPI, page scale, split view and window restoration have been applied. */
export const screenW = typeof window === 'undefined' ? 1440 : window.innerWidth || 1440

const screenH = typeof window === 'undefined' ? 900 : window.innerHeight || 900

export const NOTES_DOCK_DEFAULT = initialPanelSize(
  storedPanels.notesDockHeight,
  0.16,
  NOTES_DOCK_MIN,
  NOTES_DOCK_MAX,
  screenH
)
