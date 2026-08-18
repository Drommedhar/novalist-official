import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowRight, Check, Copy, GripVertical, Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { InputDialog } from '../../shell/InputDialog'
import './drafts.css'

interface DraftRow {
  id: string
  name: string
  isActive: boolean
  notes: string
  createdAt: string
  parentDraftId: string
  chapters: number
  scenes: number
}

interface DraftStructure {
  draftId: string
  name: string
  chapters: { guid: string; title: string; scenes: { id: string; title: string }[] }[]
}

type Pending =
  | { kind: 'new' }
  | { kind: 'duplicate'; id: string; name: string }
  | { kind: 'delete'; id: string; name: string }
  | { kind: 'transfer'; move: boolean; replaced: number }

/**
 * The drafts of a book, as things rather than as entries in a dropdown.
 *
 * A draft could be made, switched to, compared and deleted, and nothing else:
 * it could not be renamed after the moment it was created - the backend could,
 * and nothing called it - it could not be reordered, it could not say what it
 * was for, and content could not cross between two of them except one scene at
 * a time through the compare dialog. A writer with four drafts had four rows
 * called "Draft 1" through "Draft 4" in creation order and no way to fix any of
 * that.
 */
export function DraftsView(): React.JSX.Element {
  const { t } = useTranslation()
  const [drafts, setDrafts] = useState<DraftRow[] | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [dragId, setDragId] = useState<string | null>(null)
  const [dropId, setDropId] = useState<string | null>(null)

  // The transfer picker: which draft is being read, which is being written, and
  // what of it the writer ticked.
  const [fromId, setFromId] = useState('')
  const [toId, setToId] = useState('')
  const [from, setFrom] = useState<DraftStructure | null>(null)
  const [to, setTo] = useState<DraftStructure | null>(null)
  const [chapterPicks, setChapterPicks] = useState<string[]>([])
  const [scenePicks, setScenePicks] = useState<string[]>([])
  const [sent, setSent] = useState<string | null>(null)

  /** The toolbar's draft picker and the open book, after something moved. */
  const syncShell = async (reloadBook: boolean): Promise<void> => {
    await useProjectStore.getState().loadDrafts()
    if (reloadBook)
      useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
  }

  const load = (): void => {
    void rpc.request<DraftRow[]>('drafts/list', []).then((rows) => {
      setDrafts(rows)
      // The two sides default to the draft you are in and the next one along,
      // which is the transfer nine times in ten.
      const active = rows.find((d) => d.isActive) ?? rows[0]
      const other = rows.find((d) => d.id !== active?.id)
      setFromId((current) => current || active?.id || '')
      setToId((current) => current || other?.id || '')
    })
  }

  // Which draft is open is the shell's answer, not this view's. Reading it from
  // the store means "You are here" follows a switch made anywhere - this view's
  // Open button, the toolbar picker, the palette - rather than describing the
  // draft that was open when the list was last fetched.
  const activeDraftId = useProjectStore((s) => s.drafts.find((d) => d.isActive)?.id ?? '')

  useEffect(load, [activeDraftId])

  useEffect(() => {
    if (!fromId) return
    void rpc.request<DraftStructure | null>('drafts/structure', [fromId]).then(setFrom)
  }, [fromId, sent, activeDraftId])

  useEffect(() => {
    if (!toId) return
    void rpc.request<DraftStructure | null>('drafts/structure', [toId]).then(setTo)
  }, [toId, sent, activeDraftId])

  if (!drafts) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const rename = (id: string, name: string): void => {
    if (!name.trim()) return
    void rpc.request<DraftRow[]>('drafts/rename', [id, name.trim()]).then((rows) => {
      setDrafts(rows)
      void syncShell(false)
    })
  }

  const setNotes = (id: string, notes: string): void => {
    void rpc.request<DraftRow[]>('drafts/setNotes', [id, notes]).then(setDrafts)
  }

  const reorder = (draggedId: string, targetId: string): void => {
    const ids = drafts.map((d) => d.id).filter((id) => id !== draggedId)
    const at = ids.indexOf(targetId)
    ids.splice(at < 0 ? ids.length : at, 0, draggedId)
    void rpc.request<DraftRow[]>('drafts/reorder', [ids]).then((rows) => {
      setDrafts(rows)
      void syncShell(false)
    })
  }

  // Everything selected, including the scenes a ticked chapter carries with it.
  const pickedScenes = new Set(scenePicks)
  for (const chapter of from?.chapters ?? [])
    if (chapterPicks.includes(chapter.guid))
      for (const scene of chapter.scenes) pickedScenes.add(scene.id)

  // What the target already has under the same identity. Sending it again
  // rewrites its prose, and that is worth saying before it happens rather than
  // after.
  const inTarget = new Set((to?.chapters ?? []).flatMap((c) => c.scenes.map((s) => s.id)))
  const replaced = [...pickedScenes].filter((id) => inTarget.has(id)).length

  /**
   * The target draft as this transfer would leave it.
   *
   * Ticking things on one side and being told a number was not an answer to
   * "what will this do to the other draft" - the writer could not see whether
   * a chapter was arriving whole, landing beside scenes already there, or
   * writing over the version they meant to keep. The right-hand column is that
   * draft after the copy, with every row that changes saying how.
   */
  const preview: {
    guid: string
    title: string
    state: 'keeps' | 'new'
    scenes: { id: string; title: string; state: 'keeps' | 'new' | 'rewritten' }[]
  }[] = []
  const sourceByGuid = new Map((from?.chapters ?? []).map((c) => [c.guid, c] as const))
  const seenChapters = new Set<string>()

  for (const chapter of to?.chapters ?? []) {
    seenChapters.add(chapter.guid)
    const arriving = (sourceByGuid.get(chapter.guid)?.scenes ?? []).filter((s) =>
      pickedScenes.has(s.id)
    )
    const here = new Set(chapter.scenes.map((s) => s.id))
    preview.push({
      guid: chapter.guid,
      title: chapter.title,
      state: 'keeps',
      scenes: [
        ...chapter.scenes.map((s) => ({
          id: s.id,
          title: s.title,
          state: pickedScenes.has(s.id) ? ('rewritten' as const) : ('keeps' as const)
        })),
        ...arriving
          .filter((s) => !here.has(s.id))
          .map((s) => ({ id: s.id, title: s.title, state: 'new' as const }))
      ]
    })
  }

  // Chapters the target does not have yet arrive at the end, which is where the
  // transfer puts them.
  for (const chapter of from?.chapters ?? []) {
    if (seenChapters.has(chapter.guid)) continue
    const arriving = chapter.scenes.filter((s) => pickedScenes.has(s.id))
    if (arriving.length === 0) continue
    preview.push({
      guid: chapter.guid,
      title: chapter.title,
      state: 'new',
      scenes: arriving.map((s) => ({ id: s.id, title: s.title, state: 'new' as const }))
    })
  }

  const transfer = async (move: boolean): Promise<void> => {
    const result = await rpc.request<{ chapters: number; scenes: number; replaced: number }>(
      'drafts/transfer',
      [fromId, toId, chapterPicks, scenePicks, move]
    )
    setChapterPicks([])
    setScenePicks([])
    setSent(`${result.scenes}:${Date.now()}`)
    load()
    // The writer may be standing in one of the two drafts, so the binder has to
    // be told what it holds now.
    await syncShell(true)
  }

  return (
    <div className="drafts">
      <div className="drafts-toolbar">
        <span className="drafts-title">{t('drafts.title')}</span>
        <button
          className="toolbar-button toolbar-action"
          onClick={() => setPending({ kind: 'new' })}
        >
          <Plus size={14} strokeWidth={2} />
          {t('drafts.new')}
        </button>
      </div>

      {/* What the view is for is said once, by the guidance strip above it. */}
      <div className="drafts-scroll">
        <div className="drafts-list">
          {drafts.map((draft) => (
            <div
              key={draft.id}
              className={`drafts-row${draft.isActive ? ' active' : ''}${
                dropId === draft.id ? ' drop' : ''
              }`}
              draggable
              onDragStart={(e) => {
                setDragId(draft.id)
                e.dataTransfer.effectAllowed = 'move'
              }}
              onDragEnd={() => {
                setDragId(null)
                setDropId(null)
              }}
              onDragOver={(e) => {
                e.preventDefault()
                if (dragId && dragId !== draft.id) setDropId(draft.id)
              }}
              onDrop={() => {
                if (dragId && dragId !== draft.id) reorder(dragId, draft.id)
                setDragId(null)
                setDropId(null)
              }}
            >
              <span className="drafts-grip" title={t('drafts.reorder')}>
                <GripVertical size={14} strokeWidth={2} />
              </span>

              <div className="drafts-fields">
                <input
                  className="inspector-input drafts-name"
                  defaultValue={draft.name}
                  aria-label={t('drafts.name')}
                  onBlur={(e) => e.target.value !== draft.name && rename(draft.id, e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                />
                <input
                  className="inspector-input drafts-notes"
                  defaultValue={draft.notes}
                  placeholder={t('drafts.notesPlaceholder')}
                  aria-label={t('drafts.notes')}
                  onBlur={(e) => e.target.value !== draft.notes && setNotes(draft.id, e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                />
              </div>

              <span className="drafts-counts">
                {t('drafts.counts', { chapters: draft.chapters, scenes: draft.scenes })}
              </span>

              {draft.isActive ? (
                <span className="drafts-current">
                  <Check size={12} strokeWidth={2} />
                  {t('drafts.current')}
                </span>
              ) : (
                <button
                  className="dialog-button"
                  onClick={() => void useProjectStore.getState().switchDraft(draft.id)}
                >
                  {t('drafts.switchTo')}
                </button>
              )}
              <button
                className="dialog-button"
                title={t('drafts.duplicate')}
                onClick={() => setPending({ kind: 'duplicate', id: draft.id, name: draft.name })}
              >
                <Copy size={12} strokeWidth={2} />
              </button>
              <button
                className="dialog-button"
                title={t('drafts.delete')}
                disabled={drafts.length <= 1}
                onClick={() => setPending({ kind: 'delete', id: draft.id, name: draft.name })}
              >
                <Trash2 size={12} strokeWidth={2} />
              </button>
            </div>
          ))}
        </div>

        {/* Sending content the other way is the half of "reorganize" the
            dropdown could never do. */}
        <div className="drafts-transfer">
          <div className="inspector-label">{t('drafts.sendTitle')}</div>
          <p className="inspector-meta">{t('drafts.sendHint')}</p>

          <div className="drafts-sides">
            <label className="drafts-side">
              {t('drafts.sendFrom')}
              <select
                className="inspector-input"
                value={fromId}
                onChange={(e) => {
                  setFromId(e.target.value)
                  setChapterPicks([])
                  setScenePicks([])
                }}
              >
                {drafts.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            <ArrowRight size={14} strokeWidth={2} className="drafts-arrow" />
            <label className="drafts-side">
              {t('drafts.sendTo')}
              <select
                className="inspector-input"
                value={toId}
                onChange={(e) => setToId(e.target.value)}
              >
                {drafts
                  .filter((d) => d.id !== fromId)
                  .map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
              </select>
            </label>
          </div>

          {from && from.chapters.length === 0 && (
            <p className="codex-empty">{t('drafts.sourceEmpty')}</p>
          )}

          <div className="drafts-panes">
            <div className="drafts-pane">
              <div className="drafts-pane-head">{t('drafts.paneSource', { name: from?.name ?? '' })}</div>
              <div className="drafts-tree">
            {from?.chapters.map((chapter) => {
              const whole = chapterPicks.includes(chapter.guid)
              return (
                <div key={chapter.guid} className="drafts-chapter">
                  <label className="drafts-pick">
                    <input
                      type="checkbox"
                      checked={whole}
                      onChange={(e) =>
                        setChapterPicks(
                          e.target.checked
                            ? [...chapterPicks, chapter.guid]
                            : chapterPicks.filter((g) => g !== chapter.guid)
                        )
                      }
                    />
                    <span className="drafts-chapter-title">{chapter.title}</span>
                  </label>
                  {chapter.scenes.map((scene) => (
                    <label key={scene.id} className="drafts-pick drafts-scene">
                      <input
                        type="checkbox"
                        // A ticked chapter carries its scenes, so their own
                        // ticks are shown and not asked for again.
                        checked={whole || scenePicks.includes(scene.id)}
                        disabled={whole}
                        onChange={(e) =>
                          setScenePicks(
                            e.target.checked
                              ? [...scenePicks, scene.id]
                              : scenePicks.filter((id) => id !== scene.id)
                          )
                        }
                      />
                      <span>{scene.title}</span>
                    </label>
                  ))}
                </div>
              )
            })}
              </div>
            </div>

            <div className="drafts-pane">
              <div className="drafts-pane-head">{t('drafts.paneTarget', { name: to?.name ?? '' })}</div>
              <div className="drafts-tree">
                {preview.length === 0 && <p className="inspector-meta">{t('drafts.targetEmpty')}</p>}
                {preview.map((chapter) => (
                  <div key={chapter.guid} className="drafts-chapter">
                    <div className={`drafts-preview-row${chapter.state === 'new' ? ' arriving' : ''}`}>
                      <span className="drafts-chapter-title">{chapter.title}</span>
                      {chapter.state === 'new' && (
                        <span className="drafts-mark new">{t('drafts.markNew')}</span>
                      )}
                    </div>
                    {chapter.scenes.map((scene) => (
                      <div
                        key={scene.id}
                        className={`drafts-preview-row drafts-scene${
                          scene.state === 'keeps' ? '' : ' arriving'
                        }`}
                      >
                        <span>{scene.title}</span>
                        {scene.state === 'new' && (
                          <span className="drafts-mark new">{t('drafts.markNew')}</span>
                        )}
                        {scene.state === 'rewritten' && (
                          <span className="drafts-mark rewritten">{t('drafts.markRewritten')}</span>
                        )}
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="drafts-send-actions">
            <span className="inspector-meta">
              {t('drafts.selected', { count: pickedScenes.size })}
              {replaced > 0 && ` - ${t('drafts.willReplace', { count: replaced })}`}
            </span>
            <button
              className="dialog-button primary"
              disabled={pickedScenes.size === 0 || !toId}
              onClick={() =>
                replaced > 0
                  ? setPending({ kind: 'transfer', move: false, replaced })
                  : void transfer(false)
              }
            >
              {t('drafts.copyTo')}
            </button>
            <button
              className="dialog-button"
              disabled={pickedScenes.size === 0 || !toId}
              onClick={() => setPending({ kind: 'transfer', move: true, replaced })}
            >
              {t('drafts.moveTo')}
            </button>
          </div>
        </div>
      </div>

      {pending?.kind === 'new' && (
        <InputDialog
          title={t('drafts.new')}
          placeholder={t('draft.newPrompt')}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            setPending(null)
            void rpc.request('project/createDraft', [name, null]).then(() => {
              load()
              void syncShell(false)
            })
          }}
        />
      )}
      {pending?.kind === 'duplicate' && (
        <InputDialog
          title={t('drafts.duplicate')}
          placeholder={t('drafts.duplicateOf', { name: pending.name })}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            const id = pending.id
            setPending(null)
            void rpc.request<DraftRow[]>('drafts/duplicate', [id, name]).then((rows) => {
              setDrafts(rows)
              void syncShell(false)
            })
          }}
        />
      )}
      {pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('drafts.deleteTitle')}
          message={t('drafts.deleteMessage', { name: pending.name })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const id = pending.id
            setPending(null)
            void useProjectStore
              .getState()
              .deleteDraft(id)
              .then(() => load())
          }}
        />
      )}
      {pending?.kind === 'transfer' && (
        <ConfirmDialog
          title={pending.move ? t('drafts.moveTitle') : t('drafts.copyTitle')}
          message={
            pending.move
              ? t('drafts.moveMessage', { count: pickedScenes.size, replaced: pending.replaced })
              : t('drafts.replaceMessage', { count: pending.replaced })
          }
          confirmLabel={pending.move ? t('drafts.moveTo') : t('drafts.copyTo')}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const move = pending.move
            setPending(null)
            void transfer(move)
          }}
        />
      )}
    </div>
  )
}
