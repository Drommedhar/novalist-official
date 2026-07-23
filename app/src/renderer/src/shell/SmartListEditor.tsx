import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import type { SmartListDto } from './SmartListsPanel'

export interface SmartListDraft {
  name: string
  chapterStatus: string | null
  povContains: string | null
  tag: string | null
  plotlineId: string | null
}

const STATUSES = ['', 'Outline', 'FirstDraft', 'Revised', 'Edited', 'Final']

interface SmartListEditorProps {
  initial: SmartListDto | null
  onSubmit(draft: SmartListDraft): void
  onCancel(): void
}

export function SmartListEditor({
  initial,
  onSubmit,
  onCancel
}: SmartListEditorProps): React.JSX.Element {
  const { t } = useTranslation()
  const [name, setName] = useState(initial?.name ?? '')
  const [status, setStatus] = useState(initial?.chapterStatus ?? '')
  const [pov, setPov] = useState(initial?.povContains ?? '')
  const [tag, setTag] = useState(initial?.tag ?? '')
  const [plotlineId, setPlotlineId] = useState(initial?.plotlineId ?? '')
  const [plotlines, setPlotlines] = useState<{ id: string; name: string }[]>([])

  // The plotline filter is only meaningful once the book has plotlines.
  useEffect(() => {
    let cancelled = false
    void rpc
      .request<{ plotlines: { id: string; name: string }[] }>('plot/grid')
      .then((grid) => {
        if (!cancelled) setPlotlines(grid.plotlines)
      })
      .catch(() => {
        // No plotlines available — the dropdown simply stays hidden.
      })
    return () => {
      cancelled = true
    }
  }, [])

  const submit = (): void => {
    if (name.trim().length === 0) return
    onSubmit({
      name: name.trim(),
      chapterStatus: status || null,
      povContains: pov.trim() || null,
      tag: tag.trim() || null,
      plotlineId: plotlineId || null
    })
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={t('smartList.editTitle')}>
        <div className="dialog-title">{t('smartList.editTitle')}</div>
        <label className="inspector-label" htmlFor="sl-name">
          {t('smartList.name')}
        </label>
        <input
          id="sl-name"
          className="dialog-input"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
        <label className="inspector-label" htmlFor="sl-status">
          {t('smartList.chapterStatus')}
        </label>
        <select
          id="sl-status"
          className="dialog-input"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
        >
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s === '' ? t('smartList.anyStatus') : t(`dashboard.status${s}`)}
            </option>
          ))}
        </select>
        <label className="inspector-label" htmlFor="sl-pov">
          {t('smartList.povContains')}
        </label>
        <input
          id="sl-pov"
          className="dialog-input"
          value={pov}
          onChange={(e) => setPov(e.target.value)}
        />
        <label className="inspector-label" htmlFor="sl-tag">
          {t('smartList.tag')}
        </label>
        <input
          id="sl-tag"
          className="dialog-input"
          value={tag}
          onChange={(e) => setTag(e.target.value)}
        />
        {plotlines.length > 0 && (
          <>
            <label className="inspector-label" htmlFor="sl-plotline">
              {t('smartList.plotline')}
            </label>
            <select
              id="sl-plotline"
              className="dialog-input"
              value={plotlineId}
              onChange={(e) => setPlotlineId(e.target.value)}
            >
              <option value="">{t('smartList.anyPlotline')}</option>
              {plotlines.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </>
        )}
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={submit}>
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
