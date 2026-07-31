import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { History } from 'lucide-react'
import { rpc } from '../rpc/client'

interface Revision {
  id: string
  savedAt: string
  sizeBytes: number
}

/**
 * What something said before its last few saves.
 *
 * Snapshots covered scenes and nothing else, so typing over a character sheet,
 * a plot thread or a research note had no answer inside the app - the remedy
 * in the manual was a backup of the whole project.
 *
 * The list and the restore are named by the caller because each kind of thing
 * is stored differently and only its own service knows how to put one back.
 * The panel itself is the same everywhere, which is the point: a writer should
 * not have to learn a second way to undo.
 */
export function RevisionsPanel({
  historyMethod,
  restoreMethod,
  targetId,
  restoreArgs,
  onRestored
}: {
  historyMethod: string
  restoreMethod: string
  targetId: string
  /** Everything the restore needs before the revision id. */
  restoreArgs: unknown[]
  onRestored?: (result: unknown) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [revisions, setRevisions] = useState<Revision[]>([])
  const [busy, setBusy] = useState(false)

  const load = (): void => {
    void rpc
      .request<Revision[]>(historyMethod, [targetId])
      .then(setRevisions)
      .catch(() => setRevisions([]))
  }

  useEffect(load, [historyMethod, targetId])

  const restore = async (revisionId: string): Promise<void> => {
    setBusy(true)
    try {
      const result = await rpc.request<unknown>(restoreMethod, [...restoreArgs, revisionId])
      onRestored?.(result)
      // The state just replaced became a revision of its own, so an unwanted
      // restore is undoable in the same list.
      load()
    } finally {
      setBusy(false)
    }
  }

  if (revisions.length === 0) {
    return <p className="inspector-meta">{t('entityHistory.none')}</p>
  }

  return (
    <div className="entity-history">
      <p className="inspector-meta">{t('entityHistory.intro')}</p>
      {revisions.map((revision) => (
        <div key={revision.id} className="entity-history-row">
          <span className="entity-history-when">
            {new Date(revision.savedAt).toLocaleString()}
          </span>
          <button
            className="dialog-button"
            disabled={busy}
            onClick={() => void restore(revision.id)}
          >
            <History size={12} strokeWidth={2} />
            {t('entityHistory.restore')}
          </button>
        </div>
      ))}
    </div>
  )
}
