
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { rpc } from '../../rpc/client'

import { useShellStore } from '../../stores/shellStore'
import { useHostBridgeStore } from '../../stores/hostBridgeStore'

/** Reserved tag marking a quick-captured note that has not been filed yet.
 *  Mirrors ResearchItem.InboxTag on the backend. */
const INBOX_TAG = 'inbox'

export interface ResearchItemDto {
  id: string
  title: string
  type: string
  content: string
  tags: string[]
  fileSize: string
  modified: string
  entityRefs: string[]
  /** "None", "Open", "InProgress" or "Resolved". */
  status: string
  /** 0 for unrated, 1-5 otherwise. */
  rating: number
  /** Ids of other research items this one refers to. */
  relatedIds: string[]
}

export const TYPES = ['Note', 'Link', 'File', 'Image', 'Pdf', 'Audio', 'Video']

/** Where an item stands. Short on purpose - this is not a task tracker. */
export const STATUSES = ['None', 'Open', 'InProgress', 'Resolved']

export const isFileType = (type: string): boolean =>
  type === 'File' || type === 'Image' || type === 'Pdf' || type === 'Audio' || type === 'Video'

function useResearchData() {

  const { t } = useTranslation()
  const pendingResearchId = useShellStore((s) => s.pendingResearchId)
  const clearPendingResearch = useShellStore((s) => s.clearPendingResearch)
  const folderImportOpen = useShellStore((s) => s.dialog === 'importFolder')
  const importRevision = useHostBridgeStore((s) => s.importRevision)
  const [items, setItems] = useState<ResearchItemDto[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [researchTab, setResearchTab] = useState<'project' | 'scratchpad'>('project')
  const [vaultOpen, setVaultOpen] = useState(false)
  const [newTag, setNewTag] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [inboxOnly, setInboxOnly] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [fetchingTitle, setFetchingTitle] = useState(false)
  // Every Codex entry, so a research item can be linked to what it is about.
  const [allEntities, setAllEntities] = useState<{ id: string; name: string }[]>([])
  // Filing an inbox note into the Codex: which dialog is open, if any.
  const [filing, setFiling] = useState<'create' | 'append' | null>(null)

  return { t, pendingResearchId, clearPendingResearch, folderImportOpen, importRevision, items, setItems, selectedId, setSelectedId, search, setSearch, researchTab, setResearchTab, vaultOpen, setVaultOpen, newTag, setNewTag, confirmDelete, setConfirmDelete, inboxOnly, setInboxOnly, dragging, setDragging, fetchingTitle, setFetchingTitle, allEntities, setAllEntities, filing, setFiling }
}

function useResearchLoading(context: ReturnType<typeof useResearchData>) {
  const { folderImportOpen, vaultOpen, setItems, importRevision, pendingResearchId, items, setSelectedId, setResearchTab, setSearch, clearPendingResearch, selectedId, setAllEntities, allEntities } = context
  useEffect(() => {
    if (folderImportOpen || vaultOpen) return
    let cancelled = false
    void rpc
      .request<ResearchItemDto[]>('research/list')
      .then((updated) => {
        if (!cancelled)
          setItems((previous) => {
            // API imports add entries; keep local edits to existing notes while merging new ones.
            const existing = new Map(previous.map((item) => [item.id, item]))
            return updated.map((item) => existing.get(item.id) ?? item)
          })
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [folderImportOpen, vaultOpen, importRevision])

  // Quick-open (and other deep links) can ask for a specific item; select it once
  // the list has loaded, then clear the request so it fires only once.
  useEffect(() => {
    if (!pendingResearchId) return
    if (!items.some((i) => i.id === pendingResearchId)) return
    setSelectedId(pendingResearchId)
    setResearchTab('project')
    setSearch('')
    clearPendingResearch()
  }, [pendingResearchId, items, clearPendingResearch])

  const selected = items.find((i) => i.id === selectedId) ?? null

  // Refresh after either folder-import entry point so new entries can be linked.
  useEffect(() => {
    if (folderImportOpen || vaultOpen) return
    let cancelled = false
    const load = async (): Promise<void> => {
      const types = ['character', 'location', 'item', 'lore']
      try {
        const custom = await rpc.request<{ typeKey: string }[]>('entities/customTypes')
        types.push(...custom.map((c) => c.typeKey))
      } catch {
        // Built-ins alone still make the picker useful.
      }
      const all: { id: string; name: string }[] = []
      for (const typeKey of types) {
        try {
          const list = await rpc.request<{ id: string; name: string }[]>('entities/list', [typeKey])
          all.push(...list.map((e) => ({ id: e.id, name: e.name })))
        } catch {
          // Skip a type that fails rather than losing the whole picker.
        }
      }
      all.sort((a, b) => a.name.localeCompare(b.name))
      if (!cancelled) setAllEntities(all)
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [folderImportOpen, vaultOpen, importRevision])

  const entityNames = new Map(allEntities.map((e) => [e.id, e.name]))

  return { ...context, selected, entityNames }
}

function researchCollection(context: ReturnType<typeof useResearchLoading>) {
  const { items, search, inboxOnly, setItems, selected, setResearchTab, t, setSelectedId } = context
  const isInbox = (item: ResearchItemDto): boolean =>
    item.tags.some((tag) => tag.toLowerCase() === INBOX_TAG)
  const inboxCount = items.filter(isInbox).length

  const query = search.trim().toLowerCase()
  const filtered = items
    .filter((i) => !inboxOnly || isInbox(i))
    .filter(
      (i) =>
        query.length === 0 ||
        i.title.toLowerCase().includes(query) ||
        i.content.toLowerCase().includes(query) ||
        i.tags.some((tag) => tag.toLowerCase().includes(query))
    )

  const save = async (item: ResearchItemDto): Promise<void> => {
    const updated = await rpc.request<ResearchItemDto[]>('research/save', [
      item.id,
      item.title,
      item.type,
      item.content,
      item.tags,
      item.entityRefs
    ])
    setItems(updated)
  }

  const patchSelected = (patch: Partial<ResearchItemDto>): void => {
    if (!selected) return
    setItems(items.map((i) => (i.id === selected.id ? { ...i, ...patch } : i)))
  }

  const create = (type: string, content: string): void => {
    setResearchTab('project')
    void rpc
      .request<ResearchItemDto[]>('research/save', [
        null,
        t('research.titleWatermark'),
        type,
        content,
        [],
        []
      ])
      .then((updated) => {
        setItems(updated)
        setSelectedId(updated[updated.length - 1]?.id ?? null)
      })
  }

  return { ...context, isInbox, inboxCount, query, filtered, save, patchSelected, create }
}

function researchAttachments(context: ReturnType<typeof researchCollection>) {
  const { t, setItems, setSelectedId, setResearchTab, setDragging } = context
  const importFile = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('research.importFile'), 'all')
    if (!path) return
    const updated = await rpc.request<ResearchItemDto[]>('research/import', [path])
    setItems(updated)
    setSelectedId(updated[updated.length - 1]?.id ?? null)
    setResearchTab('project')
  }

  /** Files dropped onto the list are imported exactly like picked ones; dropped
   *  text becomes a note (a URL becomes a link). */
  const handleDrop = async (e: React.DragEvent): Promise<void> => {
    e.preventDefault()
    setDragging(false)
    const files = Array.from(e.dataTransfer.files)
    if (files.length > 0) {
      let updated: ResearchItemDto[] | null = null
      for (const file of files) {
        const path = window.novalist.filePath(file)
        if (!path) continue
        updated = await rpc.request<ResearchItemDto[]>('research/import', [path])
      }
      if (updated) {
        setItems(updated)
        setSelectedId(updated[updated.length - 1]?.id ?? null)
      }
      return
    }

    const text = e.dataTransfer.getData('text/plain').trim()
    if (text.length === 0) return
    const isUrl = /^https?:\/\//i.test(text)
    const updated = await rpc.request<ResearchItemDto[]>('research/save', [
      null,
      isUrl ? text : t('research.titleWatermark'),
      isUrl ? 'Link' : 'Note',
      text,
      [],
      []
    ])
    setItems(updated)
    setSelectedId(updated[updated.length - 1]?.id ?? null)
  }

  return { ...context, importFile, handleDrop }
}

function researchMetadata(context: ReturnType<typeof researchAttachments>) {
  const { selected, newTag, setNewTag, patchSelected, save, setFetchingTitle, setItems } = context
  const addTag = (): void => {
    if (!selected) return
    const tag = newTag.trim()
    if (tag.length === 0) return
    if (selected.tags.some((existing) => existing.toLowerCase() === tag.toLowerCase())) {
      setNewTag('')
      return
    }
    const next = { ...selected, tags: [...selected.tags, tag] }
    patchSelected({ tags: next.tags })
    void save(next)
    setNewTag('')
  }

  /** Names a link item after the page it points at. Leaves the title alone when
   *  the lookup fails (offline, or the page has no title). */
  const fetchLinkTitle = async (): Promise<void> => {
    if (!selected || selected.type !== 'Link') return
    setFetchingTitle(true)
    try {
      const title = await rpc.request<string | null>('research/fetchLinkTitle', [selected.content])
      if (title) {
        patchSelected({ title })
        await save({ ...selected, title })
      }
    } finally {
      setFetchingTitle(false)
    }
  }

  /** Where an item stands and what the writer thinks of it. */
  const setLifecycle = async (
    id: string,
    status: string | null,
    rating: number | null
  ): Promise<void> => {
    setItems(await rpc.request<ResearchItemDto[]>('research/setLifecycle', [id, status, rating]))
  }

  /**
   * Links two items, both ways. A one-way link is discoverable only from the
   * item that has it, and the end worth finding is usually the other one - the
   * question a source answers is what somebody is reading when they need it.
   */
  const toggleRelated = async (otherId: string, linked: boolean): Promise<void> => {
    if (!selected) return
    setItems(await rpc.request<ResearchItemDto[]>('research/link', [selected.id, otherId, linked]))
  }

  const linkEntity = async (entityId: string): Promise<void> => {
    if (!selected || selected.entityRefs.includes(entityId)) return
    const next = { ...selected, entityRefs: [...selected.entityRefs, entityId] }
    patchSelected({ entityRefs: next.entityRefs })
    await save(next)
  }

  const unlinkEntity = async (entityId: string): Promise<void> => {
    if (!selected) return
    const next = { ...selected, entityRefs: selected.entityRefs.filter((r) => r !== entityId) }
    patchSelected({ entityRefs: next.entityRefs })
    await save(next)
  }

  return { ...context, addTag, fetchLinkTitle, setLifecycle, toggleRelated, linkEntity, unlinkEntity }
}

function researchFiling(context: ReturnType<typeof researchMetadata>) {
  const { patchSelected, save, selected, setFiling, t } = context
  /** Clears the inbox flag: the note has been dealt with and becomes an ordinary
   *  research item. Filing never deletes anything — you keep the original. */
  const markFiled = async (item: ResearchItemDto): Promise<void> => {
    const next = { ...item, tags: item.tags.filter((tag) => tag.toLowerCase() !== INBOX_TAG) }
    patchSelected({ tags: next.tags })
    await save(next)
  }

  /** Files the note onto a brand-new Codex entry named after its title. */
  const fileAsNewEntity = async (typeKey: string): Promise<void> => {
    const item = selected
    setFiling(null)
    if (!item) return
    const record = await rpc.request<Record<string, unknown>>('entities/create', [
      typeKey,
      item.title,
      null
    ])
    if (item.content.trim().length > 0) {
      await rpc.request('entities/appendToSection', [
        typeKey,
        String(record.id),
        t('capture.defaultSectionTitle'),
        item.content
      ])
    }
    await markFiled(item)
  }

  /** Files the note into an existing entry's section. */
  const fileIntoEntity = async (target: {
    typeKey: string
    id: string
    sectionTitle: string
  }): Promise<void> => {
    const item = selected
    setFiling(null)
    if (!item) return
    await rpc.request('entities/appendToSection', [
      target.typeKey,
      target.id,
      target.sectionTitle,
      item.content
    ])
    await markFiled(item)
  }

  const removeTag = (tag: string): void => {
    if (!selected) return
    const next = { ...selected, tags: selected.tags.filter((existing) => existing !== tag) }
    patchSelected({ tags: next.tags })
    void save(next)
  }

    return { ...context, markFiled, fileAsNewEntity, fileIntoEntity, removeTag }
}

export function useResearchModel() {
  const state0 = useResearchData()
  const state1 = useResearchLoading(state0)
  const state2 = researchCollection(state1)
  const state3 = researchAttachments(state2)
  const state4 = researchMetadata(state3)
  const state5 = researchFiling(state4)
  return state5
}
