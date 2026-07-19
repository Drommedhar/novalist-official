import { app, Menu, shell, type MenuItemConstructorOptions } from 'electron'

/**
 * Installs a real application menu so the app has an About/Quit item (Cmd+Q on
 * macOS), standard Edit/View/Window roles, and no leftover Electron defaults.
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
      label: 'View',
      submenu: [
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
          label: 'Novalist on GitHub',
          click: () => void shell.openExternal('https://github.com/Drommedhar/novalist-official')
        }
      ]
    }
  ]

  Menu.setApplicationMenu(Menu.buildFromTemplate(template))
}
