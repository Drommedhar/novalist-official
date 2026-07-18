import { dialog, ipcMain, BrowserWindow } from 'electron'

/** Native file/folder pickers, exposed to the renderer through the preload bridge. */
export function registerDialogHandlers(): void {
  ipcMain.handle('novalist:pick-folder', async (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    const result = await dialog.showOpenDialog(win!, {
      title,
      properties: ['openDirectory', 'createDirectory']
    })
    return result.canceled ? null : result.filePaths[0]
  })
}
