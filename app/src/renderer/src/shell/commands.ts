import { DEFAULT_HOME, type CommandContainer, type CommandDef } from './commandTypes'
import { EDITOR_COMMANDS } from './editorCommands'
import { PROJECT_COMMANDS } from './projectCommands'
import type { MainView } from '../stores/shellStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { shell, project, projectOpen } from './commandContext'
import { popOut } from './PaneHeader'
import { printCurrentView } from './printView'


export { DEFAULT_HOME } from './commandTypes'
export type { CommandContainer, CommandDef, CommandScope } from './commandTypes'

/**
 * Every view, in the order the modes hold them. Navigation is
 * application-scoped: which destination is in front of the writer is a fact
 * about the window rather than about the book.
 */
const NAV_GESTURES: Partial<Record<MainView, string>> = {
  write: 'Ctrl+D1',
  dashboard: 'Ctrl+D2',
  timeline: 'Ctrl+D3',
  codex: 'Ctrl+D4',
  manuscript: 'Ctrl+D5',
  calendar: 'Ctrl+D6',
  relationships: 'Ctrl+D7',
  plotGrid: 'Ctrl+D8',
  research: 'Ctrl+D9',
  // Not a digit: the run of nine is the writer's own ordering of the views they
  // move between all day, and inserting a tenth would renumber the lot.
  narration: 'Ctrl+Alt+R'
}

const NAV_VIEWS: MainView[] = [
  'write',
  'dashboard',
  'manuscript',
  'narration',
  'timeline',
  'plotGrid',
  'calendar',
  'relationships',
  'dialogue',
  'canvas',
  'series',
  'codex',
  'wiki',
  'maps',
  'languages',
  'research',
  'gallery',
  'expose',
  'export',
  'style',
  'git',
  'extensions',
  'settings'
]

/** Views that mean something with no project open. */
const VIEWS_WITHOUT_PROJECT = new Set<MainView>(['settings', 'extensions'])

const NAV_COMMANDS: CommandDef[] = NAV_VIEWS.map((view) => ({
  id: `nav.${view}`,
  labelKey: `shell.view.${view}`,
  categoryKey: 'hotkeys.category.navigation',
  scope: 'application' as const,
  ...(NAV_GESTURES[view] ? { defaultGesture: NAV_GESTURES[view] } : {}),
  available: () => projectOpen() || VIEWS_WITHOUT_PROJECT.has(view),
  run: () => (view === 'settings' ? shell().openSettings() : shell().setMainView(view))
}))

const APPLICATION_COMMANDS: CommandDef[] = [
  /* ── Application: the menu bar ──────────────────────────────────────── */
  ...NAV_COMMANDS,
  {
    id: 'app.commandPalette',
    labelKey: 'commandPalette.placeholder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+Shift+P',
    run: () => shell().setCommandPaletteOpen(true)
  },
  {
    id: 'app.quickOpen',
    labelKey: 'quickOpen.placeholder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+P',
    available: projectOpen,
    run: () => shell().setQuickOpenOpen(true)
  },
  {
    // Everything from here down shapes the workspace, and with no project open
    // there is no workspace on screen to shape - so these were menu items that
    // looked live and did nothing when pressed.
    id: 'app.print',
    labelKey: 'print.title',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // Ctrl+P is Quick Open here and has been since before there was anything
    // to print, so moving it would cost more than it is worth.
    defaultGesture: 'Ctrl+Alt+P',
    available: projectOpen,
    run: printCurrentView
  },
  {
    id: 'app.toggleBinder',
    labelKey: 'shell.toggleBinder',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    // Ctrl+B belongs to bold. The two panels keep the same shape of gesture as
    // each other so they stay one thing to remember rather than two.
    defaultGesture: 'Ctrl+Alt+B',
    available: projectOpen,
    run: () => shell().toggleBinder()
  },
  {
    id: 'app.toggleInspector',
    labelKey: 'shell.toggleInspector',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+I',
    available: projectOpen,
    run: () => shell().toggleInspector()
  },
  {
    id: 'app.toggleModePanel',
    labelKey: 'modes.togglePanel',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().toggleModePanelVisible()
  },
  {
    id: 'app.toggleSceneNotes',
    labelKey: 'shell.toggleSceneNotes',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Shift+N',
    available: projectOpen,
    run: () => shell().toggleNotesDock()
  },
  {
    id: 'app.focusMode',
    labelKey: 'menu.focusMode',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'F11',
    available: projectOpen,
    run: () => shell().toggleFocusMode()
  },
  {
    id: 'app.splitRight',
    labelKey: 'panes.splitRight',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+ArrowRight',
    available: projectOpen,
    run: () => shell().splitActivePane('row')
  },
  {
    id: 'app.splitDown',
    labelKey: 'panes.splitDown',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+ArrowDown',
    available: projectOpen,
    run: () => shell().splitActivePane('column')
  },
  {
    id: 'app.closePane',
    labelKey: 'panes.close',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+W',
    available: projectOpen,
    run: () => shell().closeActivePane()
  },
  {
    id: 'app.resetPanes',
    labelKey: 'panes.defaultLayout',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().resetPanes()
  },
  {
    id: 'app.popOut',
    labelKey: 'panes.popOut',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => void popOut(shell().mainView)
  },
  {
    id: 'app.paneLayouts',
    labelKey: 'panes.layouts',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().openDialog('paneLayouts')
  },
  {
    id: 'app.layouts',
    labelKey: 'layouts.title',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+L',
    available: projectOpen,
    run: () => shell().setLayoutsOpen(true)
  },
  {
    // These three were accelerators the main process registered itself. The
    // menu bar shows gestures now but does not bind them - the renderer is the
    // only dispatcher - so they have to be real bindings here or the keys stop
    // working, which is exactly what happened.
    id: 'app.uiScaleIncrease',
    labelKey: 'command.uiScaleIncrease',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Plus',
    run: () => useUiScaleStore.getState().increase()
  },
  {
    id: 'app.uiScaleDecrease',
    labelKey: 'command.uiScaleDecrease',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Minus',
    run: () => useUiScaleStore.getState().decrease()
  },
  {
    id: 'app.uiScaleReset',
    labelKey: 'command.uiScaleReset',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+D0',
    run: () => useUiScaleStore.getState().reset()
  },
  {
    id: 'app.tour',
    labelKey: 'tour.title',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+T',
    available: projectOpen,
    run: () => shell().setTourOpen(true)
  },
  {
    id: 'app.manual',
    labelKey: 'command.manual',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // F1 was an accelerator the Help menu registered. The menu bar no longer
    // binds anything, so the manual's only keyboard route lives here.
    defaultGesture: 'F1',
    run: () => shell().setHelpOpen(true)
  },
  {
    id: 'app.newProject',
    labelKey: 'welcome.newProject',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => shell().openDialog('createProject')
  },
  {
    id: 'app.openProject',
    labelKey: 'welcome.browseFolder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => void project().pickAndOpenProject()
  },
  {
    id: 'app.closeProject',
    labelKey: 'command.closeProject',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    available: projectOpen,
    run: () => void project().closeProject()
  },
  {
    id: 'app.importProject',
    labelKey: 'welcome.importPlugin',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => shell().openDialog('importPlugin')
  },
  {
    id: 'app.restoreBackup',
    labelKey: 'backup.restoreAsNew',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => shell().openDialog('restoreBackup')
  },
  {
    id: 'app.importManuscript', // aislop-ignore-line code-quality/duplicate-block -- Import commands have distinct IDs and dialogs; repeated registry fields are declarative metadata.
    labelKey: 'manuscriptImport.action',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    available: projectOpen,
    run: () => shell().openDialog('importManuscript')
  },
  {
    id: 'app.importFolder',
    labelKey: 'folderImport.action',
    categoryKey: 'hotkeys.category.general', // aislop-ignore-line code-quality/duplicate-block -- Import commands have distinct IDs and dialogs; repeated registry fields are declarative metadata.
    scope: 'application',
    available: projectOpen,
    run: () => shell().openDialog('importFolder')
  },
  {
    id: 'app.about',
    labelKey: 'command.about',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // Reachable with nothing open: the version, the licences and the changelog
    // are facts about the installation rather than about a book.
    run: () => shell().setMainView('about')
  }
]

export const COMMANDS: CommandDef[] = [
  ...EDITOR_COMMANDS,
  ...PROJECT_COMMANDS,
  ...APPLICATION_COMMANDS
]

/** Where this command actually lives. */
export function homeOf(command: CommandDef): CommandContainer {
  return 'home' in command ? command.home : DEFAULT_HOME[command.scope]
}

/** Every command whose one persistent home is this container, in registry order. */
export function commandsIn(container: CommandContainer): CommandDef[] {
  return COMMANDS.filter((command) => homeOf(command) === container)
}

const BY_ID = new Map(COMMANDS.map((command) => [command.id, command]))

export function commandById(id: string): CommandDef | undefined {
  return BY_ID.get(id)
}

export function runCommand(id: string): void {
  if (project().closingProject || project().changingSceneStructure || project().workspaceBusy) return
  BY_ID.get(id)?.run()
}
