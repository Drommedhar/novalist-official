import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import type { SceneEditDto } from './sceneEdit'
import './shellDialogs.css'

interface StoryDateRangeDialogProps {
  chapterGuid: string
  sceneId: string
  onClose(): void
}

/** Set a scene's in-world story date, with an optional end date and note.
 * Persisted through project/setSceneDateRange (Novalist.Core StoryDateRange). */
export function StoryDateRangeDialog({
  chapterGuid,
  sceneId,
  onClose
}: StoryDateRangeDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let active = true
    void rpc
      .request<SceneEditDto>('project/getSceneEdit', [chapterGuid, sceneId])
      .then((meta) => {
        if (!active) return
        setStart(meta.dateStart)
        setEnd(meta.dateEnd)
        setNote(meta.dateNote)
      })
    return () => {
      active = false
    }
  }, [chapterGuid, sceneId])

  const persist = async (s: string, e: string, n: string): Promise<void> => {
    if (busy) return
    setBusy(true)
    try {
      await rpc.request('project/setSceneDateRange', [chapterGuid, sceneId, s, e, n])
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
        aria-label={t('explorer.contextSetDate')}
        onKeyDown={(e) => {
          if (e.key === 'Escape' && !busy) onClose()
        }}
      >
        <div className="dialog-title">{t('explorer.contextSetDate')}</div>

        <div className="date-range-row">
          <div className="date-range-col">
            <label className="inspector-label">{t('dialog.dateStart')}</label>
            <input
              type="date"
              className="dialog-input"
              value={start}
              onChange={(e) => setStart(e.target.value)}
            />
          </div>
          <div className="date-range-col">
            <label className="inspector-label">{t('dialog.dateEnd')}</label>
            <input
              type="date"
              className="dialog-input"
              value={end}
              onChange={(e) => setEnd(e.target.value)}
            />
          </div>
        </div>

        <label className="inspector-label">{t('dialog.dateNoteWatermark')}</label>
        <input
          className="dialog-input"
          value={note}
          placeholder={t('dialog.dateNoteWatermark')}
          onChange={(e) => setNote(e.target.value)}
        />

        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button"
            disabled={busy}
            onClick={() => void persist('', '', '')}
          >
            {t('dialog.clear')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy}
            onClick={() => void persist(start, end, note)}
          >
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
