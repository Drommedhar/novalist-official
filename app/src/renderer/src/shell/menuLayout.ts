import i18next from 'i18next'
import { COMMANDS, commandById, homeOf, type CommandDef } from './commands'
import { buildDefaultHotkeys } from './hotkeys'
import { useProjectStore } from '../stores/projectStore'

/**
 * The menu bar, built from the command registry.
 *
 * Windows and Linux hid the menu bar so the toolbar could act as the window
 * chrome, which left the app with no complete index of itself: the only
 * exhaustive surface was one you had to know to press Alt for. It is back, and
 * it is generated rather than hand-written - so a command that joins the
 * registry with application scope appears in it, and one that leaves does not
 * linger as a menu item pointing at nothing.
 *
 * Only application-scoped commands are here. A command that acts on the open
 * project lives on the toolbar, one that acts on the writing view lives on the
 * editor's bar, and one that acts on a selection lives in the toolbar over that
 * selection. That is the whole placement law, and the menu bar is where it
 * would be easiest to quietly break by adding "just one more" convenient copy.
 */

/** A node the main process can turn into an Electron menu item. */
export type MenuNode =
  | { kind: 'separator' }
  /** An Electron role - undo, copy, quit, fullscreen - which main owns. */
  | { kind: 'role'; role: string }
  | {
      kind: 'command'
      id: string
      label: string
      /** Shown, never registered: the renderer stays the only dispatcher. */
      accelerator?: string
      enabled: boolean
    }
  | { kind: 'submenu'; label: string; items: MenuNode[] }

/**
 * Novalist's gesture grammar, in Electron's.
 *
 * "Ctrl" means the platform's own modifier here as it does everywhere else in
 * the app, and the digit gestures carry the Avalonia "D1" spelling the settings
 * file has always used.
 */
export function toAccelerator(gesture: string): string | undefined {
  if (gesture.length === 0) return undefined
  const parts = gesture.split('+')
  const key = parts[parts.length - 1]
  const named: Record<string, string> = {
    ArrowRight: 'Right',
    ArrowLeft: 'Left',
    ArrowUp: 'Up',
    ArrowDown: 'Down',
    // Electron knows "Plus" but spells the other one as the character itself.
    Minus: '-'
  }
  const normalized =
    /^D[0-9]$/.test(key) ? key.slice(1) : (named[key] ?? key)
  const modifiers = parts
    .slice(0, -1)
    .map((part) => (part === 'Ctrl' ? 'CmdOrCtrl' : part))
  return [...modifiers, normalized].join('+')
}

/* The order the menus put commands in. Ids, so a rename cannot silently drop
   one - and so the placement doctor can read the menu bar's contents without
   running Electron. */
// placement-container: menuBar list
const FILE = [
  'app.newProject',
  'app.openProject',
  'app.importProject',
  'app.importManuscript',
  '-',
  'project.quickCapture',
  'app.print'
]

/* Go, grouped exactly as the modes group the app, so the menu bar and the rail
   tell the same story. A view missing from here has no home and the placement
   doctor says so; one listed here that no longer exists is reported too. */
const GO_GROUPS = [
  ['nav.dashboard'],
  ['nav.write', 'nav.manuscript'],
  ['nav.timeline', 'nav.plotGrid', 'nav.canvas', 'nav.relationships', 'nav.calendar', 'nav.dialogue'],
  ['nav.codex', 'nav.wiki', 'nav.maps', 'nav.research', 'nav.gallery', 'nav.languages'],
  ['nav.expose', 'nav.export', 'nav.git', 'nav.style'],
  ['nav.series'],
  ['nav.extensions', 'nav.settings'],
  ['app.quickOpen', 'app.commandPalette']
]

const VIEW = [
  'app.toggleModePanel',
  'app.toggleBinder',
  'app.toggleInspector',
  'app.toggleSceneNotes',
  '-',
  'app.focusMode',
  '-',
  'app.splitRight',
  'app.splitDown',
  'app.closePane',
  'app.resetPanes',
  'app.popOut',
  '-',
  'app.paneLayouts',
  'app.layouts',
  '-',
  'app.uiScaleIncrease',
  'app.uiScaleDecrease',
  'app.uiScaleReset'
]

const HELP = ['app.manual', 'app.tour', '-', 'app.about']
// placement-container: end

/** Every application-scoped command the menu bar is expected to carry. */
export function menuBarCommands(): CommandDef[] {
  return COMMANDS.filter((command) => homeOf(command) === 'menuBar')
}

/** The prefix a Recent-projects item sends back, followed by its path. */
export const OPEN_RECENT = 'openRecent:'

/**
 * Builds the template for the current language, project state and gestures.
 *
 * Rebuilt rather than patched: enablement, labels and shortcuts all move, and a
 * menu that is half-refreshed is worse than one rebuilt from scratch.
 */
export function buildMenuTemplate(): MenuNode[] {
  const t = (key: string): string => i18next.t(key)
  const gestures = new Map(buildDefaultHotkeys().map((a) => [a.actionId, a.gesture]))

  const item = (id: string): MenuNode => {
    if (id === '-') return { kind: 'separator' }
    const command = commandById(id)
    // A menu entry for a command that no longer exists would be a dead item
    // rather than a missing one, which is the harder failure to notice.
    if (!command) return { kind: 'separator' }
    return {
      kind: 'command',
      id,
      label: t(command.labelKey),
      accelerator: toAccelerator(gestures.get(id) ?? ''),
      enabled: command.available?.() !== false
    }
  }

  const items = (ids: string[]): MenuNode[] => ids.map(item)

  const go: MenuNode[] = []
  for (const group of GO_GROUPS) {
    if (go.length > 0) go.push({ kind: 'separator' })
    go.push(...items(group))
  }

  // Open Recent. Not a registry command - each item is one project rather than
  // one command, and a menu of them is what File menus have always been for.
  // The burger drawer that used to hold this list was, apart from the recents,
  // a second copy of File and Help.
  const recents = useProjectStore.getState().recentProjects
  const recentMenu: MenuNode[] =
    recents.length === 0
      ? []
      : [
          { kind: 'separator' },
          {
            kind: 'submenu',
            label: t('welcome.recentProjects'),
            items: recents.slice(0, 10).map((project) => ({
              kind: 'command' as const,
              id: `${OPEN_RECENT}${project.path}`,
              label: project.name,
              enabled: true
            }))
          }
        ]

  return [
    { kind: 'submenu', label: t('menu.file'), items: [...items(FILE), ...recentMenu] },
    {
      kind: 'submenu',
      label: t('menu.edit'),
      items: [
        { kind: 'role', role: 'undo' },
        { kind: 'role', role: 'redo' },
        { kind: 'separator' },
        { kind: 'role', role: 'cut' },
        { kind: 'role', role: 'copy' },
        { kind: 'role', role: 'paste' },
        { kind: 'role', role: 'selectAll' }
      ]
    },
    { kind: 'submenu', label: t('menu.go'), items: go },
    {
      kind: 'submenu',
      label: t('menu.view'),
      items: [
        ...items(VIEW),
        { kind: 'separator' },
        { kind: 'role', role: 'reload' },
        { kind: 'role', role: 'toggleDevTools' },
        { kind: 'role', role: 'togglefullscreen' }
      ]
    },
    { kind: 'submenu', label: t('menu.help'), items: items(HELP) }
  ]
}
