import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Camera, GitCompare, RotateCcw, Trash2 } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { InputDialog } from './InputDialog'
import { SnapshotCompareDialog } from './SnapshotCompareDialog'
import './shellDialogs.css'

interface SnapshotDto {
  id: string
  label: string
  takenAt: string
  wordCount: number
}

interface CompareView {
  idA: string
  idB: string
  labelA: string
  labelB: string
}

function snapshotLabel(snapshot: SnapshotDto | undefined): string {
  if (!snapshot) return ''
  return snapshot.label || snapshot.takenAt.slice(0, 10)
}

/**
 * Per-scene snapshot history dialog (take / restore / delete / compare),
 * mirroring the desktop Snapshots dialog opened from the toolbar.
 */
export function SnapshotsDialog({
  chapterGuid,
  sceneId,
  onClose
}: {
  chapterGuid: string
  sceneId: string
  onClose(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [snapshots, setSnapshots] = useState<SnapshotDto[]>([])
  const [labelPrompt, setLabelPrompt] = useState(false)
  const [compareBase, setCompareBase] = useState<string | null>(null)
  const [compareView, setCompareView] = useState<CompareView | null>(null)

  useEffect(() => {
    void rpc
      .request<SnapshotDto[]>('snapshots/list', [chapterGuid, sceneId])
      .then(setSnapshots)
      .catch(() => setSnapshots([]))
  }, [chapterGuid, sceneId])

  const onCompare = (snapshot: SnapshotDto): void => {
    if (!compareBase) {
      setCompareBase(snapshot.id)
      return
    }
    if (compareBase === snapshot.id) {
      setCompareBase(null)
      return
    }
    setCompareView({
      idA: compareBase,
      idB: snapshot.id,
      labelA: snapshotLabel(snapshots.find((s) => s.id === compareBase)),
      labelB: snapshotLabel(snapshot)
    })
    setCompareBase(null)
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t('snapshots.title')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="dialog-title">{t('snapshots.title')}</div>
        <button className="dialog-inline-button" onClick={() => setLabelPrompt(true)}>
          <Camera size={13} strokeWidth={2} />
          {t('snapshots.take')}
        </button>
        {compareBase && <div className="snapshot-compare-hint">{t('snapshots.comparePick')}</div>}
        <div className="snapshot-list">
          {snapshots.length === 0 && (
            <p className="dialog-empty">{t('snapshots.empty')}</p>
          )}
          {snapshots.map((snapshot) => (
            <div key={snapshot.id} className="snapshot-row">
              <span className="binder-scene-title">{snapshotLabel(snapshot)}</span>
              <span className="binder-scene-words">{snapshot.wordCount.toLocaleString()}</span>
              <div className="snapshot-actions">
                <button
                  className={`snapshot-action${compareBase === snapshot.id ? ' active' : ''}`}
                  title={t('snapshots.compare')}
                  aria-label={t('snapshots.compare')}
                  onClick={() => onCompare(snapshot)}
                >
                  <GitCompare size={13} strokeWidth={2} />
                </button>
                <button
                  className="snapshot-action"
                  title={t('snapshots.restore')}
                  aria-label={t('snapshots.restore')}
                  onClick={() =>
                    void rpc
                      .request<boolean>('snapshots/restore', [chapterGuid, sceneId, snapshot.id])
                      .then(async (restored) => {
                        if (restored) {
                          await useProjectStore.getState().openScene(chapterGuid, sceneId)
                        }
                      })
                  }
                >
                  <RotateCcw size={13} strokeWidth={2} />
                </button>
                <button
                  className="snapshot-action danger"
                  title={t('snapshots.delete')}
                  aria-label={t('snapshots.delete')}
                  onClick={() =>
                    void rpc
                      .request<SnapshotDto[]>('snapshots/delete', [chapterGuid, sceneId, snapshot.id])
                      .then((list) => {
                        setSnapshots(list)
                        if (compareBase === snapshot.id) setCompareBase(null)
                      })
                  }
                >
                  <Trash2 size={13} strokeWidth={2} />
                </button>
              </div>
            </div>
          ))}
        </div>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('dialog.close')}
          </button>
        </div>
      </div>
      {labelPrompt && (
        <InputDialog
          title={t('snapshots.take')}
          placeholder={t('snapshots.labelWatermark')}
          onCancel={() => setLabelPrompt(false)}
          onSubmit={(label) => {
            setLabelPrompt(false)
            void rpc
              .request<SnapshotDto[]>('snapshots/take', [chapterGuid, sceneId, label])
              .then(setSnapshots)
          }}
        />
      )}
      {compareView && (
        <SnapshotCompareDialog
          chapterGuid={chapterGuid}
          sceneId={sceneId}
          idA={compareView.idA}
          idB={compareView.idB}
          labelA={compareView.labelA}
          labelB={compareView.labelB}
          onClose={() => setCompareView(null)}
        />
      )}
    </div>
  )
}
