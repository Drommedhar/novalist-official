import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import type { FieldBinding } from '../../collaboration/FocusedField'
import { SharedMetadataField } from '../../collaboration/SharedMetadataField'

export function EntityAliases({ binding, values, onChange }: { binding?: FieldBinding; values: string[]; onChange(values: string[]): void }): React.JSX.Element {
  const { t } = useTranslation()
  const [draft, setDraft] = useState('')
  return <section data-entity-tab="overview" className="codex-entry-section">
    <div className="inspector-label">{t('entityEditor.aliases')}</div>
    <SharedMetadataField binding={binding} value={values} onChange={onChange}>{({ value, editable, change }) => {
      const aliases = value ?? []
      const add = (): void => {
        const alias = draft.trim()
        if (!editable || !alias || aliases.includes(alias)) return
        change(previous => [...previous ?? [], alias])
        setDraft('')
      }
      return <div className="entity-chips">
        {aliases.map(alias => <span key={alias} className="entity-chip">{alias}
          <button aria-label={`${t('explorer.contextDelete')} ${alias}`} onClick={() => change(previous => previous.filter(item => item !== alias))}>
            <X size={11} strokeWidth={2} />
          </button>
        </span>)}
        <input className="entity-chip-input" value={draft} placeholder={t('entityEditor.addAlias')} readOnly={!editable}
          onChange={event => setDraft(event.target.value)} onKeyDown={event => event.key === 'Enter' && add()} onBlur={add} />
      </div>
    }}</SharedMetadataField>
  </section>
}
