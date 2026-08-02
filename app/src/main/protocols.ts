import { app, ipcMain, net, protocol } from 'electron'
import { pathToFileURL } from 'node:url'
import { join, normalize } from 'node:path'

let projectRoot: string | null = null
const extensionRoots = new Map<string, string>()

/** Where the theme shim is served from, under every extension's own host. */
const THEME_SHIM_PATH = '/__novalist/theme.js'

/** Whether a request is for the theme shim, wherever the page asking sits. */
function isThemeShim(pathname: string): boolean {
  return pathname === THEME_SHIM_PATH || pathname.endsWith(THEME_SHIM_PATH)
}

/**
 * The script an extension panel includes to be themed.
 *
 * A panel is an iframe and CSS custom properties do not cross a document
 * boundary, so the shell posts the resolved `--nl-*` set in and this stamps it
 * onto the panel's own root element. After that `var(--nl-surface-card)` means
 * inside the panel what it means everywhere else, and a theme change repaints
 * it with the rest of the app.
 *
 * Synthesised here rather than shipped as a file: it then has no packaging
 * story, works identically in dev and in a packaged build, and cannot go
 * missing from an extension folder.
 *
 * The ready ping matters. The shell cannot know when this has run, and a theme
 * posted before the listener existed would be lost - leaving the panel
 * unstyled until the writer happened to change theme.
 */
const THEME_SHIM = `(function () {
  var root = document.documentElement;
  window.addEventListener('message', function (event) {
    var tokens = event.data && event.data.novalistTheme;
    if (!tokens) return;
    for (var name in tokens) {
      if (Object.prototype.hasOwnProperty.call(tokens, name)) {
        root.style.setProperty(name, tokens[name]);
      }
    }
    root.setAttribute('data-novalist-theme', 'ready');
  });
  parent.postMessage({ novalistThemeReady: true }, '*');
})();
`

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

    // Served for every extension, ahead of the root lookup, so a panel gets it
    // with one script tag and nobody has to copy a file into their extension.
    //
    // Matched at any depth, not just at the root: a panel's entry is normally
    // web/panel.html, so the obvious relative src resolves to
    // /web/__novalist/theme.js. Accepting only the root path meant the guide's
    // own snippet 404'd in every extension laid out the usual way.
    if (isThemeShim(decodeURIComponent(url.pathname))) {
      return new Response(THEME_SHIM, {
        headers: { 'content-type': 'text/javascript; charset=utf-8' }
      })
    }

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
