import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

interface Suggestion {
  id: string
  kind: 'insertion' | 'deletion'
  text: string
  author: string
  at: string
}

/**
 * Suggested edits waiting on the open scene, one row each.
 *
 * The marks are visible in the prose, but answering them there means hunting
 * through a scene for underlines. A list is how somebody works through twenty
 * of them without missing one.
 */
export function SuggestionsPanel({
  chapterGuid,
  sceneId
}: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [suggestions, setSuggestions] = useState<Suggestion[]>([])
  const [busy, setBusy] = useState(false)

  const load = useCallback((): void => {
    void rpc
      .request<Suggestion[]>('suggestions/forScene', [chapterGuid, sceneId])
      .then(setSuggestions)
      .catch(() => setSuggestions([]))
  }, [chapterGuid, sceneId])

  useEffect(load, [load])

  /**
   * Answering an edit rewrites the scene on disk, so the editor has to be told
   * to re-read it - otherwise the writer is looking at prose that no longer
   * matches the file and their next keystroke saves the old version back.
   */
  const answer = async (method: string, args: unknown[]): Promise<void> => {
    setBusy(true)
    try {
      setSuggestions(await rpc.request<Suggestion[]>(method, args))
      await useProjectStore.getState().flushPendingSave()
      useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
      await useProjectStore.getState().openScene(chapterGuid, sceneId)
    } finally {
      setBusy(false)
    }
  }

  if (suggestions.length === 0) return null

  return (
    <div className="suggestions-panel">
      <div className="inspector-label">
        {t('suggestions.title', { count: suggestions.length })}
      </div>

      {suggestions.map((suggestion) => (
        <div key={suggestion.id} className={`suggestion-row ${suggestion.kind}`}>
          <div className="suggestion-body">
            <span className="suggestion-text">{suggestion.text}</span>
            <span className="suggestion-meta">
              {t(`suggestions.${suggestion.kind}`)}
              {suggestion.author ? ` · ${suggestion.author}` : ''}
            </span>
          </div>
          <button
            className="ctx-reset"
            disabled={busy}
            title={t('suggestions.accept')}
            aria-label={t('suggestions.accept')}
            onClick={() =>
              void answer('suggestions/accept', [chapterGuid, sceneId, suggestion.id])
            }
          >
            <Check size={14} />
          </button>
          <button
            className="ctx-reset"
            disabled={busy}
            title={t('suggestions.reject')}
            aria-label={t('suggestions.reject')}
            onClick={() =>
              void answer('suggestions/reject', [chapterGuid, sceneId, suggestion.id])
            }
          >
            <X size={14} />
          </button>
        </div>
      ))}

      <div className="settings-button-row">
        <button
          className="dialog-button"
          disabled={busy}
          onClick={() => void answer('suggestions/acceptAll', [chapterGuid, sceneId])}
        >
          {t('suggestions.acceptAll')}
        </button>
        <button
          className="dialog-button"
          disabled={busy}
          onClick={() => void answer('suggestions/rejectAll', [chapterGuid, sceneId])}
        >
          {t('suggestions.rejectAll')}
        </button>
      </div>
    </div>
  )
}
