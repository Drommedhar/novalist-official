import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import type { ProjectStateDto } from '../stores/projectStore'

interface ShiftRow {
  sceneId: string
  title: string
  before: string
  after: string
}

interface BulkResult {
  count: number
  state: ProjectStateDto
}

/**
 * Moves every selected scene's in-world dates by a number of days.
 *
 * The preview is not decoration: it is the only way to see what the shift does
 * to a custom calendar, where month lengths are the writer's own and "plus
 * three days" is not a calculation they can do in their head. Nothing is
 * written until Apply.
 */
export function ShiftDatesDialog(props: {
  sceneIds: string[]
  onClose: () => void
  onApplied: (state: ProjectStateDto) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [days, setDays] = useState(1)
  const [rows, setRows] = useState<ShiftRow[]>([])
  const [busy, setBusy] = useState(false)

  const { sceneIds } = props

  const preview = useCallback(async () => {
    setRows(await rpc.request<ShiftRow[]>('sceneBulk/previewDateShift', [sceneIds, days]))
  }, [sceneIds, days])

  useEffect(() => {
    void preview()
  }, [preview])

  const apply = async (): Promise<void> => {
    setBusy(true)
    try {
      const result = await rpc.request<BulkResult>('sceneBulk/shiftDates', [sceneIds, days])
      props.onApplied(result.state)
    } finally {
      setBusy(false)
    }
  }

  const movable = rows.filter((r) => r.before !== r.after).length

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="dialog-card shift-dates-dialog" role="dialog" aria-label={t('bulk.shiftDates')}>
        <div className="dialog-title">{t('bulk.shiftDates')}</div>

        <div className="shift-dates-controls">
          <label className="inspector-label" htmlFor="shift-days">
            {t('bulk.shiftBy')}
          </label>
          <input
            id="shift-days"
            className="inspector-input shift-dates-days"
            type="number"
            value={days}
            onChange={(e) => setDays(Number(e.target.value) || 0)}
          />
        </div>

        <p className="match-hint">{t('bulk.shiftSummary', { moved: movable, total: rows.length })}</p>

        <div className="shift-dates-list">
          {rows.map((row) => (
            <div
              key={row.sceneId}
              className={`shift-dates-row${row.before === row.after ? ' unchanged' : ''}`}
            >
              <span className="shift-dates-title">{row.title}</span>
              <span className="shift-dates-before">{row.before || t('bulk.noDate')}</span>
              <span className="shift-dates-arrow">&#8594;</span>
              <span className="shift-dates-after">{row.after || t('bulk.noDate')}</span>
            </div>
          ))}
        </div>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={props.onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy || movable === 0}
            onClick={() => void apply()}
          >
            {t('bulk.applyShift')}
          </button>
        </div>
      </div>
    </div>
  )
}
