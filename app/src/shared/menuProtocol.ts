/** Menu descriptions exchanged over the main/renderer IPC boundary. */
export type MenuNode =
  | { kind: 'separator' }
  | { kind: 'role'; role: string; label: string }
  | {
      kind: 'command'
      id: string
      label: string
      /** Displayed only; the renderer dispatches keyboard shortcuts. */
      accelerator?: string
      enabled: boolean
    }
  | { kind: 'submenu'; label: string; items: MenuNode[] }

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
