import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import {
  useManuscriptPropsStore,
  type ManuscriptProperty,
  type PropertyType
} from '../../stores/manuscriptPropsStore'

const TYPES: PropertyType[] = ['String', 'Int', 'Bool', 'Date', 'Enum']

/**
 * Fields the writer adds to every scene or every chapter of this book.
 *
 * Codex entries have had typed properties for a long time; a scene had a closed
 * field set, so tracking tension, a revision pass or a note-to-self meant
 * overloading tags - and a tag cannot be sorted, totalled or filtered as a
 * number.
 */
export function ManuscriptPropertiesCard(): React.JSX.Element {
  const { t } = useTranslation()
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const [draft, setDraft] = useState<ManuscriptProperty[]>([])
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  useEffect(() => {
    if (!dirty) setDraft(definitions)
  }, [definitions, dirty])

  const edit = (index: number, patch: Partial<ManuscriptProperty>): void => {
    setDirty(true)
    setDraft(draft.map((d, i) => (i === index ? { ...d, ...patch } : d)))
  }

  const add = (): void => {
    setDirty(true)
    // The key is derived once and never shown, so renaming the label later
    // cannot orphan the values already stored under it.
    setDraft([
      ...draft,
      {
        key: `prop-${draft.length + 1}-${Date.now()}`,
        label: '',
        type: 'String',
        enumOptions: [],
        scope: 'Scene',
        showInOutliner: false
      }
    ])
  }

  const save = async (): Promise<void> => {
    await useManuscriptPropsStore.getState().save(draft)
    setDirty(false)
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('props.intro')}</div>

      {draft.map((property, index) => (
        <div key={property.key} className="props-row">
          <input
            className="inspector-input"
            value={property.label}
            placeholder={t('props.labelPlaceholder')}
            onChange={(e) => edit(index, { label: e.target.value })}
          />
          <select
            className="inspector-input"
            value={property.scope}
            onChange={(e) => edit(index, { scope: e.target.value as ManuscriptProperty['scope'] })}
          >
            <option value="Scene">{t('props.scopeScene')}</option>
            <option value="Chapter">{t('props.scopeChapter')}</option>
            <option value="Plotline">{t('props.scopePlotline')}</option>
            <option value="Event">{t('props.scopeEvent')}</option>
            <option value="Research">{t('props.scopeResearch')}</option>
          </select>
          <select
            className="inspector-input"
            value={property.type}
            onChange={(e) => edit(index, { type: e.target.value as PropertyType })}
          >
            {TYPES.map((type) => (
              <option key={type} value={type}>
                {t(`props.type${type}`)}
              </option>
            ))}
          </select>
          <button
            className="dialog-button danger"
            title={t('props.remove')}
            onClick={() => {
              setDirty(true)
              setDraft(draft.filter((_, i) => i !== index))
            }}
          >
            <Trash2 size={14} />
          </button>

          {/* Only a choice field needs a list of choices. */}
          {property.type === 'Enum' && (
            <input
              className="inspector-input props-options"
              value={property.enumOptions.join(', ')}
              placeholder={t('props.optionsPlaceholder')}
              onChange={(e) =>
                edit(index, {
                  enumOptions: e.target.value
                    .split(',')
                    .map((o) => o.trim())
                    .filter((o) => o.length > 0)
                })
              }
            />
          )}

          {/* Off by default: a dozen fields is not a dozen useful columns. */}
          {property.scope === 'Scene' && (
            <label className="match-toggle props-outliner-toggle">
              <input
                type="checkbox"
                checked={property.showInOutliner}
                onChange={(e) => edit(index, { showInOutliner: e.target.checked })}
              />
              {t('props.showInOutliner')}
            </label>
          )}
        </div>
      ))}

      <div className="settings-button-row">
        <button className="dialog-button" onClick={add}>
          <Plus size={14} /> {t('props.add')}
        </button>
        <button className="dialog-button primary" disabled={!dirty} onClick={() => void save()}>
          {t('dialog.save')}
        </button>
      </div>
      {dirty && <div className="settings-hint">{t('props.unsaved')}</div>}
    </div>
  )
}
