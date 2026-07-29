import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowLeftRight, Download, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

interface DraftChoice {
  id: string
  name: string
  isActive: boolean
  parentDraftId: string
}

type SceneState = 'same' | 'changed' | 'added' | 'removed'

interface DraftScene {
  sceneId: string
  title: string
  chapterGuid: string
  chapterTitle: string
  state: SceneState
  leftWords: number
  rightWords: number
}

interface DraftComparison {
  leftDraftId: string
  leftName: string
  rightDraftId: string
  rightName: string
  scenes: DraftScene[]
  sameCount: number
  changedCount: number
  addedCount: number
  removedCount: number
  leftWords: number
  rightWords: number
}

interface DiffRow {
  left: string | null
  right: string | null
  state: 'equal' | 'changed' | 'left' | 'right'
}

/**
 * Two drafts of the same book, scene by scene, with a line diff of whichever
 * scene is selected. Cloning a draft was always one click; this is the other
 * half - seeing what the rewrite actually changed, and bringing one scene of it
 * back across.
 */
export function DraftCompareDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const [drafts, setDrafts] = useState<DraftChoice[]>([])
  const [leftId, setLeftId] = useState('')
  const [rightId, setRightId] = useState('')
  const [comparison, setComparison] = useState<DraftComparison | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [rows, setRows] = useState<DiffRow[] | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void rpc.request<DraftChoice[]>('draftCompare/drafts').then((list) => {
      setDrafts(list)
      const active = list.find((d) => d.isActive)
      // The obvious pair to open on: where the active draft came from against
      // the active draft itself. That is the comparison anyone who just
      // finished a rewrite wants.
      const parent = active?.parentDraftId
        ? list.find((d) => d.id === active.parentDraftId)
        : list.find((d) => !d.isActive)
      setRightId(active?.id ?? list[0]?.id ?? '')
      setLeftId(parent?.id ?? '')
    })
  }, [])

  const compare = useCallback(async () => {
    if (!leftId || !rightId || leftId === rightId) {
      setComparison(null)
      return
    }
    setComparison(await rpc.request<DraftComparison | null>('draftCompare/compare', [leftId, rightId]))
    setSelected(null)
    setRows(null)
  }, [leftId, rightId])

  useEffect(() => {
    void compare()
  }, [compare])

  const openScene = async (sceneId: string): Promise<void> => {
    setSelected(sceneId)
    setRows(null)
    setRows(await rpc.request<DiffRow[]>('draftCompare/scene', [leftId, rightId, sceneId]))
  }

  const take = async (scene: DraftScene): Promise<void> => {
    // Overwrites prose in the draft the writer is actually in. It snapshots
    // first, so this is recoverable, but they still get told before it happens.
    if (!window.confirm(t('draftCompare.takeConfirm', { title: scene.title }))) return
    setBusy(true)
    try {
      await rpc.request<boolean>('draftCompare/take', [leftId, scene.sceneId])
      await useProjectStore.getState().flushPendingSave()
      useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
      await compare()
    } finally {
      setBusy(false)
    }
  }

  const swap = (): void => {
    setLeftId(rightId)
    setRightId(leftId)
  }

  const activeId = useMemo(() => drafts.find((d) => d.isActive)?.id ?? '', [drafts])
  const scenes = comparison?.scenes ?? []
  const selectedScene = scenes.find((s) => s.sceneId === selected)

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="draft-compare-card" role="dialog" aria-label={t('draftCompare.title')}>
        <div className="snapshot-compare-head">
          <span className="dialog-title">{t('draftCompare.title')}</span>
          <button className="ctx-reset" aria-label={t('dialog.close')} onClick={onClose}>
            <X size={16} strokeWidth={2} />
          </button>
        </div>

        <div className="draft-compare-pickers">
          <select
            className="inspector-input"
            aria-label={t('draftCompare.left')}
            value={leftId}
            onChange={(e) => setLeftId(e.target.value)}
          >
            <option value="">{t('draftCompare.pick')}</option>
            {drafts.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
          <button className="ctx-reset" aria-label={t('draftCompare.swap')} onClick={swap}>
            <ArrowLeftRight size={14} />
          </button>
          <select
            className="inspector-input"
            aria-label={t('draftCompare.right')}
            value={rightId}
            onChange={(e) => setRightId(e.target.value)}
          >
            <option value="">{t('draftCompare.pick')}</option>
            {drafts.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
        </div>

        {comparison === null ? (
          <div className="snapshot-diff-empty">{t('draftCompare.pickTwo')}</div>
        ) : (
          <>
            <div className="draft-compare-summary">
              {t('draftCompare.summary', {
                changed: comparison.changedCount,
                added: comparison.addedCount,
                removed: comparison.removedCount,
                same: comparison.sameCount
              })}
              {' · '}
              {t('draftCompare.words', {
                left: comparison.leftWords.toLocaleString(),
                right: comparison.rightWords.toLocaleString()
              })}
            </div>

            <div className="draft-compare-body">
              <div className="draft-compare-scenes">
                {scenes.length === 0 && (
                  <div className="settings-hint">{t('draftCompare.noScenes')}</div>
                )}
                {scenes.map((s) => (
                  <button
                    key={s.sceneId}
                    className={`draft-compare-scene${s.sceneId === selected ? ' selected' : ''} state-${s.state}`}
                    onClick={() => void openScene(s.sceneId)}
                  >
                    <span className="draft-compare-scene-title">{s.title}</span>
                    <span className="draft-compare-scene-meta">
                      {s.chapterTitle} · {t(`draftCompare.state.${s.state}`)}
                      {s.state === 'changed' && ` · ${s.leftWords} / ${s.rightWords}`}
                    </span>
                  </button>
                ))}
              </div>

              <div className="draft-compare-diff">
                {selectedScene == null ? (
                  <div className="snapshot-diff-empty">{t('draftCompare.pickScene')}</div>
                ) : (
                  <>
                    <div className="draft-compare-diff-head">
                      <span className="snapshot-compare-col">{comparison.leftName}</span>
                      <span className="snapshot-compare-col">{comparison.rightName}</span>
                    </div>
                    {rightId === activeId && selectedScene.state !== 'same' && (
                      <button
                        className="dialog-button"
                        disabled={busy}
                        onClick={() => void take(selectedScene)}
                      >
                        <Download size={14} /> {t('draftCompare.take', { draft: comparison.leftName })}
                      </button>
                    )}
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
                  </>
                )}
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
