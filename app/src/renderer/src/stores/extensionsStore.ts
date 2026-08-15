import { create } from 'zustand'
import { rpc } from '../rpc/client'
import {
  registerInlineAction,
  setExtensionContextMenuItems,
  type InlineActionResult
} from '../views/editor/editorBridge'
import { setExtensionHotkeys } from '../shell/hotkeys'
import { useProjectStore } from './projectStore'
import { useThemeCatalog } from './themeCatalog'

export interface ExtensionWebView {
  extensionId: string
  key: string
  title: string
  iconPath: string
  placement: 'main' | 'inspector'
  /**
   * The mode this view joins. An extension used to be appended to a flat rail,
   * where it was guaranteed to be the first thing pushed into the overflow
   * menu; naming a mode puts it in a list the writer is already reading.
   * Absent means the mode a content view implies, which is World.
   */
  mode?: string
  entry: string
  folderPath: string
}

export interface ExtensionInfo {
  id: string
  name: string
  version: string
  description: string
  author: string
  isEnabled: boolean
  loadError: string | null
}

export interface ExtensionTheme {
  extensionId: string
  name: string
  slug: string
  accentColor: string | null
  tokens: Record<string, string>
  css: string | null
}

/** A gallery extension plus its installed/update state (backend `store/index`). */
export interface StoreEntry {
  id: string
  name: string
  description: string
  author: string
  repo: string
  tags: string[]
  icon: string | null
  latestVersion: string | null
  releaseTag: string | null
  isCompatible: boolean
  isInstalled: boolean
  hasUpdate: boolean
  installedVersion: string | null
}

/** A published release (backend `store/releases`). */
export interface StoreRelease {
  tagName: string
  version: string
  body: string
  publishedAt: string
}

/** An available update for an installed extension (backend `store/checkUpdates`). */
export interface StoreUpdate {
  extensionId: string
  name: string
  repo: string
  installedVersion: string
  availableVersion: string
}

/** Result of an install/update attempt (backend `store/install` / `store/update`). */
export interface StoreInstallResult {
  id: string
  success: boolean
  error: string | null
}

export type StoreStatus = 'idle' | 'loading' | 'ready' | 'error'

interface InlineActionInfo {
  id: string
  label: string
  group: string
  icon: string
  priority: number
  allowsEmptySelection: boolean
  slashKeyword: string
}

interface ContextMenuInfo {
  id: string
  label: string
  icon: string
  iconPath: string | null
  context: string
}

interface ExtensionHotkeyInfo {
  actionId: string
  displayName: string
  category: string
  defaultGesture: string
}

interface ExtensionsState {
  extensions: ExtensionInfo[]
  views: ExtensionWebView[]
  themes: ExtensionTheme[]
  loaded: boolean
  load(): Promise<void>
  /** Re-fetch contributed views and re-announce their folder roots to the main
   * process. Called after any mutation that can change the view set. */
  refreshViews(): Promise<void>
  /** Re-fetch declarative contributions (inline actions, context menu, hotkeys,
   * themes) and wire them into the renderer. Idempotent. */
  refreshContributions(): Promise<void>
  setEnabled(id: string, enabled: boolean): Promise<void>
  install(sourceFolder: string): Promise<void>
  uninstall(id: string): Promise<void>

  // ── Remote gallery / store ──
  store: StoreEntry[]
  storeStatus: StoreStatus
  storeError: string | null
  storeUpdates: StoreUpdate[]
  /** Fetches the gallery index. Cached after the first success unless `force`. */
  loadStore(force?: boolean): Promise<void>
  /** Checks installed gallery extensions for updates; returns the update count. */
  checkStoreUpdates(): Promise<number>
  /** Downloads and installs (or updates) a gallery extension. Progress and Cancel
   * surface through the shared host-progress overlay. Refreshes both the installed
   * list and the store index on success. */
  installFromStore(id: string, repo: string, update: boolean): Promise<StoreInstallResult>
  fetchReadme(repo: string, id?: string): Promise<string>
  fetchReleases(id: string, repo: string): Promise<StoreRelease[]>
  /** Re-fetches the installed-extensions list (after a live install/update). */
  refreshInstalled(): Promise<void>
}

// Disposers for the inline actions currently registered from extensions, so a
// reload can drop the previous set before registering the fresh one.
let inlineDisposers: Array<() => void> = []

export const useExtensionsStore = create<ExtensionsState>((set, get) => ({
  extensions: [],
  views: [],
  themes: [],
  loaded: false,

  load: async () => {
    if (get().loaded) return
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/load')
    set({ extensions, loaded: true })
    await get().refreshViews()
    await get().refreshContributions()
    // Scripts an extension runs inside the interface. Loaded here rather than
    // at startup only: an extension installed now should work now, and being
    // told to restart is what makes an extension system feel like a build step.
    const { reloadRendererPlugins } = await import('../shell/pluginHost')
    await reloadRendererPlugins()
  },

  refreshViews: async () => {
    const views = await rpc.request<ExtensionWebView[]>('extensions/views')
    await window.novalist.registerExtensionRoots(
      Object.fromEntries(views.map((v) => [v.extensionId, v.folderPath]))
    )
    set({ views })
  },

  refreshContributions: async () => {
    // Inline actions: register each into the editor's inline-action registry with
    // a runner that routes execution back to the owning contributor over RPC.
    const [inline, context, hotkeys, themes] = await Promise.all([
      rpc.request<InlineActionInfo[]>('extensions/inlineActions').catch(() => []),
      rpc.request<ContextMenuInfo[]>('extensions/contextMenuItems').catch(() => []),
      rpc.request<ExtensionHotkeyInfo[]>('extensions/hotkeys').catch(() => []),
      rpc.request<ExtensionTheme[]>('extensions/themes').catch(() => [])
    ])

    for (const dispose of inlineDisposers) dispose()
    inlineDisposers = inline.map((a) =>
      registerInlineAction(
        {
          id: a.id,
          label: a.label,
          group: a.group,
          icon: a.icon,
          allowsEmptySelection: a.allowsEmptySelection,
          slashKeyword: a.slashKeyword
        },
        async (selectedText, context): Promise<InlineActionResult> => {
        const proj = useProjectStore.getState()
        const result = await rpc.request<{
          text: string
          disposition: 'replace' | 'insertAfter' | 'insertAtCaret'
          error: string | null
        } | null>('extensions/inlineAction/execute', [
          a.id,
          selectedText,
          proj.openChapterGuid ?? '',
          proj.openSceneId ?? '',
          context?.precedingText ?? '',
          context?.directive ?? ''
        ])
        if (!result) return { text: '', disposition: 'replace', error: `Unknown inline action: ${a.id}` }
        return { text: result.text, disposition: result.disposition, error: result.error ?? undefined }
      })
    )

    // Context-menu items usable from the editor operate on the current scene.
    setExtensionContextMenuItems(
      context
        .filter((c) => c.context === 'Scene' || c.context === 'Editor')
        .map((c) => ({ id: c.id, label: c.label, icon: c.icon }))
    )

    // Hotkeys: register actions that dispatch back to the host by action id.
    setExtensionHotkeys(
      hotkeys.map((h) => ({
        actionId: h.actionId,
        gesture: h.defaultGesture,
        defaultGesture: h.defaultGesture,
        categoryKey: h.category,
        labelKey: h.displayName,
        run: () => {
          void rpc.request('extensions/hotkey/execute', [h.actionId])
        }
      }))
    )

    // Contributed themes join the same catalog the Settings dropdown reads.
    // Registering re-applies the current selection, so a theme from an extension
    // that loads after settings still takes effect without a restart.
    useThemeCatalog.getState().setSource(
      'extension',
      themes.map((th) => ({
        name: th.name,
        slug: th.slug,
        tokens: th.tokens ?? {},
        css: th.css,
        origin: 'extension' as const
      }))
    )

    set({ themes })
  },

  setEnabled: async (id, enabled) => {
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/setEnabled', [id, enabled])
    set({ extensions })
    await get().refreshViews()
    await get().refreshContributions()
  },

  install: async (sourceFolder) => {
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/install', [sourceFolder])
    set({ extensions })
    await get().refreshViews()
    await get().refreshContributions()
  },

  uninstall: async (id) => {
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/uninstall', [id])
    set({ extensions })
    await get().refreshViews()
    await get().refreshContributions()
  },

  // ── Remote gallery / store ──
  store: [],
  storeStatus: 'idle',
  storeError: null,
  storeUpdates: [],

  loadStore: async (force) => {
    if (!force && get().storeStatus === 'ready') return
    set({ storeStatus: 'loading', storeError: null })
    try {
      const store = await rpc.request<StoreEntry[]>('store/index')
      set({ store, storeStatus: 'ready' })
    } catch (e) {
      set({ storeStatus: 'error', storeError: e instanceof Error ? e.message : String(e) })
    }
  },

  checkStoreUpdates: async () => {
    const storeUpdates = await rpc.request<StoreUpdate[]>('store/checkUpdates')
    set({ storeUpdates })
    // Reflect freshly discovered updates in the browse list if it is loaded.
    if (get().storeStatus === 'ready') await get().loadStore(true)
    return storeUpdates.length
  },

  installFromStore: async (id, repo, update) => {
    const method = update ? 'store/update' : 'store/install'
    const result = await rpc.request<StoreInstallResult>(method, [id, repo])
    if (result.success) {
      await get().refreshInstalled()
      await get().loadStore(true)
      set((s) => ({ storeUpdates: s.storeUpdates.filter((u) => u.extensionId !== id) }))
    }
    return result
  },

  fetchReadme: (repo, id) => rpc.request<string>('store/readme', [repo, id ?? null]),

  fetchReleases: (id, repo) => rpc.request<StoreRelease[]>('store/releases', [id, repo]),

  refreshInstalled: async () => {
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/list')
    set({ extensions })
    await get().refreshViews()
    await get().refreshContributions()
  }
}))
