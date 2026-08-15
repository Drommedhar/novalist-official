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
  | { kind: 'role'; role: string }
  | { kind: 'command'; id: string; label: string; accelerator?: string; enabled: boolean }
  | { kind: 'submenu'; label: string; items: MenuNode[] }

let showMainWindowFn: () => void = () => {}

function toItem(node: MenuNode): MenuItemConstructorOptions {
  if (node.kind === 'separator') return { type: 'separator' }
  if (node.kind === 'role') return { role: node.role as MenuItemConstructorOptions['role'] }
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
function appMenu(name: string): MenuItemConstructorOptions[] {
  return process.platform === 'darwin'
    ? [
        {
          label: name,
          submenu: [
            { role: 'about', label: `About ${name}` },
            { type: 'separator' },
            { role: 'hide', label: `Hide ${name}` },
            { role: 'hideOthers' },
            { role: 'unhide' },
            { type: 'separator' },
            { role: 'quit', label: `Quit ${name}` }
          ]
        }
      ]
    : []
}

/** The Window menu, which is the platform's rather than the app's. */
function windowMenu(name: string): MenuItemConstructorOptions {
  const isMac = process.platform === 'darwin'
  return {
    label: 'Window',
    submenu: [
      // The way back to the project after its window has been closed. The
      // window list below only names windows that exist, and closing the last
      // one does not quit on macOS, so without this the app can be running
      // with nothing to show and no menu item that brings it back.
      { label: `${name} Window`, click: () => showMainWindowFn() },
      { type: 'separator' },
      { role: 'minimize' },
      { role: 'zoom' },
      ...(isMac
        ? ([
            { type: 'separator' },
            { role: 'front' },
            { type: 'separator' },
            { role: 'window' }
          ] as MenuItemConstructorOptions[])
        : ([{ role: 'close' }] as MenuItemConstructorOptions[]))
    ]
  }
}

/** Quit or Close, appended to whatever File the renderer described. */
function fileTail(name: string): MenuItemConstructorOptions[] {
  return process.platform === 'darwin'
    ? [{ type: 'separator' }, { role: 'close' }]
    : [{ type: 'separator' }, { role: 'quit', label: `Exit ${name}` }]
}

/** Update and support items, appended to Help. */
function helpTail(): MenuItemConstructorOptions[] {
  return [
    { type: 'separator' },
    ...(isMas
      ? []
      : [
          {
            label: 'Check for Updates…',
            click: () => sendCommand('help:checkUpdates')
          } as MenuItemConstructorOptions
        ]),
    {
      label: 'Novalist on GitHub',
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
 * the roles that only Electron can implement.
 */
export function applyMenuTemplate(nodes: MenuNode[]): void {
  const name = app.name
  const described = nodes.map(toItem)
  // The renderer describes File, Edit, Go, View and Help in that order; Window
  // and the platform's own items are this side's.
  const help = described.pop()
  const file = described[0]
  if (file && Array.isArray(file.submenu)) file.submenu.push(...fileTail(name))

  const template: MenuItemConstructorOptions[] = [...appMenu(name), ...described, windowMenu(name)]
  if (help && Array.isArray(help.submenu)) {
    help.role = 'help'
    help.submenu.push(...helpTail())
    template.push(help)
  }

  Menu.setApplicationMenu(Menu.buildFromTemplate(template))
}

/**
 * The menu bar before the renderer has booted.
 *
 * A window that opens with no menu at all and grows one a second later reads as
 * a glitch, and on macOS the application menu has to exist from the first
 * frame. So the platform's own items are installed immediately and the
 * renderer's are folded in as soon as it can describe them.
 */
export function installAppMenu(showMainWindow: () => void = () => {}): void {
  showMainWindowFn = showMainWindow
  const name = app.name
  Menu.setApplicationMenu(
    Menu.buildFromTemplate([
      ...appMenu(name),
      { label: 'File', submenu: fileTail(name).slice(1) },
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
      windowMenu(name),
      { role: 'help', submenu: helpTail().slice(1) }
    ])
  )
}
