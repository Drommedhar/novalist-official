import type { BrowserWindow } from 'electron'

interface WindowState {
  fullScreen: boolean
  menuVisible: boolean
  menuAutoHide: boolean
}

/** Serialize native transitions (asynchronous on macOS), including rapid F11 presses. */
export function installFocusWindow(win: BrowserWindow): (enabled: boolean) => Promise<void> {
  let previous: WindowState | null = null
  let pending = Promise.resolve()
  const fullScreen = async (enabled: boolean): Promise<void> => {
    if (win.isDestroyed() || win.isFullScreen() === enabled) return
    await new Promise<void>((resolve) => {
      const done = (): void => {
        win.removeListener('enter-full-screen', done)
        win.removeListener('leave-full-screen', done)
        win.removeListener('closed', done)
        resolve()
      }
      if (enabled) win.once('enter-full-screen', done)
      else win.once('leave-full-screen', done)
      win.once('closed', done)
      win.setFullScreen(enabled)
    })
  }
  const setFocus = (enabled: boolean): Promise<void> => {
    pending = pending.then(async () => {
      if (win.isDestroyed()) return
      if (enabled) {
        if (previous) return
        previous = {
          fullScreen: win.isFullScreen(),
          menuVisible: win.isMenuBarVisible(),
          menuAutoHide: win.isMenuBarAutoHide()
        }
        if (process.platform !== 'darwin') {
          win.setAutoHideMenuBar(true)
          win.setMenuBarVisibility(false)
        }
        await fullScreen(true)
      } else if (previous) {
        const restore = previous
        previous = null
        await fullScreen(restore.fullScreen)
        if (!win.isDestroyed() && process.platform !== 'darwin') {
          win.setAutoHideMenuBar(restore.menuAutoHide)
          win.setMenuBarVisibility(restore.menuVisible)
        }
      }
    })
    return pending
  }
  // A reload or crashed renderer must not strand a normal workspace in full screen.
  win.webContents.on('did-start-navigation', (_event, _url, inPlace, mainFrame) => {
    if (mainFrame && !inPlace) void setFocus(false)
  })
  win.webContents.on('render-process-gone', () => { void setFocus(false) })
  return setFocus
}
