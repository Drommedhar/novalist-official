import type { MainView } from '../stores/shellStore'

/**
 * The five things a writer sits down to do.
 *
 * Novalist had nineteen top-level destinations in a rail of unlabelled icons,
 * eight of them under one heading, distinguishable only by hovering for a
 * tooltip - and every extension that contributed a view made the rail longer,
 * until the rail's answer to running out of room was to hide the surplus behind
 * a "...". The app's own growth made it harder to find things in.
 *
 * Underneath, the chrome around those nineteen was decided by a
 * twenty-two-entry table crossed with three window widths: sixty-six
 * combinations that nobody could hold in their head and nothing verified.
 *
 * A mode is a workspace. Picking one decides what the window looks like and
 * which views are one click away, so the chrome table collapses to five layouts
 * and "where is the Plot Grid" becomes "Plan" rather than "one of nineteen
 * icons". View identity does not disappear: `MainView` is still what panes,
 * deep links, help targets and the palette address. It simply stops being the
 * top-level navigation.
 */
export type Mode = 'write' | 'plan' | 'world' | 'publish' | 'series'

/**
 * The views each mode holds, in the order its panel lists them.
 *
 * Two decisions worth remembering:
 *
 * - **Style report is in Publish, not Plan.** It is a revision pass over a
 *   finished draft rather than a planning tool.
 * - **Series is its own mode.** Folding it into Plan meant a mode that was
 *   otherwise about one book carrying one view that was not.
 */
export const MODE_VIEWS: Record<Mode, MainView[]> = {
  write: ['write', 'manuscript'],
  plan: ['timeline', 'plotGrid', 'calendar', 'canvas', 'relationships', 'dialogue'],
  world: ['codex', 'wiki', 'maps', 'research', 'gallery', 'languages'],
  publish: ['expose', 'export', 'git', 'style'],
  series: ['series']
}

export const MODES: Mode[] = ['write', 'plan', 'world', 'publish', 'series']

/**
 * How a mode's panel groups the views it lists.
 *
 * A flat list of six is already harder to scan than two groups of three, and a
 * mode with a dozen extension views is unreadable without them. The names are
 * the part doing that work, and the part most likely to be wrong: they were
 * chosen from a mock rather than from use, and are expected to move.
 *
 * A mode with a single group renders no label - a heading over the whole list
 * says nothing the panel's own title has not.
 */
export interface ViewGroup {
  labelKey: string
  views: MainView[]
}

export const MODE_GROUPS: Record<Mode, ViewGroup[]> = {
  write: [{ labelKey: 'modes.group.drafting', views: ['write', 'manuscript'] }],
  plan: [
    { labelKey: 'modes.group.shape', views: ['timeline', 'plotGrid', 'canvas'] },
    { labelKey: 'modes.group.castAndTime', views: ['relationships', 'calendar', 'dialogue'] }
  ],
  world: [
    { labelKey: 'modes.group.inThisBook', views: ['codex', 'wiki', 'maps'] },
    { labelKey: 'modes.group.reference', views: ['research', 'gallery', 'languages'] }
  ],
  publish: [
    { labelKey: 'modes.group.prepare', views: ['expose', 'style'] },
    { labelKey: 'modes.group.produce', views: ['export', 'git'] }
  ],
  series: [{ labelKey: 'modes.group.acrossBooks', views: ['series'] }]
}

/**
 * The group contributed views go in: their own, last.
 *
 * So a newly installed extension never reorders a core view or pushes one out
 * of sight. The rail this replaces appended extension icons to a flat list,
 * which made them the first thing to fall into the overflow menu - the icons
 * hardest to find were the ones the writer had gone out of their way to add.
 */
export const EXTENSION_GROUP = 'modes.group.fromExtensions'

/** An extension view that names no mode joins this one. */
export const DEFAULT_EXTENSION_MODE: Mode = 'world'

/**
 * Past this many views, the panel grows a filter.
 *
 * An accelerator, never the way something becomes reachable: every view is in
 * the list whether or not the filter is showing, which is the difference
 * between this and the overflow menu it replaced.
 */
export const FILTER_THRESHOLD = 10

/**
 * Views that belong to no mode.
 *
 * Dashboard is the screen a project opens on - where you are before you have
 * chosen what to do today, and the one place allowed to talk about all five
 * modes at once. Treating it as part of Write was always slightly wrong: it is
 * about the book, not about the scene in front of you.
 *
 * Settings, Extensions and About are application-scoped, so under the placement
 * law they are reached from the menu bar and by route rather than by being
 * destinations on a rail.
 */
export const HOME_VIEW: MainView = 'dashboard'

const VIEW_MODE = new Map<MainView, Mode>(
  MODES.flatMap((mode) => MODE_VIEWS[mode].map((view) => [view, mode] as [MainView, Mode]))
)

/** The mode this view belongs to, or null for the ones that belong to none. */
export function modeOf(view: MainView): Mode | null {
  return VIEW_MODE.get(view) ?? null
}

/**
 * What a mode's workspace looks like.
 *
 * This replaces `viewChromePolicy.ts`, which held one entry per view and so
 * grew a row every time a view was added - each one a fresh chance to decide
 * differently from its neighbours. Five layouts can be reviewed in one sitting;
 * twenty-two could not, which is why nobody ever did.
 */
export interface ModeChrome {
  /**
   * The chapter and scene tree. Write's alone: the export view already has its
   * own controls for choosing what goes in, and planning acts and chapters is
   * work on the same tree but keeping the binder in one place is what holds the
   * line that a mode owns its layout.
   */
  binder: boolean
  /** The scene context sidebar. */
  inspector: boolean
  /** Book and draft selectors, and chapter/scene creation, on the project bar. */
  projectBar: boolean
  /** Project and scene statistics at the bottom of the window. */
  status: boolean
}

const NO_CHROME: ModeChrome = {
  binder: false,
  inspector: false,
  projectBar: false,
  status: false
}

/*
 * One rule, rather than a decision per mode: the project bar shows wherever the
 * mode is about the open book, and Series is the only one that is not - it sits
 * above the single book, so naming which book you are in would be a category
 * error there. The rail and the mode panel are always present.
 */
export const MODE_CHROME: Record<Mode, ModeChrome> = {
  write: { binder: true, inspector: true, projectBar: true, status: true },
  plan: { ...NO_CHROME, projectBar: true, status: true },
  world: { ...NO_CHROME, projectBar: true, status: true },
  publish: { ...NO_CHROME, projectBar: true, status: true },
  series: { ...NO_CHROME, status: true }
}

/** The Dashboard's own chrome. It is about the book, so it keeps the book's bar. */
export const HOME_CHROME: ModeChrome = {
  binder: false,
  inspector: false,
  projectBar: true,
  status: true
}

/**
 * Settings, Extensions and About keep none of it. They are not about a book,
 * and Settings had become a fourth column beside three panels it had nothing
 * to do with.
 */
export const ROUTED_CHROME: ModeChrome = NO_CHROME

/** The workspace for whatever is in the main area. */
export function chromeForView(view: MainView): ModeChrome {
  if (view === HOME_VIEW) return HOME_CHROME
  const mode = modeOf(view)
  return mode ? MODE_CHROME[mode] : ROUTED_CHROME
}
