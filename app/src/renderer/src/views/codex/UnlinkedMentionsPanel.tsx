import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link2, RefreshCw } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

interface UnlinkedMention {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  entityId: string
  entityName: string
  typeKey: string
  count: number
  context: string
}

/**
 * Codex names sitting in prose as plain text.
 *
 * Novalist recognises a bare name for the Wiki and the hover card, but nothing
 * ever turned one into a real mention - so an imported or hand-typed manuscript
 * under-reports every appearance figure the Codex derives, and the only fix was
 * to retype each name through the @-picker.
 */
export function UnlinkedMentionsPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [items, setItems] = useState<UnlinkedMention[] | null>(null)
  const [busy, setBusy] = useState(false)

  const scan = async (): Promise<void> => {
    setBusy(true)
    try {
      setItems(await rpc.request<UnlinkedMention[]>('mentions/unlinked'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="match-settings">
      <div className="match-hint">{t('unlinked.intro')}</div>

      <div className="match-row">
        <button className="dialog-button" disabled={busy} onClick={() => void scan()}>
          <RefreshCw size={14} /> {t('unlinked.scan')}
        </button>
      </div>

      {items !== null && items.length === 0 && (
        <div className="settings-hint">{t('unlinked.none')}</div>
      )}

      {(items ?? []).map((item) => (
        <div key={`${item.sceneId}-${item.entityId}`} className="unlinked-row">
          <div className="unlinked-head">
            <span className="unlinked-name">{item.entityName}</span>
            <button
              className="unlinked-where"
              onClick={() =>
                void useProjectStore.getState().openScene(item.chapterGuid, item.sceneId)
              }
            >
              {item.chapterTitle} - {item.sceneTitle}
            </button>
            <span className="unlinked-count">{item.count}</span>
            <button
              className="dialog-button"
              title={t('unlinked.link')}
              onClick={() =>
                void rpc
                  .request<UnlinkedMention[]>('mentions/link', [
                    item.chapterGuid,
                    item.sceneId,
                    item.entityId
                  ])
                  .then(setItems)
              }
            >
              <Link2 size={14} />
            </button>
          </div>
          {/* The line it sits in, so a name that is also an ordinary word can
              be judged without opening the scene. */}
          <div className="unlinked-context">{item.context}</div>
        </div>
      ))}
    </div>
  )
}
