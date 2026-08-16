import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useEditorBridge } from '../stores/editorBridgeStore'
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
  const suggestionsRevision = useEditorBridge((s) => s.suggestionsRevision)

  const load = useCallback((): void => {
    void rpc
      .request<Suggestion[]>('suggestions/forScene', [chapterGuid, sceneId])
      .then(setSuggestions)
      .catch(() => setSuggestions([]))
  }, [chapterGuid, sceneId])

  useEffect(load, [load, suggestionsRevision])

  /**
   * Takes the writer to the mark this row is about.
   *
   * The marks are drawn in the prose already, but a scene is long and the row
   * says nothing about where in it the edit is - which is how "I can see them
   * in the list and never in the text" happens.
   */
  const showInText = (id: string): void => {
    const bridge = useEditorBridge.getState()
    if (bridge.isShowing(sceneId)) bridge.editor?.scrollToSuggestionById(id)
  }

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
      // The book-wide list of scenes with edits waiting counts what is left in
      // this one, so answering the last of them has to take the scene off it.
      useEditorBridge.getState().suggestionsChanged()
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
          {/* An edit cannot be answered without reading the sentence it is in,
              so the row is also the way to it. */}
          <button
            className="suggestion-body"
            title={t('suggestions.showInText')}
            onClick={() => showInText(suggestion.id)}
          >
            <span className="suggestion-text">{suggestion.text}</span>
            <span className="suggestion-meta">
              {t(`suggestions.${suggestion.kind}`)}
              {suggestion.author ? ` · ${suggestion.author}` : ''}
            </span>
          </button>
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
