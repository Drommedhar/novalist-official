import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { MotionPresence } from './MotionPresence'
import { useDialogKeyboard } from './useDialogKeyboard'
import './scene-conflict.css'

interface MergeRow {
  mine: string | null
  theirs: string | null
  mineHtml: string | null
  theirsHtml: string | null
  /** "equal" | "changed" | "mine" | "theirs" */
  state: string
}

/**
 * Shown when a save was refused because the scene changed on disk underneath it.
 *
 * Novalist projects live in plain folders, which people put in Dropbox, iCloud
 * or Syncthing, so two machines editing one scene is ordinary. The prose is
 * never merged automatically: a sentence spliced from two drafts reads like
 * neither, so the writer sees both and picks, line by line if they want to.
 *
 * Dismissing decides nothing. Their text stays in the editor, still unsaved, and
 * the file on disk is untouched.
 */
export function SceneConflictDialog(): React.JSX.Element | null {
  const { t } = useTranslation()
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
  const conflict = useProjectStore((s) => s.sceneConflict)
  const resolve = useProjectStore((s) => s.resolveSceneConflict)
  const dismiss = useProjectStore((s) => s.dismissSceneConflict)
  const [rows, setRows] = useState<MergeRow[]>([])
  /** Which side each differing row contributes. Rows both sides agree on are
   *  not in here, because there is nothing to choose. */
  const [picks, setPicks] = useState<Record<number, 'mine' | 'theirs'>>({})
  const [busy, setBusy] = useState(false)
  const [loadedFor, setLoadedFor] = useState<typeof conflict>(null)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const keyboard = useDialogKeyboard(() => { if (!busy) dismiss() })

  useEffect(() => {
    let cancelled = false
    setRows([])
    setPicks({})
    setLoadedFor(null)
    setError(null)
    if (!conflict) {
      setRows([])
      setPicks({})
      return
    }
    void rpc
      .request<MergeRow[]>('scenes/mergeRows', [conflict.mine, conflict.theirs])
      .then((next) => {
        if (cancelled) return
        setRows(next)
        setLoadedFor(conflict)
        // Default to the writer's own text: they were the one typing, and a
        // default that silently prefers the other machine is the wrong surprise.
        const initial: Record<number, 'mine' | 'theirs'> = {}
        next.forEach((row, index) => {
          if (row.state !== 'equal') initial[index] = 'mine'
        })
        setPicks(initial)
      })
      .catch((reason: unknown) => { if (!cancelled) setError(String(reason)) })
    return () => { cancelled = true }
  }, [conflict, retry])

  if (!conflict) return <MotionPresence disabled={suspendMotion}>{null}</MotionPresence>

  const merged = (): string => {
    if (rows.every((row, index) => row.state === 'equal' || picks[index] === 'mine')) return conflict.mine
    if (rows.every((row, index) => row.state === 'equal' || picks[index] === 'theirs')) return conflict.theirs
    const lines: string[] = []
    rows.forEach((row, index) => {
      const side = row.state === 'equal' ? 'mine' : picks[index]
      const text = side === 'theirs' ? row.theirsHtml : row.mineHtml
      if (text !== null && text !== undefined) lines.push(text)
    })
    return lines.join('')
  }

  const takeAll = (side: 'mine' | 'theirs'): void => {
    const next: Record<number, 'mine' | 'theirs'> = {}
    rows.forEach((row, index) => {
      if (row.state !== 'equal') next[index] = side
    })
    setPicks(next)
  }

  const apply = async (): Promise<void> => {
    if (busy || loadedFor !== conflict) return
    setBusy(true)
    setError(null)
    try {
      await resolve(merged())
    } catch (reason) {
      setError(String(reason))
    } finally {
      setBusy(false)
    }
  }

  const differing = rows.filter((r) => r.state !== 'equal').length

  return (
    <MotionPresence disabled={suspendMotion}>
    <div className="dialog-overlay">
      <div {...keyboard} className="dialog-card scene-conflict-card" role="dialog" aria-modal="true" aria-label={t('conflict.title')}>
        <div className="dialog-title">{t('conflict.title')}</div>
        <p className="dialog-message">{t('conflict.explain', { count: differing })}</p>
        {loadedFor !== conflict && !error && <p role="status">{t('splash.loading')}</p>}
        {error && <p role="alert">{error}</p>}
        {error && loadedFor !== conflict && <button className="dialog-button" onClick={() => setRetry((value) => value + 1)}>{t('shell.retry')}</button>}

        <div className="scene-conflict-actions">
          <button className="dialog-button" disabled={busy || loadedFor !== conflict} onClick={() => takeAll('mine')}>
            {t('conflict.takeAllMine')}
          </button>
          <button className="dialog-button" disabled={busy || loadedFor !== conflict} onClick={() => takeAll('theirs')}>
            {t('conflict.takeAllTheirs')}
          </button>
        </div>

        <div className="scene-conflict-heads">
          <span>{t('conflict.mine')}</span>
          <span>{t('conflict.theirs')}</span>
        </div>

        <div className="scene-conflict-rows">
          {rows.map((row, index) => (
            <div
              key={index}
              className={`scene-conflict-row ${row.state}`}
            >
              <button
                className={`scene-conflict-cell${
                  row.state !== 'equal' && picks[index] === 'mine' ? ' chosen' : ''
                }`}
                disabled={busy || row.state === 'equal'}
                onClick={() => setPicks({ ...picks, [index]: 'mine' })}
              >
                {row.mine ?? ''}
              </button>
              <button
                className={`scene-conflict-cell${
                  row.state !== 'equal' && picks[index] === 'theirs' ? ' chosen' : ''
                }`}
                disabled={busy || row.state === 'equal'}
                onClick={() => setPicks({ ...picks, [index]: 'theirs' })}
              >
                {row.theirs ?? ''}
              </button>
            </div>
          ))}
        </div>

        <p className="match-hint">{t('conflict.snapshotNote')}</p>

        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={dismiss}>
            {t('conflict.decideLater')}
          </button>
          <button className="dialog-button danger" disabled={busy || loadedFor !== conflict} onClick={() => void apply()}>
            {t('conflict.save')}
          </button>
        </div>
      </div>
    </div>
    </MotionPresence>
  )
}
