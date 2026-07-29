import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import './scene-conflict.css'

interface MergeRow {
  mine: string | null
  theirs: string | null
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
  const conflict = useProjectStore((s) => s.sceneConflict)
  const resolve = useProjectStore((s) => s.resolveSceneConflict)
  const dismiss = useProjectStore((s) => s.dismissSceneConflict)
  const [rows, setRows] = useState<MergeRow[]>([])
  /** Which side each differing row contributes. Rows both sides agree on are
   *  not in here, because there is nothing to choose. */
  const [picks, setPicks] = useState<Record<number, 'mine' | 'theirs'>>({})
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!conflict) {
      setRows([])
      setPicks({})
      return
    }
    void rpc
      .request<MergeRow[]>('scenes/mergeRows', [conflict.mine, conflict.theirs])
      .then((next) => {
        setRows(next)
        // Default to the writer's own text: they were the one typing, and a
        // default that silently prefers the other machine is the wrong surprise.
        const initial: Record<number, 'mine' | 'theirs'> = {}
        next.forEach((row, index) => {
          if (row.state !== 'equal') initial[index] = 'mine'
        })
        setPicks(initial)
      })
      .catch(() => setRows([]))
  }, [conflict])

  if (!conflict) return null

  const merged = (): string => {
    const lines: string[] = []
    rows.forEach((row, index) => {
      const side = row.state === 'equal' ? 'mine' : picks[index]
      const text = side === 'theirs' ? row.theirs : row.mine
      if (text !== null && text !== undefined) lines.push(text)
    })
    // Back to the paragraph markup the editor speaks. The merge view works in
    // prose because that is what the writer is choosing between; a tag-level
    // diff would bury the actual difference.
    return lines
      .map((line) => `<p>${escapeHtml(line)}</p>`)
      .join('')
  }

  const takeAll = (side: 'mine' | 'theirs'): void => {
    const next: Record<number, 'mine' | 'theirs'> = {}
    rows.forEach((row, index) => {
      if (row.state !== 'equal') next[index] = side
    })
    setPicks(next)
  }

  const apply = async (): Promise<void> => {
    setBusy(true)
    try {
      await resolve(merged())
    } finally {
      setBusy(false)
    }
  }

  const differing = rows.filter((r) => r.state !== 'equal').length

  return (
    <div className="dialog-overlay">
      <div className="dialog-card scene-conflict-card" role="dialog" aria-label={t('conflict.title')}>
        <div className="dialog-title">{t('conflict.title')}</div>
        <p className="dialog-message">{t('conflict.explain', { count: differing })}</p>

        <div className="scene-conflict-actions">
          <button className="dialog-button" onClick={() => takeAll('mine')}>
            {t('conflict.takeAllMine')}
          </button>
          <button className="dialog-button" onClick={() => takeAll('theirs')}>
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
                disabled={row.state === 'equal'}
                onClick={() => setPicks({ ...picks, [index]: 'mine' })}
              >
                {row.mine ?? ''}
              </button>
              <button
                className={`scene-conflict-cell${
                  row.state !== 'equal' && picks[index] === 'theirs' ? ' chosen' : ''
                }`}
                disabled={row.state === 'equal'}
                onClick={() => setPicks({ ...picks, [index]: 'theirs' })}
              >
                {row.theirs ?? ''}
              </button>
            </div>
          ))}
        </div>

        <p className="match-hint">{t('conflict.snapshotNote')}</p>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={dismiss}>
            {t('conflict.decideLater')}
          </button>
          <button className="dialog-button danger" disabled={busy} onClick={() => void apply()}>
            {t('conflict.save')}
          </button>
        </div>
      </div>
    </div>
  )
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}
