import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import type { CustomTypeDefinition } from './CustomTypeManager'

const LORE_CATEGORIES = ['Organization', 'Culture', 'History', 'Other']

type Control = 'text' | 'textarea' | 'category' | 'parent' | 'date'

interface FieldSpec {
  key: string
  labelKey: string
  control: Control
}

interface Section {
  titleKey?: string
  fields: FieldSpec[]
}

/** Typed, labelled, grouped field layout per entity type (replaces the raw
 * string dumper). Built-in types get curated sections; custom types render
 * their declared fields with type-aware controls. */
const BUILT_IN: Record<string, Section[]> = {
  character: [
    {
      titleKey: 'entityEditor.basicInfo',
      fields: [
        { key: 'name', labelKey: 'entityEditor.name', control: 'text' },
        { key: 'surname', labelKey: 'entityEditor.surname', control: 'text' },
        { key: 'gender', labelKey: 'entityEditor.gender', control: 'text' },
        { key: 'age', labelKey: 'entityEditor.age', control: 'text' },
        { key: 'role', labelKey: 'entityEditor.rolePlaceholder', control: 'text' },
        { key: 'group', labelKey: 'entityEditor.groupPlaceholder', control: 'text' }
      ]
    },
    {
      titleKey: 'entityEditor.physicalAttributes',
      fields: [
        { key: 'eyeColor', labelKey: 'entityEditor.eyeColor', control: 'text' },
        { key: 'hairColor', labelKey: 'entityEditor.hairColor', control: 'text' },
        { key: 'hairLength', labelKey: 'entityEditor.hairLength', control: 'text' },
        { key: 'height', labelKey: 'entityEditor.height', control: 'text' },
        { key: 'build', labelKey: 'entityEditor.build', control: 'text' },
        { key: 'skinTone', labelKey: 'entityEditor.skinTone', control: 'text' },
        {
          key: 'distinguishingFeatures',
          labelKey: 'entityEditor.distinguishingFeatures',
          control: 'textarea'
        }
      ]
    }
  ],
  location: [
    {
      fields: [
        { key: 'name', labelKey: 'entityEditor.name', control: 'text' },
        { key: 'type', labelKey: 'entityEditor.locationTypePlain', control: 'text' },
        { key: 'parent', labelKey: 'entityEditor.parentLocation', control: 'parent' },
        { key: 'description', labelKey: 'entityEditor.description', control: 'textarea' }
      ]
    }
  ],
  item: [
    {
      fields: [
        { key: 'name', labelKey: 'entityEditor.name', control: 'text' },
        { key: 'type', labelKey: 'entityEditor.itemType', control: 'text' },
        { key: 'origin', labelKey: 'entityEditor.origin', control: 'text' },
        { key: 'description', labelKey: 'entityEditor.description', control: 'textarea' }
      ]
    }
  ],
  lore: [
    {
      fields: [
        { key: 'name', labelKey: 'entityEditor.name', control: 'text' },
        { key: 'category', labelKey: 'entityEditor.category', control: 'category' },
        { key: 'description', labelKey: 'entityEditor.description', control: 'textarea' }
      ]
    }
  ]
}

export function EntityDetailFields({
  entityType,
  record,
  customDef,
  updateField
}: {
  entityType: string
  record: Record<string, unknown>
  customDef: CustomTypeDefinition | undefined
  updateField: (key: string, value: string) => Promise<void>
}): React.JSX.Element {
  const { t } = useTranslation()
  const [locationNames, setLocationNames] = useState<string[]>([])

  const needsParent = entityType === 'location'
  useEffect(() => {
    if (!needsParent) return
    void rpc
      .request<{ name: string }[]>('entities/list', ['location'])
      .then((list) => setLocationNames(list.map((l) => l.name)))
      .catch(() => setLocationNames([]))
  }, [needsParent])

  const custom = !['character', 'location', 'item', 'lore'].includes(entityType)
  const customFields = record.fields as Record<string, string> | undefined

  const readValue = (key: string, isCustom: boolean): string => {
    if (key === 'name') return String(record.name ?? '')
    if (isCustom) return String(customFields?.[key] ?? '')
    return String(record[key] ?? '')
  }

  const renderControl = (
    key: string,
    control: Control,
    isCustom: boolean,
    options?: string[]
  ): React.JSX.Element => {
    const value = readValue(key, isCustom)
    const commit = (v: string): void => {
      if (v !== value) void updateField(key, v)
    }
    if (control === 'textarea') {
      return (
        <textarea
          className="inspector-textarea"
          rows={3}
          defaultValue={value}
          key={`${key}:${value}`}
          onBlur={(e) => commit(e.target.value)}
        />
      )
    }
    if (control === 'category' || (options && options.length > 0)) {
      const opts = options ?? LORE_CATEGORIES
      return (
        <select
          className="dialog-input codex-field-input"
          value={value || opts[0]}
          onChange={(e) => commit(e.target.value)}
        >
          {opts.map((o) => (
            <option key={o} value={o}>
              {o}
            </option>
          ))}
        </select>
      )
    }
    const inputType = control === 'date' ? 'date' : 'text'
    return (
      <>
        <input
          className="outliner-input codex-field-input"
          type={inputType}
          list={control === 'parent' ? 'codex-location-names' : undefined}
          defaultValue={value}
          key={`${key}:${value}`}
          onBlur={(e) => commit(e.target.value)}
        />
        {control === 'parent' && (
          <datalist id="codex-location-names">
            {locationNames.map((n) => (
              <option key={n} value={n} />
            ))}
          </datalist>
        )}
      </>
    )
  }

  const customControl = (fieldType: string): { control: Control; options?: string[] } => {
    switch (fieldType) {
      case 'Date':
        return { control: 'date' }
      case 'Bool':
        return { control: 'category', options: ['true', 'false'] }
      default:
        return { control: 'text' }
    }
  }

  const sections: Section[] = custom
    ? [
        {
          fields: [
            { key: 'name', labelKey: 'entityEditor.name', control: 'text' },
            ...(customDef?.defaultFields ?? []).map((f) => ({
              key: f.key,
              labelKey: '',
              control: customControl(f.type).control
            }))
          ]
        }
      ]
    : (BUILT_IN[entityType] ?? [])

  return (
    <div className="codex-fields">
      {sections.map((section, si) => (
        <div key={si} className="codex-field-section">
          {section.titleKey && (
            <div className="inspector-label">{t(section.titleKey)}</div>
          )}
          {section.fields.map((field) => {
            const def = custom
              ? customDef?.defaultFields.find((f) => f.key === field.key)
              : undefined
            const options = def
              ? def.type === 'Enum'
                ? (def.enumOptions ?? [])
                : def.type === 'Bool'
                  ? ['true', 'false']
                  : undefined
              : undefined
            const label = field.labelKey ? t(field.labelKey) : (def?.displayName ?? field.key)
            return (
              <div key={field.key} className="codex-field">
                <dt>{label}</dt>
                <dd>{renderControl(field.key, field.control, custom, options)}</dd>
              </div>
            )
          })}
        </div>
      ))}
    </div>
  )
}
