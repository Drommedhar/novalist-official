import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Camera, GitCompare, Pencil, RotateCcw, Trash2 } from 'lucide-react'
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

/** A snapshot in the project-wide list, which knows the scene it came from. */
interface ProjectSnapshotDto extends SnapshotDto {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
}

/**
 * What the backend labels a Replace All run's snapshots with, before the
 * timestamp that separates one run from the next. Must match
 * FindReplaceService.SnapshotBatchPrefix; it is not a translated string,
 * because it is written into files that outlive the current UI language.
 */
const BATCH_PREFIX = 'Before find/replace'

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
  const [wholeProject, setWholeProject] = useState(false)
  const [all, setAll] = useState<ProjectSnapshotDto[]>([])
  const [renaming, setRenaming] = useState<ProjectSnapshotDto | null>(null)
  const [pruned, setPruned] = useState<number | null>(null)

  useEffect(() => {
    void rpc
      .request<SnapshotDto[]>('snapshots/list', [chapterGuid, sceneId])
      .then(setSnapshots)
      .catch(() => setSnapshots([]))
  }, [chapterGuid, sceneId])

  const loadAll = (): void => {
    void rpc
      .request<ProjectSnapshotDto[]>('snapshots/all')
      .then(setAll)
      .catch(() => setAll([]))
  }

  useEffect(() => {
    if (wholeProject) loadAll()
  }, [wholeProject])

  /**
   * Auto-snapshot runs, newest first, with how many each left behind.
   *
   * The prefix is written by the backend when a Replace All starts, and every
   * snapshot from that one run carries the same label - which is what makes a
   * run separable from the one before it.
   */
  const batches = Object.entries(
    all
      .filter((s) => s.label.startsWith(BATCH_PREFIX))
      .reduce<Record<string, number>>((acc, s) => {
        acc[s.label] = (acc[s.label] ?? 0) + 1
        return acc
      }, {})
  ).sort(([a], [b]) => b.localeCompare(a))

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
        <div className="snapshot-scope">
          <button
            className={`dialog-inline-button${wholeProject ? '' : ' active'}`}
            onClick={() => setWholeProject(false)}
          >
            {t('snapshots.scopeScene')}
          </button>
          <button
            className={`dialog-inline-button${wholeProject ? ' active' : ''}`}
            onClick={() => setWholeProject(true)}
          >
            {t('snapshots.scopeProject')}
          </button>
        </div>
        {!wholeProject && (
          <button className="dialog-inline-button" onClick={() => setLabelPrompt(true)}>
            <Camera size={13} strokeWidth={2} />
            {t('snapshots.take')}
          </button>
        )}
        {wholeProject && (
          <>
            <p className="dialog-empty">{t('snapshots.pruneDesc')}</p>
            <div className="snapshot-scope">
              <button
                className="dialog-inline-button"
                onClick={() =>
                  void rpc
                    .request<number>('snapshots/prune', [5, 0, true])
                    .then((count) => {
                      setPruned(count)
                      loadAll()
                    })
                }
              >
                <Trash2 size={13} strokeWidth={2} />
                {t('snapshots.pruneKeepFive')}
              </button>
              <button
                className="dialog-inline-button"
                onClick={() =>
                  void rpc
                    .request<number>('snapshots/prune', [0, 90, true])
                    .then((count) => {
                      setPruned(count)
                      loadAll()
                    })
                }
              >
                <Trash2 size={13} strokeWidth={2} />
                {t('snapshots.pruneOld')}
              </button>
            </div>
            {/* One Replace All over a long book snapshots every scene it
                touches, and those are the ones that pile up. Each run labels
                its own, so a run can be cleared without touching the snapshots
                taken deliberately. */}
            {batches.length > 0 && (
              <>
                <p className="dialog-empty">{t('snapshots.batchesDesc')}</p>
                {batches.map(([label, count]) => (
                  <div key={label} className="snapshot-row snapshot-batch">
                    <span className="binder-scene-title">{label}</span>
                    <span className="binder-scene-words">
                      {t('snapshots.batchCount', { count })}
                    </span>
                    <div className="snapshot-actions">
                      <button
                        className="snapshot-action danger"
                        title={t('snapshots.deleteBatch')}
                        aria-label={t('snapshots.deleteBatch')}
                        onClick={() =>
                          void rpc
                            .request<number>('snapshots/deleteByLabel', [label])
                            .then((removed) => {
                              setPruned(removed)
                              loadAll()
                            })
                        }
                      >
                        <Trash2 size={13} strokeWidth={2} />
                      </button>
                    </div>
                  </div>
                ))}
              </>
            )}
            {pruned !== null && (
              <p className="dialog-empty">{t('snapshots.pruned', { count: pruned })}</p>
            )}
          </>
        )}
        {compareBase && <div className="snapshot-compare-hint">{t('snapshots.comparePick')}</div>}
        {wholeProject && (
          <div className="snapshot-list">
            {all.length === 0 && <p className="dialog-empty">{t('snapshots.empty')}</p>}
            {all.map((snapshot) => (
              <div key={snapshot.id} className="snapshot-row">
                <span className="binder-scene-title">
                  {snapshot.chapterTitle} - {snapshot.sceneTitle}: {snapshotLabel(snapshot)}
                </span>
                <span className="binder-scene-words">{snapshot.wordCount.toLocaleString()}</span>
                <div className="snapshot-actions">
                  <button
                    className="snapshot-action"
                    title={t('snapshots.rename')}
                    aria-label={t('snapshots.rename')}
                    onClick={() => setRenaming(snapshot)}
                  >
                    <Pencil size={13} strokeWidth={2} />
                  </button>
                  <button
                    className="snapshot-action danger"
                    title={t('snapshots.delete')}
                    aria-label={t('snapshots.delete')}
                    onClick={() =>
                      void rpc
                        .request('snapshots/delete', [
                          snapshot.chapterGuid,
                          snapshot.sceneId,
                          snapshot.id
                        ])
                        .then(loadAll)
                    }
                  >
                    <Trash2 size={13} strokeWidth={2} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
        <div className="snapshot-list" hidden={wholeProject}>
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
      {renaming && (
        <InputDialog
          title={t('snapshots.rename')}
          placeholder={t('snapshots.labelWatermark')}
          initialValue={renaming.label}
          onCancel={() => setRenaming(null)}
          onSubmit={(label) => {
            const target = renaming
            setRenaming(null)
            void rpc
              .request<boolean>('snapshots/rename', [
                target.chapterGuid,
                target.sceneId,
                target.id,
                label
              ])
              .then(loadAll)
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
