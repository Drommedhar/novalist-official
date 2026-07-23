import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

export interface EntityProposal {
  typeKey: string
  name: string
  detail: string
}

const BUILT_IN_LABELS: Record<string, string> = {
  character: 'codexHub.characters',
  location: 'codexHub.locations',
  item: 'codexHub.items',
  lore: 'codexHub.lore'
}

/**
 * Review list for entities an extension proposed from a scene. Nothing is
 * created until the writer ticks it and confirms — the extension only ever
 * suggests, the host does the writing.
 */
export function EntityProposalsDialog({
  proposals,
  onCreate,
  onCancel
}: {
  proposals: EntityProposal[]
  onCreate(accepted: EntityProposal[]): void
  onCancel(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  // Nothing is pre-selected: accepting is a deliberate act, not a default.
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [types, setTypes] = useState<Record<number, string>>({})

  useEffect(() => {
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  const toggle = (index: number): void => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(index)) next.delete(index)
      else next.add(index)
      return next
    })
  }

  const confirm = (): void => {
    const accepted = [...selected]
      .sort((a, b) => a - b)
      .map((i) => ({ ...proposals[i], typeKey: types[i] ?? proposals[i].typeKey }))
    if (accepted.length > 0) onCreate(accepted)
  }

  const typeLabel = (typeKey: string): string => {
    const key = BUILT_IN_LABELS[typeKey]
    return key ? t(key) : typeKey
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card dialog-card-wide" role="dialog" aria-label={t('capture.proposalsTitle')}>
        <div className="dialog-title">{t('capture.proposalsTitle')}</div>
        <div className="dialog-hint">{t('capture.proposalsHint')}</div>

        <ul className="capture-target-list">
          {proposals.map((proposal, i) => (
            <li key={`${proposal.name}-${i}`}>
              <label className="capture-proposal">
                <input type="checkbox" checked={selected.has(i)} onChange={() => toggle(i)} />
                <span className="capture-proposal-text">
                  <span className="capture-target-name">{proposal.name}</span>
                  {proposal.detail && (
                    <span className="capture-target-detail">{proposal.detail}</span>
                  )}
                </span>
                <select
                  className="capture-proposal-type"
                  value={types[i] ?? proposal.typeKey}
                  onChange={(e) => setTypes((prev) => ({ ...prev, [i]: e.target.value }))}
                  onClick={(e) => e.preventDefault()}
                >
                  {Object.keys(BUILT_IN_LABELS).map((typeKey) => (
                    <option key={typeKey} value={typeKey}>
                      {typeLabel(typeKey)}
                    </option>
                  ))}
                  {/* A custom type proposed by the extension stays selectable. */}
                  {!(proposal.typeKey in BUILT_IN_LABELS) && (
                    <option value={proposal.typeKey}>{proposal.typeKey}</option>
                  )}
                </select>
              </label>
            </li>
          ))}
        </ul>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" disabled={selected.size === 0} onClick={confirm}>
            {t('capture.proposalsCreate', { count: selected.size })}
          </button>
        </div>
      </div>
    </div>
  )
}
