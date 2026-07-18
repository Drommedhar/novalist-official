import { BrowserWindow } from 'electron'

/**
 * Update check via electron-updater (GitHub feed). On macOS the app is
 * unsigned, so updates notify and link to the release rather than
 * auto-installing - matching the previous UpdateService behavior.
 */
export function checkForUpdates(win: BrowserWindow): void {
  void import('electron-updater')
    .then(({ autoUpdater }) => {
      autoUpdater.autoDownload = process.platform !== 'darwin'
      autoUpdater.on('update-available', (info) => {
        win.webContents.send('novalist:update-available', info.version)
      })
      autoUpdater.on('error', (error) => {
        console.error('[updater]', error.message)
      })
      void autoUpdater.checkForUpdates()
    })
    .catch((error: unknown) => console.error('[updater] unavailable:', error))
}
