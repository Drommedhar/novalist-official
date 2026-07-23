import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'
import { rpc } from '../rpc/client'

interface Candidate {
  id: string
  typeKey: string
  name: string
  detail: string
}

const BUILT_IN: { typeKey: string; labelKey: string }[] = [
  { typeKey: 'character', labelKey: 'codexHub.characters' },
  { typeKey: 'location', labelKey: 'codexHub.locations' },
  { typeKey: 'item', labelKey: 'codexHub.items' },
  { typeKey: 'lore', labelKey: 'codexHub.lore' }
]

/**
 * Sends a passage from the editor into one of a Codex entity's sections: pick the
 * entity, name the section, confirm. The section is created if it does not exist,
 * so "Notes" works as a catch-all without any setup.
 */
export function AppendToEntityDialog({
  text,
  onConfirm,
  onCancel
}: {
  text: string
  onConfirm(target: { typeKey: string; id: string; sectionTitle: string }): void
  onCancel(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<Candidate | null>(null)
  const [sectionTitle, setSectionTitle] = useState(t('capture.defaultSectionTitle'))

  useEffect(() => {
    let cancelled = false
    const load = async (): Promise<void> => {
      const types = [...BUILT_IN.map((b) => b.typeKey)]
      try {
        const custom = await rpc.request<{ typeKey: string }[]>('entities/customTypes')
        types.push(...custom.map((c) => c.typeKey))
      } catch {
        // Built-ins alone are enough to pick a target.
      }
      const all: Candidate[] = []
      for (const typeKey of types) {
        try {
          const list = await rpc.request<{ id: string; name: string; detail: string }[]>(
            'entities/list',
            [typeKey]
          )
          for (const e of list) all.push({ id: e.id, typeKey, name: e.name, detail: e.detail })
        } catch {
          // Skip a type that fails to load rather than losing the whole picker.
        }
      }
      if (!cancelled) setCandidates(all)
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase()
    const matches = needle
      ? candidates.filter((c) => c.name.toLowerCase().includes(needle))
      : candidates
    return matches.slice(0, 50)
  }, [candidates, filter])

  const canSubmit = selected != null && sectionTitle.trim().length > 0
  const submit = (): void => {
    if (!selected || sectionTitle.trim().length === 0) return
    onConfirm({ typeKey: selected.typeKey, id: selected.id, sectionTitle: sectionTitle.trim() })
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card dialog-card-wide" role="dialog" aria-label={t('capture.appendTitle')}>
        <div className="dialog-title">{t('capture.appendTitle')}</div>
        <blockquote className="capture-excerpt">{text}</blockquote>

        <div className="wiki-index-search">
          <Search size={13} strokeWidth={1.75} aria-hidden="true" />
          <input
            type="search"
            value={filter}
            placeholder={t('capture.appendFilterPlaceholder')}
            aria-label={t('capture.appendFilterPlaceholder')}
            onChange={(e) => setFilter(e.target.value)}
            autoFocus
          />
        </div>

        <ul className="capture-target-list">
          {shown.map((c) => (
            <li key={`${c.typeKey}-${c.id}`}>
              <button
                type="button"
                className={`capture-target${selected?.id === c.id ? ' active' : ''}`}
                onClick={() => setSelected(c)}
              >
                <span className="capture-target-name">{c.name}</span>
                {c.detail && <span className="capture-target-detail">{c.detail}</span>}
              </button>
            </li>
          ))}
          {shown.length === 0 && <li className="capture-target-empty">{t('wiki.noMatches')}</li>}
        </ul>

        <label className="inspector-label" htmlFor="capture-section">
          {t('capture.sectionTitle')}
        </label>
        <input
          id="capture-section"
          className="dialog-input"
          value={sectionTitle}
          onChange={(e) => setSectionTitle(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') submit()
          }}
        />

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" disabled={!canSubmit} onClick={submit}>
            {t('capture.appendConfirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
