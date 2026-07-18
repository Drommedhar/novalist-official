import { create } from 'zustand'
import { rpc } from '../rpc/client'

export interface ExtensionWebView {
  extensionId: string
  key: string
  title: string
  iconPath: string
  placement: 'main' | 'inspector'
  entry: string
  folderPath: string
}

export interface ExtensionInfo {
  id: string
  name: string
  version: string
  isEnabled: boolean
  loadError: string | null
}

interface ExtensionsState {
  extensions: ExtensionInfo[]
  views: ExtensionWebView[]
  loaded: boolean
  load(): Promise<void>
}

export const useExtensionsStore = create<ExtensionsState>((set, get) => ({
  extensions: [],
  views: [],
  loaded: false,

  load: async () => {
    if (get().loaded) return
    const extensions = await rpc.request<ExtensionInfo[]>('extensions/load')
    const views = await rpc.request<ExtensionWebView[]>('extensions/views')
    window.novalist.registerExtensionRoots(
      Object.fromEntries(views.map((v) => [v.extensionId, v.folderPath]))
    )
    set({ extensions, views, loaded: true })
  }
}))
