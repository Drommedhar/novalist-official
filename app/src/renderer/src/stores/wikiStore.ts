import { create } from 'zustand'
import { rpc } from '../rpc/client'

// Mirrors the WikiRpc DTOs (camelCase over the wire).

export interface WikiEntry {
  id: string
  typeKey: string
  title: string
  subtitle: string | null
  imageUrl: string | null
  isWorldBible: boolean
  aliases: string[]
}

export interface WikiTypeGroup {
  typeKey: string
  customTypeLabel: string | null
  entries: WikiEntry[]
}

export interface WikiScopeGroup {
  isWorldBible: boolean
  types: WikiTypeGroup[]
}

export interface WikiIndex {
  scopes: WikiScopeGroup[]
}

export interface WikiLinkTarget {
  name: string
  entityId: string | null
  typeKey: string | null
}

export interface WikiField {
  labelKey: string | null
  literalLabel: string | null
  value: string
  linkEntityId: string | null
  linkTypeKey: string | null
}

export interface WikiImage {
  name: string
  url: string
}

export interface WikiInfobox {
  primaryImageUrl: string | null
  images: WikiImage[]
  fields: WikiField[]
}

export interface WikiSection {
  title: string
  content: string
}

export interface WikiRelationship {
  role: string
  targets: WikiLinkTarget[]
}

export interface WikiAppearance {
  chapterGuid: string
  sceneId: string
  chapterOrder: number
  sceneOrder: number
  chapterTitle: string
  sceneTitle: string
  synopsis: string | null
  storyDate: string
  isoDate: string | null
}

export interface WikiLead {
  primary: string | null
  secondary: string | null
  secondaryConnector: string
}

export interface WikiStats {
  appearanceCount: number
  chapterCount: number
  povSceneCount: number | null
  first: WikiAppearance | null
  last: WikiAppearance | null
}

export interface WikiReference {
  name: string
  entityId: string | null
  typeKey: string | null
  role: string
}

export interface WikiCoAppearance {
  name: string
  entityId: string
  typeKey: string
  sharedScenes: number
}

export interface WikiMapPin {
  mapId: string
  mapName: string
  pinId: string
  pinLabel: string
}

export interface WikiPlotline {
  id: string
  name: string
  color: string
}

export interface WikiOverride {
  scope: string
  changes: WikiField[]
  images: WikiImage[]
  relationships: WikiRelationship[]
  aliases: string[]
  sectionTitles: string[]
}

export interface WikiArticle {
  id: string
  typeKey: string
  customTypeLabel: string | null
  title: string
  isWorldBible: boolean
  aliases: string[]
  lead: WikiLead
  description: string | null
  infobox: WikiInfobox
  stats: WikiStats | null
  sections: WikiSection[]
  relationships: WikiRelationship[]
  referencedBy: WikiReference[]
  appearsWith: WikiCoAppearance[]
  mapPins: WikiMapPin[]
  plotlines: WikiPlotline[]
  overrides: WikiOverride[]
  appearances: WikiAppearance[]
}

interface WikiState {
  index: WikiScopeGroup[] | null
  loading: boolean
  currentType: string | null
  currentId: string | null
  article: WikiArticle | null
  articleLoading: boolean
  loadIndex(): Promise<void>
  openArticle(type: string, id: string): Promise<void>
  clear(): void
}

export const useWikiStore = create<WikiState>((set, get) => ({
  index: null,
  loading: false,
  currentType: null,
  currentId: null,
  article: null,
  articleLoading: false,

  loadIndex: async () => {
    set({ loading: true })
    const result = await rpc.request<WikiIndex>('wiki/index', [])
    set({ index: result.scopes, loading: false })

    // Auto-open the first entry so the article pane is never empty on entry.
    if (!get().currentId) {
      const first = result.scopes.flatMap((s) => s.types).flatMap((t) => t.entries)[0]
      if (first) await get().openArticle(first.typeKey, first.id)
    }
  },

  openArticle: async (type, id) => {
    set({ currentType: type, currentId: id, articleLoading: true })
    const article = await rpc.request<WikiArticle>('wiki/article', [type, id])
    // Guard against a stale response if the user clicked another entry meanwhile.
    if (get().currentId === id) set({ article, articleLoading: false })
  },

  clear: () => set({ currentType: null, currentId: null, article: null })
}))
