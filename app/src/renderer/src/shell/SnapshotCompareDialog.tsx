import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { rpc } from '../rpc/client'

interface DiffRow {
  left: string | null
  right: string | null
  state: 'equal' | 'changed' | 'left' | 'right'
}

interface SnapshotCompareDialogProps {
  chapterGuid: string
  sceneId: string
  idA: string
  idB: string
  labelA: string
  labelB: string
  onClose(): void
}

/** Read-only side-by-side line diff of two scene snapshots. The left column is
 * snapshot A, the right column is snapshot B; removed lines are tinted, added
 * lines highlighted. */
export function SnapshotCompareDialog({
  chapterGuid,
  sceneId,
  idA,
  idB,
  labelA,
  labelB,
  onClose
}: SnapshotCompareDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const [rows, setRows] = useState<DiffRow[] | null>(null)

  useEffect(() => {
    void rpc
      .request<DiffRow[]>('snapshots/diff', [chapterGuid, sceneId, idA, idB])
      .then(setRows)
      .catch(() => setRows([]))
  }, [chapterGuid, sceneId, idA, idB])

  const identical = rows !== null && rows.every((r) => r.state === 'equal')

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="snapshot-compare-card" role="dialog" aria-label={t('snapshots.compareTitle')}>
        <div className="snapshot-compare-head">
          <span className="dialog-title">{t('snapshots.compareTitle')}</span>
          <button className="ctx-reset" aria-label={t('dialog.close')} onClick={onClose}>
            <X size={16} strokeWidth={2} />
          </button>
        </div>
        <div className="snapshot-compare-cols">
          <span className="snapshot-compare-col" title={labelA}>
            {labelA}
          </span>
          <span className="snapshot-compare-col" title={labelB}>
            {labelB}
          </span>
        </div>
        {rows !== null && identical ? (
          <div className="snapshot-diff-empty">{t('snapshots.compareIdentical')}</div>
        ) : (
          <div className="snapshot-diff">
            {(rows ?? []).map((row, i) => (
              <div key={i} className="snapshot-diff-row">
                <span
                  className={`snapshot-diff-cell left${
                    row.state === 'changed' || row.state === 'left' ? ' removed' : ''
                  }`}
                >
                  {row.left ?? ''}
                </span>
                <span
                  className={`snapshot-diff-cell${
                    row.state === 'changed' || row.state === 'right' ? ' added' : ''
                  }`}
                >
                  {row.right ?? ''}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
