import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy, Trash2 } from 'lucide-react'
import { rpc } from '../rpc/client'

interface Darling {
  id: string
  text: string
  /** The scene it was cut from, or empty when that scene is gone. */
  source: string
  note: string
  createdAt: string
}

/**
 * Prose the writer cut and kept.
 *
 * Deleted text was recoverable only by opening a snapshot of the whole scene
 * and reading it for the paragraph that used to be there. A paragraph cut
 * because it does not belong in this chapter is not a mistake to undo — it is
 * writing looking for a different home, and there was nowhere to put it.
 */
export function DarlingsPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [items, setItems] = useState<Darling[]>([])
  const [query, setQuery] = useState('')

  const load = useCallback(() => {
    void rpc
      .request<Darling[]>('darlings/list')
      .then(setItems)
      .catch(() => setItems([]))
  }, [])

  useEffect(load, [load])

  const needle = query.trim().toLowerCase()
  const shown =
    needle.length === 0
      ? items
      : items.filter(
          (d) =>
            d.text.toLowerCase().includes(needle) || d.note.toLowerCase().includes(needle)
        )

  return (
    <div className="darlings-panel">
      <div className="inspector-label">{t('darlings.title')}</div>

      {items.length === 0 ? (
        <p className="settings-hint">{t('darlings.empty')}</p>
      ) : (
        <input
          className="dialog-input"
          placeholder={t('darlings.search')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      )}

      {shown.map((item) => (
        <div key={item.id} className="darling">
          {/* The prose itself, whole. Truncating a cut paragraph to a preview
              would hide the reason it was worth keeping. */}
          <div className="darling-text">{item.text}</div>
          <div className="darling-meta">
            {item.source && <span className="darling-source">{item.source}</span>}
            <span>{new Date(item.createdAt).toLocaleDateString()}</span>
            <span className="toolbar-spacer" />
            {/* Copy rather than a one-click reinsert: a cut usually belongs
                somewhere other than where the caret happens to be. */}
            <button
              className="binder-row-action"
              aria-label={t('darlings.copy')}
              title={t('darlings.copy')}
              onClick={() => window.novalist.copyText(item.text)}
            >
              <Copy size={12} strokeWidth={2} />
            </button>
            <button
              className="binder-row-action"
              aria-label={t('darlings.remove')}
              title={t('darlings.remove')}
              onClick={() =>
                void rpc.request<Darling[]>('darlings/remove', [item.id]).then(setItems)
              }
            >
              <Trash2 size={12} strokeWidth={2} />
            </button>
          </div>
          <input
            className="links-note"
            placeholder={t('darlings.notePlaceholder')}
            defaultValue={item.note}
            onBlur={(e) => {
              if (e.target.value === item.note) return
              void rpc
                .request<Darling[]>('darlings/setNote', [item.id, e.target.value])
                .then(setItems)
            }}
          />
        </div>
      ))}
    </div>
  )
}
