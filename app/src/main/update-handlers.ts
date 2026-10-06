import { app, BrowserWindow, ipcMain } from 'electron'
import { randomUUID } from 'node:crypto'
import type { WindowCloseCoordinator } from './window-close'
import {
  checkAppUpdate,
  downloadAppUpdate,
  launchAppUpdate,
  type DownloadedAppUpdate
} from './appUpdater'

export function registerUpdateHandlers(
  getMainWindow: () => BrowserWindow | null,
  windowCloses: WindowCloseCoordinator,
  approvedCloses: Set<number>
): void {
  // App self-update (GitHub release → download installer → open). Extension
  // updates are handled separately by the renderer via the extension store.
  // Disabled in the Mac App Store build: Apple prohibits self-updating, so even a
  // manual trigger must do nothing there (updates arrive via the App Store).
  const isMasBuild = (process as NodeJS.Process & { mas?: boolean }).mas === true
  const pendingAppUpdates = new Map<
    number,
    { token: string; update: DownloadedAppUpdate }
  >()
  ipcMain.handle('novalist:check-app-update', () => (isMasBuild ? null : checkAppUpdate()))
  ipcMain.handle('novalist:has-detached-panes', () =>
    BrowserWindow.getAllWindows().some((candidate) => candidate !== getMainWindow() && !candidate.isDestroyed())
  )
  ipcMain.handle('novalist:download-app-update', async (event, info) => {
    if (isMasBuild) throw new Error('Self-update is disabled in the App Store build.')
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) throw new Error('No window for update download.')
    const update = await downloadAppUpdate(info, win)
    if (!update.handoff) {
      pendingAppUpdates.delete(event.sender.id)
      return { filePath: update.filePath, launchToken: null }
    }
    const token = randomUUID()
    pendingAppUpdates.set(event.sender.id, { token, update })
    return { filePath: update.filePath, launchToken: token }
  })
  ipcMain.handle('novalist:launch-app-update', async (event, token: string) => {
    if (isMasBuild) throw new Error('Self-update is disabled in the App Store build.')
    const pending = pendingAppUpdates.get(event.sender.id)
    if (!pending || typeof token !== 'string' || pending.token !== token) {
      throw new Error('The downloaded update is no longer available. Download it again.')
    }
    // A launch token is one-shot. A failed OS handoff leaves Novalist open, and
    // retrying through the dialog obtains a fresh token for the cached download.
    pendingAppUpdates.delete(event.sender.id)
    const owners = BrowserWindow.getAllWindows().filter((win) => !win.isDestroyed()).map((win) => win.webContents.id)
    await windowCloses.prepare(owners, getMainWindow()?.webContents.id)
    try { await launchAppUpdate(pending.update) }
    catch (error) { windowCloses.abort(owners); throw error }
    // This authenticated handoff already awaited the renderer's save and backup
    // preflight while editing was locked. Do not start that work a second time.
    for (const target of BrowserWindow.getAllWindows()) approvedCloses.add(target.id)
    // Launch acknowledgement and shutdown are one main-process operation. In
    // particular, a spawned Linux helper must never be left waiting because its
    // renderer disappeared before it could send a second, unauthenticated IPC.
    app.quit()
  })

}
