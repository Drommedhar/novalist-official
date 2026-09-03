import {
  dialog,
  ipcMain,
  BrowserWindow,
  shell,
  clipboard,
  type OpenDialogOptions
} from 'electron'
import { writeFile } from 'node:fs/promises'
import { dirname, extname, join, normalize, isAbsolute } from 'node:path'
import { currentProjectRoot } from './protocols'
import { saveBookmark, beginAccess, endAccess } from './mac-bookmarks'
import {
  initializeManuscriptStaging,
  releaseStagedManuscript,
  stageManuscriptSource
} from './manuscript-staging'

const isSandboxedMas =
  (process.platform === 'darwin' &&
    (process as NodeJS.Process & { mas?: boolean }).mas === true) ||
  process.env.NOVALIST_FORCE_MAS === '1'

function manuscriptPickerProperties(): NonNullable<OpenDialogOptions['properties']> {
  // A sandboxed Mac must grant the whole Scrivener package, not only its
  // manifest, because the prose lives in sibling Files/Data or Files/Docs.
  // macOS can choose files and directories in one panel; Windows and Linux
  // cannot, so they open the package and choose its exact .scrivx instead.
  return isSandboxedMas
    ? ['openFile', 'openDirectory', 'treatPackageAsDirectory']
    : ['openFile', 'treatPackageAsDirectory']
}

interface FilePickerOptions {
  extensions?: unknown
  filterName?: unknown
  scrivenerAccessTitle?: unknown
}

type StagingFailureReason =
  | 'access-denied'
  | 'disk-full'
  | 'source-missing'
  | 'unsafe-link'
  | 'manifest-not-found'
  | 'manifest-ambiguous'
  | 'invalid-manifest'
  | 'invalid-project'
  | 'io'
  | 'other'

function stagingFailureReason(error: unknown): StagingFailureReason {
  const code =
    typeof error === 'object' && error !== null && 'code' in error
      ? String(error.code)
      : ''
  if (code === 'EACCES' || code === 'EPERM' || code === 'EROFS') return 'access-denied'
  if (code === 'ENOSPC' || code === 'EDQUOT') return 'disk-full'
  if (code === 'ENOENT' || code === 'ENOTDIR') return 'source-missing'
  if (code === 'ELOOP') return 'unsafe-link'
  if (code === 'NOVALIST_MANIFEST_NOT_FOUND') return 'manifest-not-found'
  if (code === 'NOVALIST_MANIFEST_AMBIGUOUS') return 'manifest-ambiguous'
  if (code === 'NOVALIST_INVALID_MANIFEST') return 'invalid-manifest'
  if (code.length > 0) return 'io'
  return error instanceof Error ? 'invalid-project' : 'other'
}

function manuscriptStagingError(
  stage: 'project' | 'source',
  error: unknown
): Error {
  const reason = stagingFailureReason(error)
  console.error(`[manuscript-import] staging failed stage=${stage} reason=${reason}.`)
  return new Error(`manuscript-staging-failed:${stage}:${reason}`)
}

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
  if (isSandboxedMas) {
    void initializeManuscriptStaging().catch((error: unknown) => {
      const reason = stagingFailureReason(error)
      console.error(`[manuscript-import] staging failed stage=prepare reason=${reason}.`)
    })
  }

  ipcMain.handle('novalist:open-external', async (_event, target: string) => {
    if (/^https?:\/\//i.test(target)) {
      await shell.openExternal(target)
      return true
    }
    const resolved = resolveProjectPath(target)
    if (!resolved) return false
    return (await shell.openPath(resolved)) === ''
  })

  ipcMain.handle('novalist:reveal-path', (_event, target: string) => {
    const resolved = resolveProjectPath(target)
    if (!resolved) return false
    shell.showItemInFolder(resolved)
    return true
  })

  /**
   * The clipboard is not the app's to spend under test.
   *
   * An e2e spec drove About's "Copy system information" and read the clipboard
   * back to check it, so every run of the suite silently threw away whatever
   * the person at the keyboard had copied - once, a page of notes taken while
   * the suite ran. Almost everything a test can damage is inside a temporary
   * directory; the system clipboard is not, and no amount of care in the specs
   * makes it so. So the refusal lives here, where a spec cannot forget it, and
   * what would have been copied is kept for the test to read instead.
   */
  ipcMain.on('novalist:copy-text', (_event, text: string) => {
    if (process.env.NOVALIST_NO_CLIPBOARD === '1') {
      ;(globalThis as unknown as { __copied?: string }).__copied = text
      return
    }
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
  ipcMain.handle('novalist:release-picked-file', (_event, path: string) =>
    typeof path === 'string' ? releaseStagedManuscript(path) : undefined
  )

  ipcMain.handle(
    'novalist:pick-file',
    async (
      event,
      title: string,
      mode?: string,
      options?: FilePickerOptions
    ) => {
      const manuscriptExtensions = Array.isArray(options?.extensions)
        ? [
            ...new Set(
              options.extensions
                .filter((extension): extension is string => typeof extension === 'string')
                .map((extension) => extension.trim().replace(/^\./, '').toLowerCase())
                .filter((extension) => /^[a-z0-9]+$/.test(extension))
            )
          ]
        : []
      const manuscriptFilterName =
        typeof options?.filterName === 'string' && options.filterName.trim().length > 0
          ? options.filterName.trim().slice(0, 80)
          : 'Manuscripts'
      const scrivenerAccessTitle =
        typeof options?.scrivenerAccessTitle === 'string' &&
        options.scrivenerAccessTitle.trim().length > 0
          ? options.scrivenerAccessTitle.trim().slice(0, 160)
          : title
      // The backend owns the readable-format list. Opening an unrestricted picker
      // while that list is unavailable would recreate the mismatch this mode is
      // meant to prevent.
      if (mode === 'manuscript' && manuscriptExtensions.length === 0) return null

      const win = BrowserWindow.fromWebContents(event.sender)
      const result = await dialog.showOpenDialog(win!, {
        title,
        properties: mode === 'manuscript' ? manuscriptPickerProperties() : ['openFile'],
        // Captures the package-wide grant when a sandboxed Mac selects a .scriv
        // package or a suffixless Scrivenix project directory.
        securityScopedBookmarks: mode === 'manuscript' && isSandboxedMas,
        filters:
          mode === 'all'
            ? [{ name: 'All files', extensions: ['*'] }]
            : mode === 'manuscript'
              ? [
                  {
                    name: manuscriptFilterName,
                    extensions: manuscriptExtensions
                  }
                ]
            : [{ name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif'] }]
      })
      if (result.canceled || result.filePaths.length === 0) return null

      const picked = result.filePaths[0]
      if (mode !== 'manuscript' || !isSandboxedMas) return picked

      // A direct .scrivx choice grants only that XML file under App Sandbox,
      // while importing it also needs its sibling payload. Ask for its exact
      // parent as an access grant, but return the original file so the reader
      // never guesses between manifests.
      if (extname(picked).toLowerCase() === '.scrivx') {
        const projectRoot = dirname(picked)
        const access = await dialog.showOpenDialog(win!, {
          title: scrivenerAccessTitle,
          defaultPath: dirname(projectRoot),
          properties: ['openDirectory', 'treatPackageAsDirectory'],
          securityScopedBookmarks: true
        })
        if (
          access.canceled ||
          access.filePaths.length === 0 ||
          normalize(access.filePaths[0]) !== normalize(projectRoot)
        ) {
          return null
        }
        try {
          return await stageManuscriptSource(picked, projectRoot)
        } catch (error) {
          throw manuscriptStagingError('project', error)
        }
      }

      try {
        return await stageManuscriptSource(picked)
      } catch (error) {
        throw manuscriptStagingError('source', error)
      }
    }
  )

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
