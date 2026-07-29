import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import type { CustomTypeDefinition } from './CustomTypeManager'
import { MarkdownEditor } from '../../shell/MarkdownEditor'

const LORE_CATEGORIES = ['Organization', 'Culture', 'History', 'Other']
const REF_TYPES = ['character', 'location', 'item', 'lore']

type Control = 'text' | 'textarea' | 'category' | 'parent' | 'date' | 'ref'

interface FieldSpec {
  key: string
  labelKey: string
  control: Control
}

interface Section {
  titleKey?: string
  fields: FieldSpec[]
}

/**
 * Every field key a built-in sheet can show, in the order Novalist ships them.
 * The arrange dialog needs the same list the sheet renders from, and reading
 * it from here is what keeps the two in step.
 */
export function builtInFieldKeys(entityType: string): string[] {
  return (BUILT_IN[entityType] ?? []).flatMap((section) => section.fields.map((f) => f.key))
}

/** The label each of those keys is shown under. */
export function builtInFieldLabelKeys(entityType: string): Record<string, string> {
  const labels: Record<string, string> = {}
  for (const section of BUILT_IN[entityType] ?? [])
    for (const field of section.fields) labels[field.key] = field.labelKey
  return labels
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
  const [refNames, setRefNames] = useState<string[]>([])

  const [sheet, setSheet] = useState<{ hidden: string[]; order: string[] }>({
    hidden: [],
    order: []
  })

  // How this project arranges this type's sheet. Empty means the default:
  // every field, in the order Novalist ships them in.
  useEffect(() => {
    void rpc
      .request<{ hidden: string[]; order: string[] }>('sheets/get', [entityType])
      .then(setSheet)
      .catch(() => setSheet({ hidden: [], order: [] }))
  }, [entityType])

  const custom = !['character', 'location', 'item', 'lore'].includes(entityType)
  const customFields = record.fields as Record<string, string> | undefined

  const needsParent = entityType === 'location'
  useEffect(() => {
    if (!needsParent) return
    void rpc
      .request<{ name: string }[]>('entities/list', ['location'])
      .then((list) => setLocationNames(list.map((l) => l.name)))
      .catch(() => setLocationNames([]))
  }, [needsParent])

  // EntityRef custom fields offer a name picker across the built-in entity types.
  const needsRefs = custom && (customDef?.defaultFields ?? []).some((f) => f.type === 'EntityRef')
  useEffect(() => {
    if (!needsRefs) return
    void Promise.all(
      REF_TYPES.map((type) =>
        rpc.request<{ name: string }[]>('entities/list', [type]).catch(() => [])
      )
    )
      .then((lists) => setRefNames([...new Set(lists.flat().map((e) => e.name))].sort()))
      .catch(() => setRefNames([]))
  }, [needsRefs])

  const readValue = (key: string, isCustom: boolean): string => {
    if (key === 'name') return String(record.name ?? '')
    if (isCustom) return String(customFields?.[key] ?? '')
    return String(record[key] ?? '')
  }

  const renderControl = (
    key: string,
    control: Control,
    isCustom: boolean,
    options?: string[],
    label?: string
  ): React.JSX.Element => {
    const value = readValue(key, isCustom)
    const commit = (v: string): void => {
      if (v !== value) void updateField(key, v)
    }
    if (control === 'textarea') {
      return <MarkdownField key={key} value={value} ariaLabel={label} onCommit={commit} />
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
    const listId =
      control === 'parent' ? 'codex-location-names' : control === 'ref' ? 'codex-ref-names' : undefined
    return (
      <>
        <input
          className="outliner-input codex-field-input"
          type={inputType}
          list={listId}
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
        {control === 'ref' && (
          <datalist id="codex-ref-names">
            {refNames.map((n) => (
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
      case 'EntityRef':
        return { control: 'ref' }
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

  // The project's own arrangement, applied over the shipped one. A field the
  // order does not mention keeps its natural place, so a field added to
  // Novalist later is not invisible in a project arranged before it existed.
  const arranged = sections
    .map((section) => ({
      ...section,
      fields: section.fields
        .filter((field) => field.key === 'name' || !sheet.hidden.includes(field.key))
        .sort((a, b) => {
          const ai = sheet.order.indexOf(a.key)
          const bi = sheet.order.indexOf(b.key)
          if (ai < 0 && bi < 0) return 0
          if (ai < 0) return 1
          if (bi < 0) return -1
          return ai - bi
        })
    }))
    .filter((section) => section.fields.length > 0)

  return (
    <div className="codex-fields">
      {arranged.map((section, si) => (
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
                <dd>{renderControl(field.key, field.control, custom, options, label)}</dd>
              </div>
            )
          })}
        </div>
      ))}
    </div>
  )
}

/**
 * Long-text entity fields commit on blur rather than on every keystroke, so the
 * draft is held here and only reaches the store when focus leaves - matching
 * the textarea this replaced.
 */
function MarkdownField({
  value,
  ariaLabel,
  onCommit
}: {
  value: string
  ariaLabel?: string
  onCommit: (next: string) => void
}): React.JSX.Element {
  const [draft, setDraft] = useState(value)
  // Adopt the stored value when a different entity is selected.
  useEffect(() => setDraft(value), [value])
  return (
    <MarkdownEditor
      value={draft}
      ariaLabel={ariaLabel}
      onChange={setDraft}
      onBlur={() => onCommit(draft)}
    />
  )
}
