import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface ReplacementDto {
  id: string
  find: string
  replace: string
  isRegex: boolean
  matchCase: boolean
  enabled: boolean
  order: number
}

/**
 * Substitutions applied to every export and never to the prose.
 *
 * Find and Replace writes to the source scenes and snapshots each one it
 * touches, which is right for fixing a name and wrong for "the submission copy
 * spells it out and the ebook uses the glyph". These run on the way out, so a
 * rule can be turned off without anything to undo.
 *
 * Order matters and is the list's order: an earlier rule's output is a later
 * rule's input.
 */
export function ReplacementsPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [rules, setRules] = useState<ReplacementDto[]>([])
  const [tokens, setTokens] = useState<string[]>([])
  const [open, setOpen] = useState(false)

  useEffect(() => {
    void rpc
      .request<ReplacementDto[]>('export/replacements')
      .then(setRules)
      .catch(() => setRules([]))
    void rpc.request<string[]>('export/tokens').then(setTokens).catch(() => setTokens([]))
  }, [])

  const save = (next: ReplacementDto[]): void => {
    setRules(next)
    void rpc.request<ReplacementDto[]>('export/saveReplacements', [next]).then(setRules)
  }

  const patch = (index: number, changes: Partial<ReplacementDto>): void =>
    save(rules.map((r, i) => (i === index ? { ...r, ...changes } : r)))

  return (
    <details className="export-panel" open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary className="export-panel-summary">{t('export.replacements')}</summary>
      <div className="settings-hint">{t('export.replacementsIntro')}</div>

      {rules.map((rule, index) => (
        <div key={rule.id} className="export-replacement-row">
          <input
            className="dialog-input"
            placeholder={t('export.replacementFind')}
            value={rule.find}
            onChange={(e) => patch(index, { find: e.target.value })}
          />
          <input
            className="dialog-input"
            placeholder={t('export.replacementReplace')}
            value={rule.replace}
            onChange={(e) => patch(index, { replace: e.target.value })}
          />
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={rule.isRegex}
              onChange={(e) => patch(index, { isRegex: e.target.checked })}
            />
            {t('findReplace.regex')}
          </label>
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={rule.matchCase}
              onChange={(e) => patch(index, { matchCase: e.target.checked })}
            />
            {t('findReplace.matchCase')}
          </label>
          {/* Off keeps the rule without running it: a rule that is right for
              one submission and wrong for the next is worth not deleting. */}
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={rule.enabled}
              onChange={(e) => patch(index, { enabled: e.target.checked })}
            />
            {t('export.replacementEnabled')}
          </label>
          <button
            className="binder-expand"
            aria-label={t('export.replacementRemove')}
            onClick={() => save(rules.filter((_, i) => i !== index))}
          >
            <Trash2 size={13} strokeWidth={2} />
          </button>
        </div>
      ))}

      <button
        className="dialog-button"
        onClick={() =>
          save([
            ...rules,
            {
              id: '',
              find: '',
              replace: '',
              isRegex: false,
              matchCase: false,
              enabled: true,
              order: rules.length
            }
          ])
        }
      >
        {t('export.replacementAdd')}
      </button>

      {/* The tokens an export resolves. Listed rather than documented
          elsewhere: this is where somebody is when they want one. */}
      {tokens.length > 0 && (
        <>
          <div className="inspector-label">{t('export.tokens')}</div>
          <div className="settings-hint">{t('export.tokensIntro')}</div>
          <div className="export-token-list">
            {tokens.map((token) => (
              <code key={token} className="export-token">{`<$${token}>`}</code>
            ))}
          </div>
        </>
      )}
    </details>
  )
}
