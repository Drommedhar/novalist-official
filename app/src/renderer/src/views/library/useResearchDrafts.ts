import { useCallback, useEffect, useRef, useState, type SetStateAction } from 'react'
import { rpc } from '../../rpc/client'
import { persistPendingWrite, registerPendingWrite } from '../../stores/pendingWrites'
import { useProjectStore } from '../../stores/projectStore'
import { mobileRecoveryScope, registerResearchRecovery } from '../../mobile/recovery'
import { recordRecovery, readRecoveryJournal, removeRecovery, sameRecoveryScope, sameResearchValue, researchValue, type ResearchRecoveryValue } from '../../mobile/recoveryJournal'
import type { ResearchItemDto } from './researchModel'

export function useResearchDrafts() {
  const [items, setState] = useState<ResearchItemDto[]>([])
  const [saveError, setSaveError] = useState<string | null>(null)
  const current = useRef(items)
  const drafts = useRef(new Map<string, ResearchItemDto>())
  const originals = useRef(new Map<string, ResearchRecoveryValue>())
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const mounted = useRef(true)
  const owner = useRef(useProjectStore.getState().projectPath)

  const setItems = useCallback((next: SetStateAction<ResearchItemDto[]>): void => {
    const incoming = typeof next === 'function' ? next(current.current) : next
    current.current = incoming.map((item) => {
      const draft = drafts.current.get(item.id)
      return draft ? { ...item, title: draft.title, type: draft.type, content: draft.content, tags: draft.tags, entityRefs: draft.entityRefs } : item
    })
    if (mounted.current) setState(current.current)
  }, [])

  const save = useCallback(async (item: ResearchItemDto): Promise<void> => {
    const draft = drafts.current.get(item.id) ?? item
    const scope = mobileRecoveryScope()
    try {
      await persistPendingWrite(`research:${owner.current}:${draft.id}`, async () => {
        const updated = await rpc.request<ResearchItemDto[]>('research/save', [
          draft.id, draft.title, draft.type, draft.content, draft.tags, draft.entityRefs
        ])
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

  return { items, setItems, save, saveError, retrySave: flush, patchItem }
}
