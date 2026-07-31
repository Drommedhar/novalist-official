import { rpc } from '../rpc/client'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore, type MainView } from '../stores/shellStore'

interface RendererPlugin {
  extensionId: string
  extensionName: string
  apiVersion: number
  /** Relative to the extension folder, for the module URL. */
  entry: string
  refused: string | null
  /** Where the extension lives, so the protocol can resolve the module URL. */
  folderPath: string
}

/** One thing a plugin added, kept so the interface can render it. */
export interface PluginCommand {
  extensionId: string
  id: string
  title: string
  run: () => void
}

export interface PluginStatusItem {
  extensionId: string
  id: string
  text: string
  tooltip?: string
  onClick?: () => void
}

/**
 * The surface a renderer plugin is handed. Versioned, because a plugin written
 * against one shape and run against another fails in ways that look like the
 * app's fault rather than the plugin's.
 */
export interface PluginApi {
  readonly apiVersion: 1
  readonly extensionId: string

  /** Call any backend method. Same surface the app itself uses. */
  request<T>(method: string, params?: unknown[]): Promise<T>

  /** Add something to the command palette. */
  registerCommand(id: string, title: string, run: () => void): void

  /** Put text in the status bar. Calling again with the same id replaces it. */
  setStatusItem(id: string, text: string, options?: { tooltip?: string; onClick?: () => void }): void

  /** Take it away again. */
  removeStatusItem(id: string): void

  /** Show the writer a message. */
  notify(message: string): void

  /** Which view the interface is showing, and switch it. */
  getView(): string
  setView(view: string): void

  /** The scene the editor has open, or null. */
  currentScene(): { chapterGuid: string; sceneId: string } | null

  /** Called whenever the open scene changes. Returns a function to stop. */
  onSceneChanged(handler: (scene: { chapterGuid: string; sceneId: string } | null) => void): () => void

  /** Write to the console with this extension's name on it. */
  log(...args: unknown[]): void
}

const commands: PluginCommand[] = []
const statusItems: PluginStatusItem[] = []
const listeners = new Set<() => void>()
let loaded = false

/** Every command plugins have added. */
export function pluginCommands(): readonly PluginCommand[] {
  return commands
}

/** Every status item plugins have added. */
export function pluginStatusItems(): readonly PluginStatusItem[] {
  return statusItems
}

/** Told when either list changes, so a component can re-render. */
export function onPluginContributionsChanged(handler: () => void): () => void {
  listeners.add(handler)
  return () => listeners.delete(handler)
}

function changed(): void {
  for (const handler of listeners) handler()
}

/**
 * What one plugin gets. A fresh object per extension so a status item or a
 * command always carries the name of whoever added it - when a plugin
 * misbehaves the writer needs to know which one to turn off.
 */
function apiFor(extensionId: string, extensionName: string): PluginApi {
  return {
    apiVersion: 1,
    extensionId,

    request: (method, params) => rpc.request(method, params),

    registerCommand(id, title, run) {
      const key = `${extensionId}:${id}`
      const at = commands.findIndex((c) => `${c.extensionId}:${c.id}` === key)
      const entry: PluginCommand = { extensionId, id, title, run }
      if (at >= 0) commands[at] = entry
      else commands.push(entry)
      changed()
    },

    setStatusItem(id, text, options) {
      const key = `${extensionId}:${id}`
      const at = statusItems.findIndex((s) => `${s.extensionId}:${s.id}` === key)
      const entry: PluginStatusItem = { extensionId, id, text, ...options }
      if (at >= 0) statusItems[at] = entry
      else statusItems.push(entry)
      changed()
    },

    removeStatusItem(id) {
      const key = `${extensionId}:${id}`
      const at = statusItems.findIndex((s) => `${s.extensionId}:${s.id}` === key)
      if (at >= 0) {
        statusItems.splice(at, 1)
        changed()
      }
    },

    notify: (message) => useHostBridgeStore.getState().pushToast(String(message)),

    getView: () => useShellStore.getState().mainView,
    setView: (view) => useShellStore.getState().setMainView(view as MainView),

    currentScene() {
      const { openChapterGuid, openSceneId } = useProjectStore.getState()
      return openChapterGuid && openSceneId
        ? { chapterGuid: openChapterGuid, sceneId: openSceneId }
        : null
    },

    onSceneChanged(handler) {
      let last = ''
      return useProjectStore.subscribe((state) => {
        const key = `${state.openChapterGuid ?? ''}|${state.openSceneId ?? ''}`
        if (key === last) return
        last = key
        // A plugin that throws in a subscription would otherwise take the
        // store's notify loop down with it, which stops the whole interface
        // updating rather than only this plugin.
        try {
          handler(
            state.openChapterGuid && state.openSceneId
              ? { chapterGuid: state.openChapterGuid, sceneId: state.openSceneId }
              : null
          )
        } catch (error) {
          console.error(`[${extensionName}]`, error)
        }
      })
    },

    log: (...args) => console.log(`[${extensionName}]`, ...args)
  }
}

/**
 * Loads every renderer plugin, once.
 *
 * A webview is sandboxed and cannot touch the editor. This is not: a plugin
 * here sees every keystroke, and one that misbehaves produces bugs that look
 * like Novalist's. That is what the writer signed up for by installing it, and
 * the manual says so in those words.
 *
 * What is still worth doing is attribution. Each script is evaluated on its own
 * and its failures are caught and named, so a broken plugin is a message about
 * that extension rather than a blank window.
 */
export async function loadRendererPlugins(): Promise<void> {
  if (loaded) return
  loaded = true
  await runPlugins()
}

/**
 * Loads them again, throwing away what the previous ones added.
 *
 * Installing an extension that contributes a script and then being told to
 * restart is the kind of thing that makes an extension system feel like a
 * build step. The Extensions view calls this after it loads.
 */
export async function reloadRendererPlugins(): Promise<void> {
  loaded = true
  commands.length = 0
  statusItems.length = 0
  changed()
  await runPlugins()
}

async function runPlugins(): Promise<void> {

  let plugins: RendererPlugin[] = []
  try {
    // Extensions have to be loaded before any of them can contribute a script,
    // and nothing else guarantees that has happened: the Extensions view loads
    // them, but only if the writer goes there, and a plugin that waits for that
    // is a plugin that does nothing until somebody visits a screen they had no
    // reason to visit. Loading is idempotent on the host.
    await rpc.request('extensions/load')
    plugins = await rpc.request<RendererPlugin[]>('extensions/rendererPlugins')
    // The protocol resolves novalist-ext://<id>/... against these, so they are
    // registered before a single import is attempted. Registering replaces the
    // whole map, so the webview roots go back in with them.
    const views = await rpc
      .request<{ extensionId: string; folderPath: string }[]>('extensions/views')
      .catch(() => [])
    await window.novalist.registerExtensionRoots({
      ...Object.fromEntries(views.map((v) => [v.extensionId, v.folderPath])),
      ...Object.fromEntries(plugins.map((p) => [p.extensionId, p.folderPath]))
    })
  } catch {
    return
  }

  for (const plugin of plugins) {
    if (plugin.refused) {
      // Named rather than dropped: an extension that does nothing and says
      // nothing is the hardest kind of broken to report.
      useHostBridgeStore
        .getState()
        .pushToast(`${plugin.extensionName}: ${plugin.refused}`)
      continue
    }

    try {
      // Imported as a real module over the extension protocol rather than
      // evaluated from a string. Evaluating strings would mean turning on
      // unsafe-eval for the whole interface, which would also let anything
      // else that reached a string run - a far bigger hole than the one the
      // plugin itself opens.
      const url = `novalist-ext://${plugin.extensionId}/${plugin.entry}`
      const module = (await import(/* @vite-ignore */ url)) as {
        default?: (api: PluginApi) => void
        activate?: (api: PluginApi) => void
      }
      const activate = module.activate ?? module.default
      if (typeof activate !== 'function') {
        useHostBridgeStore
          .getState()
          .pushToast(`${plugin.extensionName}: its script exports no activate function`)
        continue
      }
      activate(apiFor(plugin.extensionId, plugin.extensionName))
    } catch (error) {
      console.error(`[${plugin.extensionName}]`, error)
      useHostBridgeStore
        .getState()
        .pushToast(`${plugin.extensionName}: its interface script failed to run`)
    }
  }
}
