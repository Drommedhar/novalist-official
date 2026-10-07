import { useCallback, useEffect, useRef, useState, type SetStateAction } from 'react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { persistPendingWrite, registerPendingWrite } from '../../stores/pendingWrites'

interface SectionRow {
  title: string
  content: string
}

/** Keep drafts and their owner together: navigation can unmount CodeMirror
 * before its deferred blur notification, and an entry switch can reuse it. */
export function useEntitySections(
  entityType: string,
  selectedId: string | null,
  record: Record<string, unknown> | null
): {
  sections: SectionRow[]
  setSections: (next: SetStateAction<SectionRow[]>) => void
  flushSections: () => Promise<void>
} {
  const [sections, setState] = useState<SectionRow[]>([])
  const rows = useRef<SectionRow[]>([])
  const loadedFor = useRef<string | null>(null)
  const restoredRevision = useCodexStore((state) => state.restoredRevision)
  const pending = useRef<{ type: string; id: string; sections: SectionRow[] } | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const inFlight = useRef<Promise<void> | null>(null)

  const flushSections = useCallback((): Promise<void> => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
    const draft = pending.current
    if (!draft) return inFlight.current ?? Promise.resolve()
    pending.current = null
    const request = persistPendingWrite(`entity-sections:${draft.type}:${draft.id}`, async () => {
      const updated = await rpc.request<Record<string, unknown>>('entities/updateLists', [
        draft.type, draft.id, null, draft.sections, null
      ])
      const codex = useCodexStore.getState()
      if (codex.entityType === draft.type && codex.selectedId === draft.id) {
        useCodexStore.setState({ selectedRecord: updated })
      }
    })
    inFlight.current = request
    const settled = (): void => {
      // Failed payloads remain retryable in pendingWrites. A settled rejection
      // here must not keep rejecting shutdown after that retry succeeds.
      if (inFlight.current === request) inFlight.current = null
    }
    void request.then(settled, settled)
    return request
  }, [])

  useEffect(() => {
    if (!selectedId || !record || record.id !== selectedId) return
    const owner = `${entityType}:${selectedId}:${restoredRevision}`
    if (loadedFor.current === owner) return
    loadedFor.current = owner
    rows.current = Array.isArray(record.sections)
      ? (record.sections as SectionRow[]).map((section) => ({ ...section })) : []
    setState(rows.current)
  }, [entityType, selectedId, record, restoredRevision])

  useEffect(() => () => {
    void flushSections().catch(() => {})
  }, [entityType, selectedId, flushSections])

  useEffect(() => registerPendingWrite(flushSections), [flushSections])

  const setSections = (next: SetStateAction<SectionRow[]>): void => {
    if (!selectedId) return
    rows.current = typeof next === 'function' ? next(rows.current) : next
    setState(rows.current)
    pending.current = {
      type: entityType, id: selectedId,
      sections: rows.current.map((section) => ({ ...section }))
    }
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => void flushSections().catch(() => {}), 350)
  }

  return { sections, setSections, flushSections }
}
