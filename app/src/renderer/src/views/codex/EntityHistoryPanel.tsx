import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { History } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'

interface Revision {
  id: string
  savedAt: string
  sizeBytes: number
}

/**
 * What this entry said before its last few saves.
 *
 * Snapshots covered scenes and nothing else, so typing the wrong eye colour
 * over the right one had no answer inside the app - the remedy in the manual
 * was a backup of the whole project.
 */
export function EntityHistoryPanel({
  entityType,
  entityId
}: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [revisions, setRevisions] = useState<Revision[]>([])
  const [busy, setBusy] = useState(false)

  const load = (): void => {
    void rpc
      .request<Revision[]>('entities/history', [entityId])
      .then(setRevisions)
      .catch(() => setRevisions([]))
  }

  useEffect(load, [entityId])

  const restore = async (revisionId: string): Promise<void> => {
    setBusy(true)
    try {
      const record = await rpc.request<Record<string, unknown>>('entities/restoreRevision', [
        entityType,
        entityId,
        revisionId
      ])
      useCodexStore.setState({ selectedRecord: record })
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
