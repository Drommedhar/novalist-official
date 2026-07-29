import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../rpc/client'
import type { SmartListDto, SmartListRule } from './SmartListsPanel'

export interface SmartListDraft {
  name: string
  match: 'All' | 'Any'
  rules: SmartListRule[]
}

/** One field a rule can test, and what is worth offering for it. */
interface FieldDto {
  field: string
  label: string
  kind: 'text' | 'number' | 'choice'
  options: string[]
}

/** Which comparisons make sense for which kind of field. */
const OPERATORS: Record<FieldDto['kind'], string[]> = {
  text: ['Contains', 'Is', 'IsSet', 'IsNotSet'],
  number: ['Is', 'GreaterThan', 'LessThan', 'IsSet', 'IsNotSet'],
  choice: ['Is', 'IsSet', 'IsNotSet']
}

interface SmartListEditorProps {
  initial: SmartListDto | null
  onSubmit(draft: SmartListDraft): void
  onCancel(): void
}

/**
 * A saved list as a set of rules rather than four fixed filters.
 *
 * The old editor was chapter status, POV, tag and plotline, all ANDed. That
 * cannot express "either of these two POVs", "no synopsis yet", or anything
 * about a field the writer added themselves - which are the questions a
 * collection is usually for.
 */
export function SmartListEditor({
  initial,
  onSubmit,
  onCancel
}: SmartListEditorProps): React.JSX.Element {
  const { t } = useTranslation()
  const [name, setName] = useState(initial?.name ?? '')
  const [match, setMatch] = useState<'All' | 'Any'>(initial?.match ?? 'All')
  const [rules, setRules] = useState<SmartListRule[]>(initial?.rules ?? [])
  const [fields, setFields] = useState<FieldDto[]>([])
  const [plotlines, setPlotlines] = useState<{ id: string; name: string }[]>([])

  // Only the backend knows this book's tags, stages, plotlines and the
  // writer's own scene fields, so the choices come from there.
  useEffect(() => {
    void rpc.request<FieldDto[]>('smartLists/fields').then(setFields).catch(() => setFields([]))
    void rpc
      .request<{ plotlines: { id: string; name: string }[] }>('plot/grid')
      .then((grid) => setPlotlines(grid.plotlines))
      .catch(() => setPlotlines([]))
  }, [])

  const definition = (field: string): FieldDto | undefined => fields.find((f) => f.field === field)

  /** Plotlines are stored by id; showing the id would be unreadable. */
  const optionLabel = (field: string, option: string): string => {
    if (field === 'plotline') return plotlines.find((p) => p.id === option)?.name ?? option
    if (field === 'chapterStatus') return t(`dashboard.status${option}`)
    return option
  }

  const fieldLabel = (field: FieldDto): string =>
    field.field.startsWith('prop:') ? field.label : t(`smartList.field.${field.field}`)

  const edit = (index: number, patch: Partial<SmartListRule>): void =>
    setRules(rules.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const add = (): void =>
    setRules([...rules, { field: fields[0]?.field ?? 'title', op: 'Contains', value: '' }])

  const submit = (): void => {
    if (name.trim().length === 0) return
    onSubmit({ name: name.trim(), match, rules })
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div
        className="dialog-card smart-list-card"
        role="dialog"
        aria-label={t('smartList.editTitle')}
      >
        <div className="dialog-title">{t('smartList.editTitle')}</div>

        <label className="inspector-label" htmlFor="sl-name">
          {t('smartList.name')}
        </label>
        <input
          id="sl-name"
          className="dialog-input"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />

        <label className="inspector-label" htmlFor="sl-match">
          {t('smartList.match')}
        </label>
        <select
          id="sl-match"
          className="dialog-input"
          value={match}
          onChange={(e) => setMatch(e.target.value as 'All' | 'Any')}
        >
          <option value="All">{t('smartList.matchAll')}</option>
          <option value="Any">{t('smartList.matchAny')}</option>
        </select>

        {rules.map((rule, index) => {
          const field = definition(rule.field)
          const kind = field?.kind ?? 'text'
          const needsValue = rule.op !== 'IsSet' && rule.op !== 'IsNotSet'
          return (
            <div key={index} className="smart-list-rule">
              <select
                className="dialog-input"
                aria-label={t('smartList.ruleField')}
                value={rule.field}
                onChange={(e) => {
                  // Operators differ by kind, so a field change that leaves an
                  // impossible comparison behind resets it to a valid one.
                  const next = definition(e.target.value)
                  const allowed = OPERATORS[next?.kind ?? 'text']
                  edit(index, {
                    field: e.target.value,
                    op: allowed.includes(rule.op) ? rule.op : allowed[0],
                    value: ''
                  })
                }}
              >
                {fields.map((f) => (
                  <option key={f.field} value={f.field}>
                    {fieldLabel(f)}
                  </option>
                ))}
              </select>
              <select
                className="dialog-input"
                aria-label={t('smartList.ruleOperator')}
                value={rule.op}
                onChange={(e) => edit(index, { op: e.target.value })}
              >
                {OPERATORS[kind].map((op) => (
                  <option key={op} value={op}>
                    {t(`smartList.op.${op}`)}
                  </option>
                ))}
              </select>
              {needsValue &&
                (field && field.options.length > 0 ? (
                  <select
                    className="dialog-input"
                    aria-label={t('smartList.ruleValue')}
                    value={rule.value}
                    onChange={(e) => edit(index, { value: e.target.value })}
                  >
                    <option value="">{t('smartList.chooseValue')}</option>
                    {field.options.map((option) => (
                      <option key={option} value={option}>
                        {optionLabel(rule.field, option)}
                      </option>
                    ))}
                  </select>
                ) : (
                  <input
                    className="dialog-input"
                    aria-label={t('smartList.ruleValue')}
                    type={kind === 'number' ? 'number' : 'text'}
                    value={rule.value}
                    onChange={(e) => edit(index, { value: e.target.value })}
                  />
                ))}
              <button
                className="dialog-button danger"
                title={t('smartList.removeRule')}
                onClick={() => setRules(rules.filter((_, i) => i !== index))}
              >
                <Trash2 size={14} />
              </button>
            </div>
          )
        })}

        {rules.length === 0 && <div className="settings-hint">{t('smartList.noRules')}</div>}

        <div className="settings-button-row">
          <button className="dialog-button" onClick={add}>
            <Plus size={14} /> {t('smartList.addRule')}
          </button>
        </div>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={submit}>
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
