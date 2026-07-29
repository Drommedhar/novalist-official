import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ManuscriptPropertyField } from './ManuscriptPropertyField'
import {
  useManuscriptPropsStore,
  type PropertyScope
} from '../stores/manuscriptPropsStore'

interface CustomFieldsPanelProps {
  scope: PropertyScope
  /** The plotline, event or research item the values belong to. */
  id: string
  className?: string
}

/**
 * The writer's own fields for one plotline, timeline event or research item.
 *
 * Renders nothing at all when no fields are defined for the scope - an empty
 * "Your fields" heading on every editor would be a permanent reminder of a
 * feature the writer has not chosen to use.
 */
export function CustomFieldsPanel({
  scope,
  id,
  className
}: CustomFieldsPanelProps): React.JSX.Element | null {
  const { t } = useTranslation()
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const [values, setValues] = useState<Record<string, string>>({})

  const fields = definitions.filter((d) => d.scope === scope)

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  useEffect(() => {
    if (!id || fields.length === 0) return
    void useManuscriptPropsStore
      .getState()
      .valuesFor(scope, id)
      .then(setValues)
      .catch(() => setValues({}))
    // Definitions changing does not change what is stored, so the values are
    // fetched per item rather than per render of the field list.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scope, id, fields.length])

  if (fields.length === 0) return null

  return (
    <div className={className ?? 'custom-fields'}>
      <div className="inspector-label">{t('props.yourFields')}</div>
      {fields.map((field) => (
        <label key={field.key} className="custom-field">
          <span className="custom-field-label">{field.label}</span>
          <ManuscriptPropertyField
            className="inspector-input"
            property={field}
            value={values[field.key] ?? ''}
            onCommit={(value) => {
              void useManuscriptPropsStore
                .getState()
                .setValueFor(scope, id, field.key, value)
                .then(setValues)
            }}
          />
        </label>
      ))}
    </div>
  )
}
