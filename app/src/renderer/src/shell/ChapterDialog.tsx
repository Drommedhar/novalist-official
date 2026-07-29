import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore, type ChapterDto, type ProjectStateDto } from '../stores/projectStore'
import { useManuscriptPropsStore } from '../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from './ManuscriptPropertyField'
import './shellDialogs.css'

/** Chapter status enum values (mirrors Novalist.Core ChapterStatus) with the
 * display label keys used elsewhere in the app. */
const CHAPTER_STATUSES: { value: string; labelKey: string }[] = [
  { value: 'Outline', labelKey: 'dashboard.statusOutline' },
  { value: 'FirstDraft', labelKey: 'dashboard.statusFirstDraft' },
  { value: 'Revised', labelKey: 'dashboard.statusRevised' },
  { value: 'Edited', labelKey: 'dashboard.statusEdited' },
  { value: 'Final', labelKey: 'dashboard.statusFinal' }
]

const DEFAULT_STATUS = 'Outline'

interface ChapterDialogProps {
  /** Present = edit an existing chapter; absent = create a new one. */
  chapter?: ChapterDto | null
  onClose(): void
}

/** Create or edit a chapter: title, status, and act in a single dialog,
 * replacing the plain title-only InputDialog. */
export function ChapterDialog({ chapter, onClose }: ChapterDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const isEdit = !!chapter
  const [title, setTitle] = useState(chapter?.title ?? '')
  const [status, setStatus] = useState(chapter?.status ?? DEFAULT_STATUS)
  const [act, setAct] = useState(chapter?.act ?? '')
  const [busy, setBusy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const chapterProps = definitions.filter((d) => d.scope === 'Chapter')
  const [values, setValues] = useState<Record<string, string>>({})

  useEffect(() => inputRef.current?.focus(), [])

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
    // A chapter that does not exist yet has no values to read; the fields
    // appear once it does, which is why they are only offered on edit.
    if (!chapter) return
    void rpc
      .request<Record<string, string>>('manuscriptProps/chapterValues', [chapter.guid])
      .then(setValues)
      .catch(() => setValues({}))
  }, [chapter])

  const submit = async (): Promise<void> => {
    const name = title.trim()
    if (!name || busy) return
    setBusy(true)
    const store = useProjectStore.getState()
    try {
      if (isEdit && chapter) {
        if (name !== chapter.title) await store.renameChapter(chapter.guid, name)
        if (status !== chapter.status) await store.setChapterStatus(chapter.guid, status)
        if (act.trim() !== chapter.act) await store.setChapterAct(chapter.guid, act.trim())
      } else {
        const prev = new Set(store.chapters.map((c) => c.guid))
        let state = await rpc.request<ProjectStateDto>('project/createChapter', [name])
        const created = state.chapters.find((c) => !prev.has(c.guid))
        if (created) {
          if (status !== DEFAULT_STATUS)
            state = await rpc.request<ProjectStateDto>('project/setChapterStatus', [
              created.guid,
              status
            ])
          if (act.trim())
            state = await rpc.request<ProjectStateDto>('project/setChapterAct', [
              created.guid,
              act.trim()
            ])
        }
        useProjectStore.getState().applyState(state)
      }
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && !busy && onClose()}
    >
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t(isEdit ? 'explorer.renameChapter' : 'dialog.newChapterTitle')}
        onKeyDown={(e) => {
          if (e.key === 'Escape' && !busy) onClose()
        }}
      >
        <div className="dialog-title">
          {t(isEdit ? 'explorer.renameChapter' : 'dialog.newChapterTitle')}
        </div>

        <label className="inspector-label">{t('dialog.chapterName')}</label>
        <input
          ref={inputRef}
          className="dialog-input"
          value={title}
          placeholder={t('dialog.chapterNameWatermark')}
          onChange={(e) => setTitle(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void submit()}
        />

        <label className="inspector-label">{t('dialog.status')}</label>
        <select
          className="dialog-input"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
        >
          {CHAPTER_STATUSES.map((s) => (
            <option key={s.value} value={s.value}>
              {t(s.labelKey)}
            </option>
          ))}
        </select>

        <label className="inspector-label">{t('dialog.act')}</label>
        <input
          className="dialog-input"
          value={act}
          placeholder={t('dialog.actWatermark')}
          onChange={(e) => setAct(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void submit()}
        />

        {/* The book's own chapter fields, written straight through rather
            than on submit: they are independent of the title and status this
            dialog otherwise commits together. */}
        {isEdit && chapter && chapterProps.length > 0 && (
          <>
            <label className="inspector-label">{t('props.sceneTitle')}</label>
            {chapterProps.map((property) => (
              <div key={property.key} className="chapter-prop-row">
                <span className="chapter-prop-label">{property.label}</span>
                <ManuscriptPropertyField
                  property={property}
                  className="dialog-input"
                  value={values[property.key] ?? ''}
                  onCommit={(value) => {
                    setValues({ ...values, [property.key]: value ?? '' })
                    void useManuscriptPropsStore
                      .getState()
                      .setChapterValue(chapter.guid, property.key, value)
                  }}
                />
              </div>
            ))}
          </>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy || !title.trim()}
            onClick={() => void submit()}
          >
            {t('dialog.ok')}
          </button>
        </div>
      </div>
    </div>
  )
}
