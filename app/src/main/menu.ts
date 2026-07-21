import { app, BrowserWindow, Menu, shell, type MenuItemConstructorOptions } from 'electron'

// Mac App Store build: the store delivers updates and self-update is disabled, so
// the manual "Check for Updates…" item is omitted there.
const isMas = (process as NodeJS.Process & { mas?: boolean }).mas === true

/** Relays a menu command to the renderer, which maps it onto the shell store. */
function sendCommand(command: string): void {
  const win = BrowserWindow.getFocusedWindow() ?? BrowserWindow.getAllWindows()[0]
  win?.webContents.send('novalist:menu-command', command)
}

const nav = (label: string, view: string, accelerator?: string): MenuItemConstructorOptions => ({
  label,
  accelerator,
  click: () => sendCommand(`nav:${view}`)
})

/**
 * Installs a real application menu so the app has an About/Quit item (Cmd+Q on
 * macOS), standard Edit/View/Window roles, a Go menu that mirrors the activity
 * bar (so every view is reachable from the menu bar too), panel toggles, and no
 * leftover Electron defaults.
 */
export function installAppMenu(): void {
  const isMac = process.platform === 'darwin'
  const name = app.name

  const template: MenuItemConstructorOptions[] = [
    ...(isMac
      ? [
          {
            label: name,
            submenu: [
              { role: 'about' as const, label: `About ${name}` },
              { type: 'separator' as const },
              { role: 'hide' as const, label: `Hide ${name}` },
              { role: 'hideOthers' as const },
              { role: 'unhide' as const },
              { type: 'separator' as const },
              { role: 'quit' as const, label: `Quit ${name}` }
            ]
          }
        ]
      : []),
    {
      label: 'File',
      submenu: isMac
        ? [{ role: 'close' as const }]
        : [{ role: 'quit' as const, label: `Exit ${name}` }]
    },
    {
      label: 'Edit',
      submenu: [
        { role: 'undo' as const },
        { role: 'redo' as const },
        { type: 'separator' as const },
        { role: 'cut' as const },
        { role: 'copy' as const },
        { role: 'paste' as const },
        { role: 'selectAll' as const }
      ]
    },
    {
      label: 'Go',
      submenu: [
        nav('Editor', 'write', 'CmdOrCtrl+1'),
        nav('Dashboard', 'dashboard', 'CmdOrCtrl+2'),
        nav('Manuscript', 'manuscript', 'CmdOrCtrl+5'),
        { type: 'separator' as const },
        nav('Timeline', 'timeline', 'CmdOrCtrl+3'),
        nav('Plot Grid', 'plotGrid', 'CmdOrCtrl+8'),
        nav('Calendar', 'calendar', 'CmdOrCtrl+6'),
        nav('Relationships', 'relationships', 'CmdOrCtrl+7'),
        { type: 'separator' as const },
        nav('Codex', 'codex', 'CmdOrCtrl+4'),
        nav('Maps', 'maps'),
        nav('Research', 'research', 'CmdOrCtrl+9'),
        nav('Gallery', 'gallery'),
        { type: 'separator' as const },
        nav('Export', 'export'),
        nav('Git', 'git'),
        nav('Extensions', 'extensions'),
        nav('Settings', 'settings')
      ]
    },
    {
      label: 'View',
      submenu: [
        {
          label: 'Toggle Binder',
          accelerator: 'CmdOrCtrl+B',
          click: () => sendCommand('toggle:binder')
        },
        {
          label: 'Toggle Context Sidebar',
          accelerator: 'CmdOrCtrl+Shift+B',
          click: () => sendCommand('toggle:inspector')
        },
        {
          label: 'Toggle Scene Notes',
          accelerator: 'CmdOrCtrl+Shift+N',
          click: () => sendCommand('toggle:sceneNotes')
        },
        {
          label: 'Focus Mode',
          accelerator: 'Alt+F',
          click: () => sendCommand('toggle:focus')
        },
        { type: 'separator' as const },
        { role: 'reload' as const },
        { role: 'toggleDevTools' as const },
        { type: 'separator' as const },
        { role: 'resetZoom' as const },
        { role: 'zoomIn' as const },
        { role: 'zoomOut' as const },
        { type: 'separator' as const },
        { role: 'togglefullscreen' as const }
      ]
    },
    {
      label: 'Window',
      submenu: [
        { role: 'minimize' as const },
        { role: 'zoom' as const },
        ...(isMac
          ? [
              { type: 'separator' as const },
              { role: 'front' as const },
              { type: 'separator' as const },
              { role: 'window' as const }
            ]
          : [{ role: 'close' as const }])
      ]
    },
    {
      role: 'help',
      submenu: [
        {
          label: 'Novalist Manual',
          accelerator: 'F1',
          click: () => sendCommand('help:manual')
        },
        ...(isMas
          ? []
          : [
              {
                label: 'Check for Updates…',
                click: () => sendCommand('help:checkUpdates')
              }
            ]),
        { type: 'separator' as const },
        {
          label: 'Novalist on GitHub',
          click: () => void shell.openExternal('https://github.com/Drommedhar/novalist-official')
        }
      ]
    }
  ]

  Menu.setApplicationMenu(Menu.buildFromTemplate(template))
}
