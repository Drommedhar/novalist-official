import { app, ipcMain, net, protocol } from 'electron'
import { pathToFileURL } from 'node:url'
import { join, normalize } from 'node:path'

let projectRoot: string | null = null
const extensionRoots = new Map<string, string>()

/**
 * novalist-project:// serves read-only files from the active project folder so
 * project images (and later map assets) load from a real origin on every OS.
 * The renderer announces the root after project/open succeeds.
 */
export function registerProtocolSchemes(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: 'novalist-project',
      privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true }
    },
    {
      scheme: 'novalist-ext',
      privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true }
    }
  ])
}

export function registerProtocolHandlers(): void {
  ipcMain.on('novalist:set-project-root', (_event, root: string | null) => {
    projectRoot = root
  })

  ipcMain.handle(
    'novalist:register-ext-roots',
    (_event, roots: Record<string, string>) => {
      // Merged rather than replaced. Two callers register their own subset -
      // the views and the renderer plugins - and clearing meant whichever ran
      // second silently unregistered the other's extensions.
      for (const [id, root] of Object.entries(roots)) extensionRoots.set(id, root)
    }
  )

  protocol.handle('novalist-ext', (request) => {
    const url = new URL(request.url)
    // novalist-ext://{extensionId}/{path}: host carries the extension id.
    const root = extensionRoots.get(url.hostname)
    if (!root) return new Response('unknown extension', { status: 404 })
    const relative = decodeURIComponent(url.pathname)
    const resolved = normalize(join(root, relative))
    if (!resolved.startsWith(normalize(root))) {
      return new Response('forbidden', { status: 403 })
    }
    return net.fetch(pathToFileURL(resolved).toString())
  })

  protocol.handle('novalist-project', (request) => {
    if (!projectRoot) return new Response('no project open', { status: 404 })
    const url = new URL(request.url)
    const relative = decodeURIComponent(url.pathname)
    const resolved = normalize(join(projectRoot, relative))
    if (!resolved.startsWith(normalize(projectRoot))) {
      return new Response('forbidden', { status: 403 })
    }
    return net.fetch(pathToFileURL(resolved).toString())
  })
}

export function currentProjectRoot(): string | null {
  return projectRoot
}

// app reference kept for future packaged-path use; avoids unused-import churn.
void app
