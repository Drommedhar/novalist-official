import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronUp } from 'lucide-react'
import { rpc } from '../../rpc/client'
import '../../shell/shellDialogs.css'

interface SheetDto {
  hidden: string[]
  order: string[]
}

/**
 * Which fields this entry type's sheet shows, and in what order.
 *
 * The built-in field sets are fixed and were always all shown, so a project
 * that never records eye colour carried the field on every character for ever.
 * Hiding one keeps its value: the field is out of the way, not gone, which is
 * the difference between a preference and a trap.
 */
export function ArrangeFieldsDialog({
  entityType,
  fields,
  labels,
  onClose
}: {
  entityType: string
  /** Every field key the sheet can show, in the order Novalist ships them. */
  fields: string[]
  /** The label to show for each key. */
  labels: Record<string, string>
  onClose(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [hidden, setHidden] = useState<string[]>([])
  const [order, setOrder] = useState<string[]>(fields)

  useEffect(() => {
    void rpc
      .request<SheetDto>('sheets/get', [entityType])
      .then((sheet) => {
        setHidden(sheet.hidden)
        // A saved order may predate a field Novalist has added since, so the
        // shipped list is appended rather than replaced.
        setOrder([...sheet.order.filter((k) => fields.includes(k)),
          ...fields.filter((k) => !sheet.order.includes(k))])
      })
      .catch(() => undefined)
    // fields is a constant for a given type; re-running on it would fight the
    // ordering the writer is in the middle of.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityType])

  const move = (index: number, by: number): void => {
    const next = [...order]
    const target = index + by
    if (target < 0 || target >= next.length) return
    ;[next[index], next[target]] = [next[target], next[index]]
    setOrder(next)
  }

  const save = (nextHidden: string[], nextOrder: string[]): void => {
    setHidden(nextHidden)
    setOrder(nextOrder)
    void rpc.request('sheets/save', [entityType, nextHidden, nextOrder])
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card" role="dialog" aria-label={t('entityEditor.arrangeTitle')}>
        <div className="dialog-title">{t('entityEditor.arrangeTitle')}</div>
        <p className="settings-hint">{t('entityEditor.arrangeIntro')}</p>

        <div className="snapshot-list">
          {order.map((key, index) => (
            <div key={key} className="match-row">
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  // The name is what an entry is; hiding it would leave a sheet
                  // nobody can tell apart from another.
                  disabled={key === 'name'}
                  checked={!hidden.includes(key)}
                  onChange={(e) =>
                    save(
                      e.target.checked ? hidden.filter((k) => k !== key) : [...hidden, key],
                      order
                    )
                  }
                />
                {labels[key] ?? key}
              </label>
              <button
                className="match-remove"
                title={t('entityEditor.arrangeUp')}
                aria-label={`${t('entityEditor.arrangeUp')}: ${labels[key] ?? key}`}
                disabled={index === 0}
                onClick={() => move(index, -1)}
              >
                <ChevronUp size={13} strokeWidth={2} />
              </button>
              <button
                className="match-remove"
                title={t('entityEditor.arrangeDown')}
                aria-label={`${t('entityEditor.arrangeDown')}: ${labels[key] ?? key}`}
                disabled={index === order.length - 1}
                onClick={() => move(index, 1)}
              >
                <ChevronDown size={13} strokeWidth={2} />
              </button>
            </div>
          ))}
        </div>

        <div className="dialog-actions">
          <button
            className="dialog-button primary"
            onClick={() => {
              save(hidden, order)
              onClose()
            }}
          >
            {t('dialog.close')}
          </button>
        </div>
      </div>
    </div>
  )
}
