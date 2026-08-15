import { app, BrowserWindow, Menu, shell, type MenuItemConstructorOptions } from 'electron'

// Mac App Store build: the store delivers updates and self-update is disabled, so
// the manual "Check for Updates…" item is omitted there.
const isMas = (process as NodeJS.Process & { mas?: boolean }).mas === true

/** Relays a menu command to the renderer, which runs it through the registry. */
function sendCommand(command: string): void {
  const win = BrowserWindow.getFocusedWindow() ?? BrowserWindow.getAllWindows()[0]
  win?.webContents.send('novalist:menu-command', command)
}

/**
 * A node in the menu the renderer describes. Mirrors `MenuNode` in
 * shell/menuLayout.ts; the two sit on opposite sides of an IPC boundary, so
 * they are two declarations of one contract rather than one shared type.
 */
export type MenuNode =
  | { kind: 'separator' }
  | { kind: 'role'; role: string; label: string }
  | { kind: 'command'; id: string; label: string; accelerator?: string; enabled: boolean }
  | { kind: 'submenu'; label: string; items: MenuNode[] }

/**
 * Labels for the menus this side builds. Mirrors `MenuLabels` in menuLayout.ts.
 *
 * The behaviour of Window, quitting, the updater and About is the platform's
 * rather than Novalist's, so it stays here - but the words are the interface's,
 * and an English "Window" beside a German "Datei" is the sort of thing that
 * reads as an unfinished app.
 */
export interface MenuLabels {
  window: string
  mainWindow: string
  minimize: string
  zoom: string
  closeWindow: string
  front: string
  windowList: string
  quit: string
  about: string
  hide: string
  hideOthers: string
  unhide: string
  checkUpdates: string
  github: string
}

/** What the menu says before the renderer has told us the writer's language. */
const BOOTSTRAP: MenuLabels = {
  window: 'Window',
  mainWindow: 'Main Window',
  minimize: 'Minimise',
  zoom: 'Zoom',
  closeWindow: 'Close Window',
  front: 'Bring All to Front',
  windowList: 'Window',
  quit: 'Quit',
  about: 'About',
  hide: 'Hide',
  hideOthers: 'Hide Others',
  unhide: 'Show All',
  checkUpdates: 'Check for Updates…',
  github: 'Novalist on GitHub'
}

let showMainWindowFn: () => void = () => {}

function toItem(node: MenuNode): MenuItemConstructorOptions {
  if (node.kind === 'separator') return { type: 'separator' }
  if (node.kind === 'role') {
    return { role: node.role as MenuItemConstructorOptions['role'], label: node.label }
  }
  if (node.kind === 'submenu') return { label: node.label, submenu: node.items.map(toItem) }
  return {
    label: node.label,
    accelerator: node.accelerator,
    // Shown but not bound. The renderer already listens for every gesture,
    // including inside the editor iframe, and a gesture registered here as
    // well would either fire twice or take the key away from the half that
    // knows what the writer has rebound it to.
    registerAccelerator: false,
    enabled: node.enabled,
    click: () => sendCommand(node.id)
  }
}

/** The macOS application menu. Nothing in it is Novalist's own command. */
function appMenu(name: string, labels: MenuLabels): MenuItemConstructorOptions[] {
  return process.platform === 'darwin'
    ? [
        {
          label: name,
          submenu: [
            { role: 'about', label: `${labels.about} ${name}` },
            { type: 'separator' },
            { role: 'hide', label: `${labels.hide} ${name}` },
            { role: 'hideOthers', label: labels.hideOthers },
            { role: 'unhide', label: labels.unhide },
            { type: 'separator' },
            { role: 'quit', label: `${labels.quit} ${name}` }
          ]
        }
      ]
    : []
}

/** The Window menu, which is the platform's rather than the app's. */
function windowMenu(name: string, labels: MenuLabels): MenuItemConstructorOptions {
  const isMac = process.platform === 'darwin'
  return {
    label: labels.window,
    submenu: [
      // The way back to the project after its window has been closed. The
      // window list below only names windows that exist, and closing the last
      // one does not quit on macOS, so without this the app can be running
      // with nothing to show and no menu item that brings it back.
      { label: `${name} ${labels.mainWindow}`, click: () => showMainWindowFn() },
      { type: 'separator' },
      { role: 'minimize', label: labels.minimize },
      { role: 'zoom', label: labels.zoom },
      ...(isMac
        ? ([
            { type: 'separator' },
            { role: 'front', label: labels.front },
            { type: 'separator' },
            { role: 'window', label: labels.windowList }
          ] as MenuItemConstructorOptions[])
        : ([{ role: 'close', label: labels.closeWindow }] as MenuItemConstructorOptions[]))
    ]
  }
}

/** Quit or Close, appended to whatever File the renderer described. */
function fileTail(name: string, labels: MenuLabels): MenuItemConstructorOptions[] {
  return process.platform === 'darwin'
    ? [{ type: 'separator' }, { role: 'close', label: labels.closeWindow }]
    : [{ type: 'separator' }, { role: 'quit', label: `${labels.quit} ${name}` }]
}

/** Update and support items, appended to Help. */
function helpTail(labels: MenuLabels): MenuItemConstructorOptions[] {
  return [
    { type: 'separator' },
    ...(isMas
      ? []
      : [
          {
            label: labels.checkUpdates,
            click: () => sendCommand('help:checkUpdates')
          } as MenuItemConstructorOptions
        ]),
    {
      label: labels.github,
      click: () => void shell.openExternal('https://github.com/Drommedhar/novalist-official')
    }
  ]
}

/**
 * Installs the menu bar the renderer described.
 *
 * The renderer owns the content because the command registry does: an
 * application-scoped command belongs in the menu bar by definition, and a
 * hand-written template beside a registry is a second list that drifts. What
 * the main process keeps is the part that is the platform's rather than
 * Novalist's - the macOS application menu, the Window menu, quit and close, and
 * the roles that only Electron can implement - and even those take their words
 * from the interface's own language.
 */
export function applyMenuTemplate(nodes: MenuNode[], labels: MenuLabels = BOOTSTRAP): void {
  const name = app.name
  const described = nodes.map(toItem)
  // The renderer describes File, Edit, Go, View and Help in that order; Window
  // and the platform's own items are this side's.
  const help = described.pop()
  const file = described[0]
  if (file && Array.isArray(file.submenu)) file.submenu.push(...fileTail(name, labels))

  const template: MenuItemConstructorOptions[] = [
    ...appMenu(name, labels),
    ...described,
    windowMenu(name, labels)
  ]
  if (help && Array.isArray(help.submenu)) {
    help.role = 'help'
    help.submenu.push(...helpTail(labels))
    template.push(help)
  }

  Menu.setApplicationMenu(Menu.buildFromTemplate(template))
}

/**
 * The menu bar before the renderer has booted.
 *
 * A window that opens with no menu at all and grows one a second later reads as
 * a glitch, and on macOS the application menu has to exist from the first
 * frame. So the platform's own items are installed immediately, in English,
 * and the renderer's are folded in - in the writer's language - as soon as it
 * can describe them.
 */
export function installAppMenu(showMainWindow: () => void = () => {}): void {
  showMainWindowFn = showMainWindow
  const name = app.name
  Menu.setApplicationMenu(
    Menu.buildFromTemplate([
      ...appMenu(name, BOOTSTRAP),
      { label: 'File', submenu: fileTail(name, BOOTSTRAP).slice(1) },
      {
        label: 'Edit',
        submenu: [
          { role: 'undo' },
          { role: 'redo' },
          { type: 'separator' },
          { role: 'cut' },
          { role: 'copy' },
          { role: 'paste' },
          { role: 'selectAll' }
        ]
      },
      windowMenu(name, BOOTSTRAP),
      { role: 'help', submenu: helpTail(BOOTSTRAP).slice(1) }
    ])
  )
}
