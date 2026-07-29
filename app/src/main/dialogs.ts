import { dialog, ipcMain, BrowserWindow, shell, clipboard } from 'electron'
import { writeFile } from 'node:fs/promises'
import { join, normalize, isAbsolute } from 'node:path'
import { pathToFileURL } from 'node:url'
import { currentProjectRoot } from './protocols'
import { saveBookmark, beginAccess, endAccess } from './mac-bookmarks'

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
      properties: ['openDirectory', 'createDirectory'],
      // Only has effect in the Mac App Store build; ignored elsewhere. Lets us
      // reopen this folder on a later launch under the sandbox.
      securityScopedBookmarks: true
    })
    if (result.canceled || result.filePaths.length === 0) return null
    const picked = result.filePaths[0]
    saveBookmark(picked, result.bookmarks?.[0])
    return picked
  })

  // Begin/end security-scoped access to a previously-picked project folder.
  // Off the Mac App Store build these are trivial (always granted); on MAS they
  // resolve the stored bookmark so the backend can read/write a project reopened
  // from a stored path. Returning false lets the renderer re-prompt for access.
  ipcMain.handle('novalist:begin-project-access', (_event, path: string) => beginAccess(path))
  ipcMain.on('novalist:end-project-access', (_event, path: string) => endAccess(path))

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

  /**
   * Captures a rectangle of the window to a PNG.
   *
   * The 2D map is a DOM tree with overlays and an SVG border rather than one
   * canvas, so there is nothing to call toDataURL on. Electron's own capture
   * sees exactly what the writer sees, needs no rasterising dependency, and
   * works identically for the 3D view.
   *
   * `scale` multiplies the captured pixels, so a map can leave at a resolution
   * fit for endpapers rather than at whatever size the window happened to be.
   */
  ipcMain.handle(
    'novalist:capture-region',
    async (
      event,
      rect: { x: number; y: number; width: number; height: number },
      outputPath: string,
      scale: number
    ) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win) return false

      const bounds = {
        x: Math.max(0, Math.round(rect.x)),
        y: Math.max(0, Math.round(rect.y)),
        width: Math.max(1, Math.round(rect.width)),
        height: Math.max(1, Math.round(rect.height))
      }

      try {
        const image = await win.webContents.capturePage(bounds)
        const factor = Math.max(1, Math.min(8, Math.round(scale || 1)))
        const sized =
          factor === 1
            ? image
            : image.resize({
                width: bounds.width * factor,
                height: bounds.height * factor,
                quality: 'best'
              })
        await writeFile(outputPath, sized.toPNG())
        return true
      } catch {
        // A capture that fails must not take the view down with it.
        return false
      }
    }
  )
}
