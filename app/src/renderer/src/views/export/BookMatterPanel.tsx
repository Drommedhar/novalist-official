import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronUp, Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import {
  persistPendingWrite,
  registerPendingWrite,
  retainPendingWrite
} from '../../stores/pendingWrites'

interface Matter {
  id: string
  kind: string
  placement: string
  title: string
  content: string
  order: number
  included: boolean
  inTableOfContents: boolean
  showsHeadingByDefault: boolean
}

/** Autosave delay, matching the scene editor's. */
const SAVE_DELAY_MS = 2000
let matterFenceSequence = 0

/**
 * Front and back matter: the pages around the story. Typed rather than free
 * text, because each kind is set differently in an exported book - a dedication
 * is centred with no heading, a copyright page is small print, a foreword reads
 * like a chapter.
 */
export function BookMatterPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [items, setItems] = useState<Matter[]>([])
  const [kinds, setKinds] = useState<string[]>([])
  const [newKind, setNewKind] = useState('Dedication')
  const [openId, setOpenId] = useState<string | null>(null)
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const draftsRef = useRef(drafts)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const inFlightSave = useRef<Promise<void> | null>(null)
  const immediateWrites = useRef(new Set<Promise<unknown>>())
  const inFlightFenceKey = useRef(`matter:in-flight:${++matterFenceSequence}`)
  draftsRef.current = drafts

  const trackWrite = useCallback(<T,>(write: Promise<T>): Promise<T> => {
    immediateWrites.current.add(write)
    void write.then(
      () => immediateWrites.current.delete(write),
      () => immediateWrites.current.delete(write)
    )
    return write
  }, [])

  const load = useCallback(async () => {
    setItems(await rpc.request<Matter[]>('matter/list'))
  }, [])

  useEffect(() => {
    void rpc.request<string[]>('matter/kinds').then(setKinds)
    void load()
  }, [load])

  const flushDrafts = useCallback(async (): Promise<void> => {
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = null
    while (true) {
      const activeSave = inFlightSave.current
      if (activeSave) {
        await activeSave
        continue
      }
      const pending = { ...draftsRef.current }
      const ids = Object.keys(pending)
      if (ids.length === 0) return
      const request = (async (): Promise<void> => {
        for (const id of ids) {
          const content = pending[id]
          await persistPendingWrite(`matter:content:${id}`, () =>
            rpc.request('matter/update', [id, null, content, null, null, null])
          )
        }
        const next = { ...draftsRef.current }
        for (const id of ids) {
          if (next[id] === pending[id]) delete next[id]
        }
        draftsRef.current = next
        setDrafts(next)
        await load()
      })()
      inFlightSave.current = request
      try {
        await request
      } finally {
        if (inFlightSave.current === request) inFlightSave.current = null
      }
    }
  }, [load])

  const changeDraft = (id: string, content: string): void => {
    const next = { ...draftsRef.current, [id]: content }
    draftsRef.current = next
    setDrafts(next)
  }

  // Body text is debounced so typing does not write the project file on every
  // keystroke; the toggles save immediately because they are single decisions.
  useEffect(() => {
    if (Object.keys(drafts).length === 0) return
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = window.setTimeout(() => {
      saveTimer.current = null
      void flushDrafts().catch(() => {})
    }, SAVE_DELAY_MS)
    return () => {
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
    }
  }, [drafts, flushDrafts])

  const flushAllWrites = useCallback(async (): Promise<void> => {
    const immediate = [...immediateWrites.current]
    await flushDrafts()
    const results = await Promise.allSettled(immediate)
    const failure = results.find((result) => result.status === 'rejected')
    if (failure?.status === 'rejected') throw failure.reason
  }, [flushDrafts])

  useEffect(() => registerPendingWrite(flushAllWrites), [flushAllWrites])

  useEffect(
    () => () => {
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
      // Draft payloads retain themselves by matter id. Immediate button writes
      // have already started, so retain only an acknowledgement of those exact
      // promises; retrying this fence cannot repeat a create/reorder/delete.
      void flushDrafts().catch(() => {})
      const immediate = [...immediateWrites.current]
      if (immediate.length > 0) {
        retainPendingWrite(inFlightFenceKey.current, async () => {
          const results = await Promise.allSettled(immediate)
          const failure = results.find((result) => result.status === 'rejected')
          if (failure?.status === 'rejected') throw failure.reason
        })
      }
    },
    [flushDrafts]
  )

  const add = async (): Promise<void> => {
    setItems(await trackWrite(rpc.request<Matter[]>('matter/create', [newKind])))
  }

  const update = async (
    id: string,
    patch: { title?: string; included?: boolean; inTableOfContents?: boolean; placement?: string }
  ): Promise<void> => {
    setItems(
      await trackWrite(
        rpc.request<Matter[]>('matter/update', [
          id,
          patch.title ?? null,
          null,
          patch.included ?? null,
          patch.inTableOfContents ?? null,
          patch.placement ?? null
        ])
      )
    )
  }

  const move = async (id: string, delta: number): Promise<void> => {
    setItems(await trackWrite(rpc.request<Matter[]>('matter/reorder', [id, delta])))
  }

  const remove = async (id: string): Promise<void> => {
    if (!window.confirm(t('matter.deleteConfirm'))) return
    setItems(await trackWrite(rpc.request<Matter[]>('matter/delete', [id])))
  }

  const group = (placement: string): Matter[] => items.filter((m) => m.placement === placement)

  const renderGroup = (placement: string, labelKey: string): React.JSX.Element => (
    <>
      <h4>{t(labelKey)}</h4>
      {group(placement).length === 0 && <p className="settings-hint">{t('matter.noneHere')}</p>}
      {group(placement).map((m, i, all) => (
        <div key={m.id} className="matter-row">
          <div className="matter-head">
            <button
              className="matter-toggle"
              onClick={() => setOpenId(openId === m.id ? null : m.id)}
            >
              {t(`matter.kind.${m.kind}`, { defaultValue: m.kind })}
              {m.title && <span className="matter-custom-title">{m.title}</span>}
            </button>
            <button className="dialog-button" disabled={i === 0} onClick={() => void move(m.id, -1)}>
              <ChevronUp size={14} />
            </button>
            <button
              className="dialog-button"
              disabled={i === all.length - 1}
              onClick={() => void move(m.id, 1)}
            >
              <ChevronDown size={14} />
            </button>
            <button className="dialog-button" onClick={() => void remove(m.id)}>
              <Trash2 size={14} />
            </button>
          </div>

          {openId === m.id && (
            <div className="matter-body">
              <label className="inspector-label" htmlFor={`matter-title-${m.id}`}>
                {t('matter.heading')}
              </label>
              <input
                id={`matter-title-${m.id}`}
                className="inspector-input"
                value={m.title}
                placeholder={
                  m.showsHeadingByDefault
                    ? t('matter.headingDefault', {
                        kind: t(`matter.kind.${m.kind}`, { defaultValue: m.kind })
                      })
                    : t('matter.headingNone')
                }
                onChange={(e) => void update(m.id, { title: e.target.value })}
              />

              <label className="inspector-label" htmlFor={`matter-body-${m.id}`}>
                {t('matter.content')}
              </label>
              <textarea
                id={`matter-body-${m.id}`}
                className="inspector-input matter-content"
                value={drafts[m.id] ?? m.content}
                onChange={(e) => changeDraft(m.id, e.target.value)}
              />

              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={m.included}
                  onChange={(e) => void update(m.id, { included: e.target.checked })}
                />
                {t('matter.included')}
              </label>
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={m.inTableOfContents}
                  onChange={(e) => void update(m.id, { inTableOfContents: e.target.checked })}
                />
                {t('matter.inToc')}
              </label>
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={m.placement === 'Back'}
                  onChange={(e) =>
                    void update(m.id, { placement: e.target.checked ? 'Back' : 'Front' })
                  }
                />
                {t('matter.moveToBack')}
              </label>
            </div>
          )}
        </div>
      ))}
    </>
  )

  return (
    <div className="matter-panel">
      <p className="settings-hint">{t('matter.description')}</p>

      <div className="settings-button-row">
        <select
          className="inspector-input"
          value={newKind}
          onChange={(e) => setNewKind(e.target.value)}
        >
          {kinds.map((k) => (
            <option key={k} value={k}>
              {t(`matter.kind.${k}`, { defaultValue: k })}
            </option>
          ))}
        </select>
        <button className="dialog-button" onClick={() => void add()}>
          <Plus size={14} /> {t('matter.add')}
        </button>
      </div>

      {renderGroup('Front', 'matter.front')}
      {renderGroup('Back', 'matter.back')}
    </div>
  )
}
