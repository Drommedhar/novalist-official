import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import type { CustomTypeDefinition } from '../codex/CustomTypeManager'

interface TemplateSummary {
  id: string
  name: string
  builtIn: boolean
}

interface TemplateRecord {
  id?: string | null
  name: string
  fields: { key: string; defaultValue: string }[]
  customPropertyDefs: {
    key: string
    type: string
    defaultValue: string
    enumOptions?: string[] | null
    intervalUnit?: string | null
  }[]
  sections: { title: string; defaultContent: string }[]
  includeRelationships?: boolean
  includeImages?: boolean
  includeChapterOverrides?: boolean
  ageMode?: string | null
  ageIntervalUnit?: string | null
}

/** Known-field display names, mirroring the desktop editor's loc mapping. */
const FIELD_LOC_KEYS: Record<string, string> = {
  Gender: 'entityEditor.gender',
  Age: 'entityEditor.age',
  Role: 'entityEditor.rolePlaceholder',
  EyeColor: 'entityEditor.eyeColor',
  HairColor: 'entityEditor.hairColor',
  HairLength: 'entityEditor.hairLength',
  Height: 'entityEditor.height',
  Build: 'entityEditor.build',
  SkinTone: 'entityEditor.skinTone',
  DistinguishingFeatures: 'entityEditor.distinguishingFeatures',
  Type: 'entityEditor.locationTypePlain',
  Description: 'entityEditor.description',
  Origin: 'entityEditor.origin',
  Category: 'entityEditor.category'
}

const PROP_TYPES = ['String', 'Int', 'Bool', 'Date', 'Enum', 'Timespan']
const INTERVAL_UNITS = ['Years', 'Months', 'Days']

interface EditorState {
  type: string
  isCharacter: boolean
  isCustom: boolean
  record: TemplateRecord
  knownFields: string[]
  /** Known-field state: active flag + default value per key. */
  known: Record<string, { active: boolean; defaultValue: string }>
  customFields: { key: string; defaultValue: string }[]
  props: {
    key: string
    type: string
    defaultValue: string
    enumOptionsText: string
    intervalUnit: string
  }[]
  sections: { title: string; defaultContent: string }[]
}

function toEditorState(
  type: string,
  isCustom: boolean,
  record: TemplateRecord,
  knownFields: string[]
): EditorState {
  const isNew = record.fields.length === 0 && !record.id
  const known: EditorState['known'] = {}
  for (const key of knownFields) {
    const field = record.fields.find((f) => f.key.toLowerCase() === key.toLowerCase())
    known[key] = {
      active: isNew || field !== undefined,
      defaultValue: field?.defaultValue ?? ''
    }
  }
  const knownSet = new Set(knownFields.map((k) => k.toLowerCase()))
  return {
    type,
    isCharacter: type === 'character',
    isCustom,
    record,
    knownFields,
    known,
    customFields: record.fields
      .filter((f) => !knownSet.has(f.key.toLowerCase()))
      .map((f) => ({ key: f.key, defaultValue: f.defaultValue })),
    props: record.customPropertyDefs.map((p) => ({
      key: p.key,
      type: p.type,
      defaultValue: p.defaultValue,
      enumOptionsText: (p.enumOptions ?? []).join(', '),
      intervalUnit: p.intervalUnit ?? 'Years'
    })),
    sections: record.sections.map((s) => ({ ...s }))
  }
}

export function TemplatesCard(): React.JSX.Element {
  const { t } = useTranslation()
  const projectRoot = useProjectStore((s) => s.projectPath)
  const [customTypes, setCustomTypes] = useState<CustomTypeDefinition[]>([])
  const [lists, setLists] = useState<Record<string, TemplateSummary[]>>({})
  const [editor, setEditor] = useState<EditorState | null>(null)

  const groups: { type: string; label: string }[] = [
    { type: 'character', label: t('settings.characterTemplates') },
    { type: 'location', label: t('settings.locationTemplates') },
    { type: 'item', label: t('settings.itemTemplates') },
    { type: 'lore', label: t('settings.loreTemplates') },
    ...customTypes.map((c) => ({ type: c.typeKey, label: c.displayNamePlural }))
  ]

  const refresh = async (): Promise<void> => {
    const types = await rpc
      .request<CustomTypeDefinition[]>('entities/customTypes')
      .catch(() => [] as CustomTypeDefinition[])
    setCustomTypes(types)
    const next: Record<string, TemplateSummary[]> = {}
    for (const type of ['character', 'location', 'item', 'lore', ...types.map((c) => c.typeKey)]) {
      next[type] = await rpc.request<TemplateSummary[]>('templates/list', [type]).catch(() => [])
    }
    setLists(next)
  }

  useEffect(() => {
    if (projectRoot) void refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectRoot])

  const openEditor = async (type: string, id: string | null): Promise<void> => {
    const knownFields = await rpc.request<string[]>('templates/knownFields', [type])
    const record: TemplateRecord = id
      ? await rpc.request<TemplateRecord>('templates/get', [type, id])
      : {
          name: t('template.newTemplate'),
          fields: [],
          customPropertyDefs: [],
          sections: [],
          includeRelationships: true,
          includeImages: true,
          includeChapterOverrides: true
        }
    setEditor(toEditorState(type, !['character', 'location', 'item', 'lore'].includes(type), record, knownFields))
  }

  const save = async (): Promise<void> => {
    if (!editor) return
    const fields = [
      ...editor.knownFields
        .filter((key) => editor.known[key].active)
        .map((key) => ({ key, defaultValue: editor.known[key].defaultValue })),
      ...editor.customFields
        .filter((f) => f.key.trim())
        .map((f) => ({ key: f.key, defaultValue: f.defaultValue }))
    ]
    const payload = {
      id: editor.record.id ?? null,
      name: editor.record.name,
      fields,
      customPropertyDefs: editor.props
        .filter((p) => p.key.trim())
        .map((p) => ({
          key: p.key,
          type: p.type,
          defaultValue: p.defaultValue,
          enumOptions:
            p.type === 'Enum' && p.enumOptionsText.trim()
              ? p.enumOptionsText.split(',').map((o) => o.trim()).filter(Boolean)
              : null,
          intervalUnit: p.type === 'Timespan' ? p.intervalUnit : null
        })),
      sections: editor.sections,
      includeImages: editor.record.includeImages ?? true,
      includeRelationships: editor.record.includeRelationships ?? true,
      includeChapterOverrides: editor.record.includeChapterOverrides ?? true,
      ageMode:
        editor.isCharacter && editor.known.Age?.active && editor.record.ageMode === 'date'
          ? 'date'
          : null,
      ageIntervalUnit:
        editor.isCharacter && editor.known.Age?.active && editor.record.ageMode === 'date'
          ? (editor.record.ageIntervalUnit ?? 'Years')
          : null
    }
    await rpc.request('templates/save', [editor.type, payload])
    setEditor(null)
    await refresh()
  }

  const patchRecord = (patch: Partial<TemplateRecord>): void => {
    if (!editor) return
    setEditor({ ...editor, record: { ...editor.record, ...patch } })
  }

  return (
    <section className="dashboard-card templates-card">
      <div className="dashboard-card-title">{t('settings.templates')}</div>
      {groups.map((group) => (
        <div key={group.type} className="template-group">
          <label className="inspector-label">{group.label}</label>
          <div className="type-manager-list">
            {(lists[group.type] ?? []).map((tpl) => (
              <div key={tpl.id} className="type-manager-row">
                <span className="type-manager-name">{tpl.name}</span>
                <button
                  className="binder-expand"
                  aria-label={t('entityPanel.editType')}
                  onClick={() => void openEditor(group.type, tpl.id)}
                >
                  <Pencil size={13} strokeWidth={2} />
                </button>
                <button
                  className="binder-expand"
                  aria-label={t('template.delete')}
                  onClick={() => {
                    void rpc
                      .request('templates/delete', [group.type, tpl.id])
                      .then(() => refresh())
                  }}
                >
                  <Trash2 size={13} strokeWidth={2} />
                </button>
              </div>
            ))}
          </div>
          <button
            className="binder-rail-item"
            onClick={() => void openEditor(group.type, null)}
          >
            <Plus size={13} strokeWidth={2} /> {t('template.addTemplate')}
          </button>
        </div>
      ))}

      {editor && (
        <div className="dialog-overlay">
          <div className="dialog-card type-manager-card" role="dialog" aria-label={editor.record.name}>
            <div className="dialog-title">{editor.record.name}</div>

            <label className="inspector-label">{t('template.templateName')}</label>
            <input
              className="dialog-input"
              value={editor.record.name}
              onChange={(e) => patchRecord({ name: e.target.value })}
            />

            <label className="inspector-label">{t('template.fields')}</label>
            <div className="type-manager-fields">
              {editor.knownFields.map((key) => {
                const state = editor.known[key]
                const isAge = editor.isCharacter && key === 'Age'
                return (
                  <div key={key} className="type-manager-field">
                    <label className="type-manager-check template-known-name">
                      <input
                        type="checkbox"
                        checked={state.active}
                        onChange={(e) =>
                          setEditor({
                            ...editor,
                            known: { ...editor.known, [key]: { ...state, active: e.target.checked } }
                          })
                        }
                      />
                      {FIELD_LOC_KEYS[key] ? t(FIELD_LOC_KEYS[key]) : key}
                    </label>
                    {state.active && !isAge && (
                      <input
                        className="dialog-input"
                        placeholder={t('template.defaultValue')}
                        value={state.defaultValue}
                        onChange={(e) =>
                          setEditor({
                            ...editor,
                            known: {
                              ...editor.known,
                              [key]: { ...state, defaultValue: e.target.value }
                            }
                          })
                        }
                      />
                    )}
                    {state.active && isAge && (
                      <>
                        <select
                          className="dialog-input type-manager-type"
                          value={editor.record.ageMode === 'date' ? 'date' : 'number'}
                          onChange={(e) =>
                            patchRecord({ ageMode: e.target.value === 'date' ? 'date' : null })
                          }
                        >
                          <option value="number">{t('template.ageMode.number')}</option>
                          <option value="date">{t('template.ageMode.date')}</option>
                        </select>
                        {editor.record.ageMode === 'date' && (
                          <select
                            className="dialog-input type-manager-type"
                            value={editor.record.ageIntervalUnit ?? 'Years'}
                            onChange={(e) => patchRecord({ ageIntervalUnit: e.target.value })}
                          >
                            {INTERVAL_UNITS.map((u) => (
                              <option key={u} value={u}>
                                {t(`template.intervalUnit.${u.toLowerCase()}`)}
                              </option>
                            ))}
                          </select>
                        )}
                      </>
                    )}
                  </div>
                )
              })}
            </div>

            <label className="inspector-label">{t('template.customFields')}</label>
            <div className="type-manager-fields">
              {editor.customFields.map((field, index) => (
                <div key={index} className="type-manager-field">
                  <input
                    className="dialog-input"
                    placeholder={t('template.fieldKey')}
                    value={field.key}
                    onChange={(e) =>
                      setEditor({
                        ...editor,
                        customFields: editor.customFields.map((f, i) =>
                          i === index ? { ...f, key: e.target.value } : f
                        )
                      })
                    }
                  />
                  <input
                    className="dialog-input"
                    placeholder={t('template.defaultValue')}
                    value={field.defaultValue}
                    onChange={(e) =>
                      setEditor({
                        ...editor,
                        customFields: editor.customFields.map((f, i) =>
                          i === index ? { ...f, defaultValue: e.target.value } : f
                        )
                      })
                    }
                  />
                  <button
                    className="binder-expand"
                    aria-label={t('template.removeField')}
                    onClick={() =>
                      setEditor({
                        ...editor,
                        customFields: editor.customFields.filter((_, i) => i !== index)
                      })
                    }
                  >
                    <Trash2 size={13} strokeWidth={2} />
                  </button>
                </div>
              ))}
              <button
                className="binder-rail-item"
                onClick={() =>
                  setEditor({
                    ...editor,
                    customFields: [...editor.customFields, { key: '', defaultValue: '' }]
                  })
                }
              >
                {t('template.addCustomField')}
              </button>
            </div>

            <label className="inspector-label">{t('template.defaultCustomProperties')}</label>
            <div className="type-manager-fields">
              {editor.props.map((prop, index) => {
                const patchProp = (patch: Partial<(typeof editor.props)[number]>): void =>
                  setEditor({
                    ...editor,
                    props: editor.props.map((p, i) => (i === index ? { ...p, ...patch } : p))
                  })
                return (
                  <div key={index} className="type-manager-field">
                    <input
                      className="dialog-input"
                      placeholder={t('template.propertyName')}
                      value={prop.key}
                      onChange={(e) => patchProp({ key: e.target.value })}
                    />
                    <select
                      className="dialog-input type-manager-type"
                      value={prop.type}
                      onChange={(e) => patchProp({ type: e.target.value })}
                    >
                      {PROP_TYPES.map((pt) => (
                        <option key={pt} value={pt}>
                          {t(`template.propType.${pt.toLowerCase()}`)}
                        </option>
                      ))}
                    </select>
                    {prop.type === 'Bool' ? (
                      <select
                        className="dialog-input type-manager-type"
                        value={prop.defaultValue === 'true' ? 'true' : 'false'}
                        onChange={(e) => patchProp({ defaultValue: e.target.value })}
                      >
                        <option value="false">{t('template.boolFalse')}</option>
                        <option value="true">{t('template.boolTrue')}</option>
                      </select>
                    ) : (
                      <input
                        className="dialog-input"
                        placeholder={t('template.propertyValue')}
                        value={prop.defaultValue}
                        onChange={(e) => patchProp({ defaultValue: e.target.value })}
                      />
                    )}
                    {prop.type === 'Enum' && (
                      <input
                        className="dialog-input"
                        placeholder={t('template.enumOptionsHint')}
                        value={prop.enumOptionsText}
                        onChange={(e) => patchProp({ enumOptionsText: e.target.value })}
                      />
                    )}
                    {prop.type === 'Timespan' && (
                      <select
                        className="dialog-input type-manager-type"
                        value={prop.intervalUnit}
                        onChange={(e) => patchProp({ intervalUnit: e.target.value })}
                      >
                        {INTERVAL_UNITS.map((u) => (
                          <option key={u} value={u}>
                            {t(`template.intervalUnit.${u.toLowerCase()}`)}
                          </option>
                        ))}
                      </select>
                    )}
                    <button
                      className="binder-expand"
                      aria-label={t('template.removeProperty')}
                      onClick={() =>
                        setEditor({ ...editor, props: editor.props.filter((_, i) => i !== index) })
                      }
                    >
                      <Trash2 size={13} strokeWidth={2} />
                    </button>
                  </div>
                )
              })}
              <button
                className="binder-rail-item"
                onClick={() =>
                  setEditor({
                    ...editor,
                    props: [
                      ...editor.props,
                      {
                        key: `prop${editor.props.length + 1}`,
                        type: 'String',
                        defaultValue: '',
                        enumOptionsText: '',
                        intervalUnit: 'Years'
                      }
                    ]
                  })
                }
              >
                {t('template.addProperty')}
              </button>
            </div>

            <label className="inspector-label">{t('template.sections')}</label>
            <div className="type-manager-fields">
              {editor.sections.map((section, index) => (
                <div key={index} className="type-manager-field">
                  <input
                    className="dialog-input"
                    placeholder={t('template.sectionTitle')}
                    value={section.title}
                    onChange={(e) =>
                      setEditor({
                        ...editor,
                        sections: editor.sections.map((s, i) =>
                          i === index ? { ...s, title: e.target.value } : s
                        )
                      })
                    }
                  />
                  <input
                    className="dialog-input"
                    placeholder={t('template.sectionDefaultContent')}
                    value={section.defaultContent}
                    onChange={(e) =>
                      setEditor({
                        ...editor,
                        sections: editor.sections.map((s, i) =>
                          i === index ? { ...s, defaultContent: e.target.value } : s
                        )
                      })
                    }
                  />
                  <button
                    className="binder-expand"
                    aria-label={t('template.removeSection')}
                    onClick={() =>
                      setEditor({
                        ...editor,
                        sections: editor.sections.filter((_, i) => i !== index)
                      })
                    }
                  >
                    <Trash2 size={13} strokeWidth={2} />
                  </button>
                </div>
              ))}
              <button
                className="binder-rail-item"
                onClick={() =>
                  setEditor({
                    ...editor,
                    sections: [...editor.sections, { title: '', defaultContent: '' }]
                  })
                }
              >
                {t('template.addSection')}
              </button>
            </div>

            <label className="inspector-label">{t('template.options')}</label>
            <div className="type-manager-features">
              <label className="type-manager-check">
                <input
                  type="checkbox"
                  checked={editor.record.includeImages ?? true}
                  onChange={(e) => patchRecord({ includeImages: e.target.checked })}
                />
                {t('template.includeImages')}
              </label>
              {(editor.isCharacter || editor.isCustom) && (
                <label className="type-manager-check">
                  <input
                    type="checkbox"
                    checked={editor.record.includeRelationships ?? true}
                    onChange={(e) => patchRecord({ includeRelationships: e.target.checked })}
                  />
                  {t('template.includeRelationships')}
                </label>
              )}
              {editor.isCharacter && (
                <label className="type-manager-check">
                  <input
                    type="checkbox"
                    checked={editor.record.includeChapterOverrides ?? true}
                    onChange={(e) => patchRecord({ includeChapterOverrides: e.target.checked })}
                  />
                  {t('template.includeChapterOverrides')}
                </label>
              )}
            </div>

            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setEditor(null)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button primary"
                disabled={!editor.record.name.trim()}
                onClick={() => void save()}
              >
                {t('template.save')}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}
