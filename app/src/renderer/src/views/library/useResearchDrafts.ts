import { useCallback, useEffect, useRef, useState, type SetStateAction, type RefObject } from 'react'
import { rpc } from '../../rpc/client'
import { persistPendingWrite, registerPendingWrite } from '../../stores/pendingWrites'
import { useProjectStore } from '../../stores/projectStore'
import { mobileRecoveryScope, registerResearchRecovery } from '../../mobile/recovery'
import { recordRecovery, readRecoveryJournal, removeRecovery, sameRecoveryScope, sameResearchValue, researchValue, type ResearchRecoveryValue } from '../../mobile/recoveryJournal'
import type { ResearchItemDto } from './researchModel'

async function refreshResearch(state: {
  current: RefObject<ResearchItemDto[]>; drafts: RefObject<Map<string, ResearchItemDto>>
  mounted: RefObject<boolean>; writeRevision: RefObject<number>; refreshRequest: RefObject<number>
  setItems: (items: ResearchItemDto[]) => void; setSaveError: (error: string) => void
}): Promise<void> {
  const request = ++state.refreshRequest.current
  try {
    while (state.mounted.current && request === state.refreshRequest.current) {
      const revision = state.writeRevision.current
      const updated = await rpc.request<ResearchItemDto[]>('research/list')
      if (!state.mounted.current || request !== state.refreshRequest.current) return
      if (revision !== state.writeRevision.current) continue
      const incoming = new Set(updated.map(item => item.id))
      state.setItems([...updated, ...state.current.current.filter(item => !incoming.has(item.id) && state.drafts.current.has(item.id))])
      return
    }
  } catch (reason) {
    if (state.mounted.current) state.setSaveError(String(reason))
  }
}

function mergeResearchDrafts(items: ResearchItemDto[], drafts: Map<string, ResearchItemDto>, originals: Map<string, ResearchRecoveryValue>): ResearchItemDto[] {
  return items.map(item => {
    const draft = drafts.get(item.id)
    if (!draft) return item
    const proseChanged = draft.content !== originals.get(item.id)?.content
    return { ...item, title: draft.title, type: draft.type, content: proseChanged ? draft.content : item.content,
      sharedProse: proseChanged ? undefined : item.sharedProse, tags: draft.tags, entityRefs: draft.entityRefs }
  })
}

export function useResearchDrafts() {
  const [items, setState] = useState<ResearchItemDto[]>([])
  const [saveError, setSaveError] = useState<string | null>(null)
  const current = useRef(items)
  const drafts = useRef(new Map<string, ResearchItemDto>())
  const originals = useRef(new Map<string, ResearchRecoveryValue>())
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const mounted = useRef(true)
  const owner = useRef(useProjectStore.getState().projectPath)
  const writeRevision = useRef(0)
  const refreshRequest = useRef(0)

  const setItems = useCallback((next: SetStateAction<ResearchItemDto[]>): void => {
    const incoming = typeof next === 'function' ? next(current.current) : next
    current.current = mergeResearchDrafts(incoming, drafts.current, originals.current)
    if (mounted.current) setState(current.current)
  }, [])

  const refresh = useCallback(() => refreshResearch({ current, drafts, mounted, writeRevision, refreshRequest, setItems, setSaveError }), [setItems])

  const save = useCallback(async (item: ResearchItemDto): Promise<void> => {
    const draft = drafts.current.get(item.id) ?? item
    const scope = mobileRecoveryScope()
    try {
      await persistPendingWrite(`research:${owner.current}:${draft.id}`, async () => {
        const updated = await rpc.request<ResearchItemDto[]>('research/save', [
          draft.id, draft.title, draft.type, draft.sharedProse ? null : draft.content, draft.tags, draft.entityRefs
        ])
        writeRevision.current++
        if (drafts.current.get(draft.id) === draft) { drafts.current.delete(draft.id); originals.current.delete(draft.id) }
        if (scope) for (const entry of readRecoveryJournal()) {
          if (entry.kind === 'research' && sameRecoveryScope(entry.scope, scope) && sameResearchValue(entry.draft, draft)) removeRecovery(entry)
        }
        const acknowledged = updated.find((entry) => entry.id === draft.id)
        if (acknowledged) setItems(current.current.map((entry) => entry.id === draft.id
          ? { ...acknowledged, status: entry.status, rating: entry.rating, relatedIds: entry.relatedIds }
          : entry))
      })
      if (mounted.current) setSaveError(null)
    } catch (reason) {
      if (mounted.current) setSaveError(String(reason))
      throw reason
    }
  }, [setItems])

  const flush = useCallback(async (): Promise<void> => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
    const results = await Promise.allSettled([...drafts.current.values()].map(save))
    const failure = results.find((result) => result.status === 'rejected')
    if (failure?.status === 'rejected') {
      if (mounted.current) setSaveError(String(failure.reason))
      throw failure.reason
    }
  }, [save])

  const journalDrafts = useCallback((): void => {
    const scope = mobileRecoveryScope()
    if (!scope) return
    for (const draft of drafts.current.values()) recordRecovery({ scope, kind: 'research', source: draft.id, draft: researchValue(draft), base: originals.current.get(draft.id) ?? researchValue(draft) })
  }, [])

  useEffect(() => {
    mounted.current = true
    const unregisterRecovery = registerResearchRecovery(journalDrafts)
    const unregister = registerPendingWrite(flush)
    return () => {
      mounted.current = false
      unregister()
      unregisterRecovery()
      void flush().catch(() => { /* Failed writes remain in the shared retry registry. */ })
    }
  }, [flush, journalDrafts])

  const patchItem = (id: string, patch: Partial<ResearchItemDto>): void => {
    const item = current.current.find((entry) => entry.id === id)
    if (!item) return
    const next = { ...item, ...patch }
    if (!originals.current.has(id)) originals.current.set(id, researchValue(item))
    drafts.current.set(id, next)
    setItems(current.current)
    try { journalDrafts() } catch (reason) { setSaveError(String(reason)) }
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => void flush().catch(() => {}), 350)
  }

  return { items, setItems, refresh, save, saveError, retrySave: flush, patchItem }
}
