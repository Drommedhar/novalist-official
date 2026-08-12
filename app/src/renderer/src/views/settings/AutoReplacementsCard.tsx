import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useSettingsStore } from '../../stores/settingsStore'
import { MAX_SAMPLE, previewRule } from './replacementPreview'

/** One thing that gets substituted. Mirrors AutoReplacementPair. */
export interface ReplacementRule {
  kind: 'literal' | 'regex'
  start: string
  end: string
  startReplace: string
  endReplace: string
}

const EMPTY_RULE: ReplacementRule = {
  kind: 'literal',
  start: '',
  end: '',
  startReplace: '',
  endReplace: ''
}

/**
 * The rules a writer types against, as an editable list.
 *
 * These were always a stored list rather than anything derived - the language
 * preset only ever seeded it - but nothing showed the list, so "quote style"
 * meant one of eleven presets and nothing else. A writer wanting an arrow for
 * `->`, or their own abbreviation expanded, had a settings file to hand-edit
 * and no way to know that would work.
 *
 * A closing form is what makes a rule alternate: it is how a quotation mark
 * knows whether it is opening or closing. Left empty, a rule produces the same
 * thing every time.
 */
export function AutoReplacementsCard({ scope }: { scope: 'global' | 'project' }): React.JSX.Element {
  const { t } = useTranslation()
  const view = useSettingsStore((s) => s.view)
  const load = useSettingsStore((s) => s.load)
  const [error, setError] = useState<string | null>(null)

  const stored = (scope === 'project'
    ? view?.overrides?.autoReplacements
    : view?.global.autoReplacements) as ReplacementRule[] | undefined
  const storedRules: ReplacementRule[] = stored ?? []
  const storedKey = JSON.stringify(storedRules)

  // A row with nothing to match on is not a rule yet - it is a writer part way
  // through adding one. Those rows live here and nowhere else until they have a
  // trigger, because storing one would mean storing a rule that can never fire.
  const [rows, setRows] = useState<ReplacementRule[]>(storedRules)
  useEffect(() => {
    // Re-seed when the stored list changes underneath - picking a language
    // rewrites it - while keeping any row still being filled in.
    setRows((prev) => [...storedRules, ...prev.filter((rule) => rule.start.length === 0)])
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [storedKey])

  const isPending = (rule: ReplacementRule): boolean => rule.start.length === 0

  // Edits arrive faster than they can be saved - tabbing from one field of a
  // row to the next is two of them - and each carries the whole list, so two in
  // flight at once means the slower one wins with the older list. Queued, so
  // they land in the order they were made and the last edit is the one kept.
  const queue = useRef<Promise<void>>(Promise.resolve())

  const commit = (next: ReplacementRule[]): void => {
    setRows(next)
    queue.current = queue.current.then(async () => {
      try {
        setError(null)
        await rpc.request('settings/setAutoReplacements', [scope, next.filter((r) => !isPending(r))])
        await load()
      } catch {
        // The backend refuses a rule it cannot run rather than storing one that
        // would be silently skipped, so say so and leave the row on screen.
        setError(t('settings.rulesRejected'))
      }
    })
  }

  const update = (index: number, patch: Partial<ReplacementRule>): void => {
    const next = rows.map((rule, i) => (i === index ? { ...rule, ...patch } : rule))
    // A literal rule's trigger is both its opening and its closing form; the
    // engine reads `end` to know a keystroke can close as well as open.
    const edited = next[index]
    if (edited.kind === 'literal') edited.end = edited.start
    if (!edited.endReplace) edited.endReplace = edited.startReplace
    commit(next)
  }

  const rules = rows

  return (
    <div className="settings-subgroup">
      <label className="inspector-label">{t('settings.rules')}</label>
      <div className="settings-hint">{t('settings.rulesDesc')}</div>

      <div className="replacement-rules">
        <div className="replacement-rules-head">
          <span>{t('settings.ruleKind')}</span>
          <span>{t('settings.ruleTrigger')}</span>
          <span>{t('settings.ruleBecomes')}</span>
          <span>{t('settings.ruleClosing')}</span>
          <span />
        </div>
        {rules.map((rule, index) => (
          <div className="replacement-rule" key={index}>
            <select
              className="dialog-input"
              aria-label={t('settings.ruleKind')}
              value={rule.kind === 'regex' ? 'regex' : 'literal'}
              onChange={(e) => update(index, { kind: e.target.value as ReplacementRule['kind'] })}
            >
              <option value="literal">{t('settings.ruleKindLiteral')}</option>
              <option value="regex">{t('settings.ruleKindRegex')}</option>
            </select>
            <RuleField
              label={t('settings.ruleTrigger')}
              value={rule.start}
              onCommit={(v) => update(index, { start: v })}
            />
            <RuleField
              label={t('settings.ruleBecomes')}
              value={rule.startReplace}
              onCommit={(v) => update(index, { startReplace: v })}
            />
            {/* A pattern has no alternating form, so there is nothing to close. */}
            <RuleField
              label={t('settings.ruleClosing')}
              value={rule.kind === 'regex' ? '' : rule.endReplace}
              disabled={rule.kind === 'regex'}
              onCommit={(v) => update(index, { endReplace: v })}
            />
            <button
              type="button"
              className="match-remove"
              title={t('settings.ruleRemove')}
              aria-label={t('settings.ruleRemove')}
              onClick={() => commit(rules.filter((_, i) => i !== index))}
            >
              <Trash2 size={15} strokeWidth={1.75} />
            </button>
            <RuleTryIt rule={rule} />
          </div>
        ))}
      </div>

      <RuleHelp />

      {error && <div className="settings-hint replacement-rules-error">{error}</div>}

      <div className="replacement-rules-actions">
        <button
          type="button"
          className="dialog-button"
          onClick={() => {
            // Local only: an empty row has nothing to match on, and asking the
            // backend to store it would be asking it to store a rule it is
            // right to refuse.
            setError(null)
            setRows([...rules, { ...EMPTY_RULE }])
          }}
        >
          {t('settings.ruleAdd')}
        </button>
      </div>
    </div>
  )
}

/**
 * A rule tried against a sample the writer supplies, as they write the rule.
 *
 * A pattern is not something anybody gets right first time, and until now the
 * only way to find out what one did was to save it and go and type into the
 * manuscript. The sample stays here rather than being stored: it is scratch
 * paper for building the rule, not part of it.
 */
function RuleTryIt({ rule }: { rule: ReplacementRule }): React.JSX.Element | null {
  const { t } = useTranslation()
  const [sample, setSample] = useState('')

  if (!rule.start) return null

  const preview = previewRule(rule, sample)
  const changed = preview.result !== sample

  return (
    <div className="rule-tryit">
      <label className="rule-tryit-label" htmlFor={`try-${rule.kind}-${rule.start}`}>
        {t('settings.ruleTry')}
      </label>
      <input
        id={`try-${rule.kind}-${rule.start}`}
        className="dialog-input"
        aria-label={t('settings.ruleTry')}
        placeholder={t('settings.ruleTryPlaceholder')}
        maxLength={MAX_SAMPLE}
        value={sample}
        onChange={(e) => setSample(e.target.value)}
      />
      {preview.error && (
        <div className="rule-tryit-out replacement-rules-error">
          {t(`settings.rulePattern_${preview.error}`)}
        </div>
      )}
      {!preview.error && sample && (
        <div className="rule-tryit-out">
          {changed ? (
            <>
              <span className="rule-tryit-result">{preview.result}</span>
              {/* What matched only tells you something when the trigger is a
                  pattern: for plain text it is the field you just typed. */}
              {rule.kind === 'regex' && (
                <span className="rule-tryit-detail">
                  {t('settings.ruleTryMatched', { text: preview.matched })}
                  {preview.groups.length > 0 &&
                    ' · ' + preview.groups.map((group, i) => `$${i + 1}=${group}`).join(' ')}
                </span>
              )}
            </>
          ) : (
            <span className="rule-tryit-detail">{t('settings.ruleTryNoMatch')}</span>
          )}
        </div>
      )}
    </div>
  )
}

/** How matching and replacement actually work, for whoever needs it. */
function RuleHelp(): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <details className="rule-help">
      <summary>{t('settings.ruleHelpTitle')}</summary>
      <ul>
        <li>{t('settings.ruleHelpWhen')}</li>
        <li>{t('settings.ruleHelpOrder')}</li>
        <li>{t('settings.ruleHelpClosing')}</li>
        <li>{t('settings.ruleHelpCaptures')}</li>
        <li>{t('settings.ruleHelpCleanup')}</li>
        <li>{t('settings.ruleHelpRefused')}</li>
      </ul>
      <div className="rule-help-table">
        <div>
          <code>\d</code> {t('settings.ruleHelpDigit')}
        </div>
        <div>
          <code>\w</code> {t('settings.ruleHelpWord')}
        </div>
        <div>
          <code>\s</code> {t('settings.ruleHelpSpace')}
        </div>
        <div>
          <code>.</code> {t('settings.ruleHelpAny')}
        </div>
        <div>
          <code>[abc]</code> {t('settings.ruleHelpSet')}
        </div>
        <div>
          <code>[^abc]</code> {t('settings.ruleHelpNotSet')}
        </div>
        <div>
          <code>[a-z]</code> {t('settings.ruleHelpRange')}
        </div>
        <div>
          <code>\b</code> {t('settings.ruleHelpBoundary')}
        </div>
        <div>
          <code>+</code> {t('settings.ruleHelpPlus')}
        </div>
        <div>
          <code>*</code> {t('settings.ruleHelpStar')}
        </div>
        <div>
          <code>?</code> {t('settings.ruleHelpOptional')}
        </div>
        <div>
          <code>|</code> {t('settings.ruleHelpOr')}
        </div>
        <div>
          <code>(…)</code> {t('settings.ruleHelpGroup')}
        </div>
        <div>
          <code>$1</code> {t('settings.ruleHelpBackref')}
        </div>
        <div>
          <code>\.</code> {t('settings.ruleHelpEscape')}
        </div>
      </div>
    </details>
  )
}

/** A rule field that commits on blur, so a half-typed pattern is never stored. */
function RuleField({
  label,
  value,
  disabled,
  onCommit
}: {
  label: string
  value: string
  disabled?: boolean
  onCommit(next: string): void
}): React.JSX.Element {
  const [draft, setDraft] = useState(value)
  const [editing, setEditing] = useState(false)
  return (
    <input
      className="dialog-input"
      aria-label={label}
      disabled={disabled}
      value={editing ? draft : value}
      onFocus={() => {
        setDraft(value)
        setEditing(true)
      }}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => {
        setEditing(false)
        if (draft !== value) onCommit(draft)
      }}
    />
  )
}
