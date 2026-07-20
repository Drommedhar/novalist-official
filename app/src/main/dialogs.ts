import { dialog, ipcMain, BrowserWindow, shell, clipboard } from 'electron'
import { join, normalize, isAbsolute } from 'node:path'
import { pathToFileURL } from 'node:url'
import { currentProjectRoot } from './protocols'

/** Resolves a renderer-supplied path (project-relative or absolute) to an
 * absolute path inside the open project, guarding against traversal. */
function resolveProjectPath(target: string): string | null {
  if (isAbsolute(target)) return target
  const root = currentProjectRoot()
  if (!root) return null
  const resolved = normalize(join(root, target))
  return resolved.startsWith(normalize(root)) ? resolved : null
}

/** Native file/folder pickers, exposed to the renderer through the preload bridge. */
export function registerDialogHandlers(): void {
  ipcMain.handle('novalist:open-external', async (_event, target: string) => {
    if (/^https?:\/\//i.test(target)) {
      await shell.openExternal(target)
      return true
    }
    const resolved = resolveProjectPath(target)
    if (!resolved) return false
    await shell.openExternal(pathToFileURL(resolved).toString())
    return true
  })

  ipcMain.handle('novalist:reveal-path', (_event, target: string) => {
    const resolved = resolveProjectPath(target)
    if (!resolved) return false
    shell.showItemInFolder(resolved)
    return true
  })

  ipcMain.on('novalist:copy-text', (_event, text: string) => {
    clipboard.writeText(text)
  })

  ipcMain.handle('novalist:read-clipboard-image', async () => {
    const image = clipboard.readImage()
    if (image.isEmpty()) return null
    const { tmpdir } = await import('node:os')
    const { writeFile } = await import('node:fs/promises')
    const file = join(tmpdir(), `novalist-clip-${process.hrtime.bigint()}.png`)
    await writeFile(file, image.toPNG())
    return file
  })

  ipcMain.handle('novalist:pick-folder', async (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    const result = await dialog.showOpenDialog(win!, {
      title,
      properties: ['openDirectory', 'createDirectory']
    })
    return result.canceled ? null : result.filePaths[0]
  })

  ipcMain.handle('novalist:pick-file', async (event, title: string, mode?: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    const result = await dialog.showOpenDialog(win!, {
      title,
      properties: ['openFile'],
      filters:
        mode === 'all'
          ? [{ name: 'All files', extensions: ['*'] }]
          : [{ name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif'] }]
    })
    return result.canceled ? null : result.filePaths[0]
  })

  ipcMain.handle('novalist:save-file', async (event, defaultName: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    const result = await dialog.showSaveDialog(win!, { defaultPath: defaultName })
    return result.canceled ? null : result.filePath
  })
}
