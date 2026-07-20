import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Play, SlidersHorizontal } from 'lucide-react'
import { rpc } from '../../rpc/client'
import '../../shell/hostBridge.css'

interface SettingsPageDto {
  extensionId: string
  extensionName: string
  category: string
  iconPath: string | null
}

interface WizardDescriptorDto {
  extensionId: string
  extensionName: string
  wizardId: string
  displayName: string
  description: string
}

interface SettingsFieldDto {
  key: string
  label: string
  type: string
  value: string
  options: string[] | null
  min: number | null
  max: number | null
  group: string | null
  help: string | null
  /** When set, this field is shown only while the field named by
   * visibleWhenKey currently holds one of visibleWhenValues. */
  visibleWhenKey: string | null
  visibleWhenValues: string[] | null
}

interface SettingsSchemaDto {
  extensionId: string
  extensionName: string
  title: string
  fields: SettingsFieldDto[]
}

/**
 * Surfaces extension-contributed settings pages, declarative settings schemas,
 * and wizards. Native settings-page bodies (ISettingsContributor) the headless
 * backend cannot render appear as informational cards; a declarative schema
 * (ISettingsSchemaContributor) is rendered as an editable form; configuration
 * flows are also driven through contributed wizards.
 */
export function ExtensionSettings(): React.JSX.Element | null {
  const { t } = useTranslation()
  const [pages, setPages] = useState<SettingsPageDto[]>([])
  const [wizards, setWizards] = useState<WizardDescriptorDto[]>([])
  const [schemas, setSchemas] = useState<SettingsSchemaDto[]>([])
  const [runningId, setRunningId] = useState<string | null>(null)

  const refresh = (): void => {
    void rpc.request<SettingsPageDto[]>('extensions/settingsPages').then(setPages)
    void rpc.request<WizardDescriptorDto[]>('extensions/wizards').then(setWizards)
    void rpc.request<SettingsSchemaDto[]>('extensions/settingsSchema').then(setSchemas)
  }

  useEffect(() => {
    refresh()
  }, [])

  const runWizard = async (w: WizardDescriptorDto): Promise<void> => {
    setRunningId(w.wizardId)
    try {
      await rpc.request('extensions/runWizard', [w.extensionId, w.wizardId])
    } finally {
      setRunningId(null)
    }
  }

  if (pages.length === 0 && wizards.length === 0 && schemas.length === 0) return null

  return (
    <div className="ext-settings-section">
      <h2 className="dashboard-title">{t('extensions.settingsTitle')}</h2>

      {schemas.map((schema) => (
        <ExtensionSchemaForm key={schema.extensionId} schema={schema} onSaved={refresh} />
      ))}

      {pages.map((page) => (
        <div key={`${page.extensionId}:${page.category}`} className="ext-settings-card">
          <SlidersHorizontal className="ext-settings-icon" size={18} />
          <div className="ext-settings-card-body">
            <span className="ext-settings-card-title">{page.category}</span>
            <span className="ext-settings-card-sub">{page.extensionName}</span>
          </div>
        </div>
      ))}

      {wizards.map((w) => (
        <div key={`${w.extensionId}:${w.wizardId}`} className="ext-settings-card">
          <div className="ext-settings-card-body">
            <span className="ext-settings-card-title">{w.displayName}</span>
            <span className="ext-settings-card-sub">{w.description || w.extensionName}</span>
          </div>
          <button
            type="button"
            className="export-inline-btn"
            disabled={runningId !== null}
            onClick={() => void runWizard(w)}
          >
            <Play size={13} strokeWidth={2} /> {t('extensions.settingsRun')}
          </button>
        </div>
      ))}
    </div>
  )
}

/** Editable form rendered from an extension's declarative settings schema. */
function ExtensionSchemaForm({
  schema,
  onSaved
}: {
  schema: SettingsSchemaDto
  onSaved: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(schema.fields.map((f) => [f.key, f.value]))
  )
  const [saving, setSaving] = useState(false)

  const set = (key: string, value: string): void => setValues((v) => ({ ...v, [key]: value }))

  const save = async (): Promise<void> => {
    setSaving(true)
    try {
      await rpc.request('extensions/settingsSchema/save', [schema.extensionId, values])
      onSaved()
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="ext-schema-form">
      <div className="ext-schema-head">
        <span className="ext-settings-card-title">{schema.title}</span>
        <span className="ext-settings-card-sub">{schema.extensionName}</span>
      </div>
      <div className="ext-schema-fields">
        {schema.fields
          .filter(
            (field) =>
              !field.visibleWhenKey ||
              (field.visibleWhenValues ?? []).includes(values[field.visibleWhenKey] ?? '')
          )
          .map((field) => (
            <label key={field.key} className="ext-schema-field">
              <span className="ext-schema-label">{field.label}</span>
              <SchemaInput field={field} value={values[field.key] ?? ''} onChange={(v) => set(field.key, v)} />
              {field.help && <span className="ext-schema-help">{field.help}</span>}
            </label>
          ))}
      </div>
      <div className="ext-schema-actions">
        <button type="button" className="export-inline-btn" disabled={saving} onClick={() => void save()}>
          {t('extensions.settingsSave')}
        </button>
      </div>
    </div>
  )
}

function SchemaInput({
  field,
  value,
  onChange
}: {
  field: SettingsFieldDto
  value: string
  onChange: (v: string) => void
}): React.JSX.Element {
  switch (field.type) {
    case 'bool':
      return (
        <input
          type="checkbox"
          className="ext-schema-checkbox"
          checked={value === 'true'}
          onChange={(e) => onChange(e.target.checked ? 'true' : 'false')}
        />
      )
    case 'number':
      return (
        <input
          type="number"
          className="ext-schema-input"
          value={value}
          min={field.min ?? undefined}
          max={field.max ?? undefined}
          onChange={(e) => onChange(e.target.value)}
        />
      )
    case 'select':
      return (
        <select className="ext-schema-input" value={value} onChange={(e) => onChange(e.target.value)}>
          {(field.options ?? []).map((opt) => (
            <option key={opt} value={opt}>
              {opt}
            </option>
          ))}
        </select>
      )
    case 'password':
      return (
        <input
          type="password"
          className="ext-schema-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      )
    case 'multiline':
      return (
        <textarea
          className="ext-schema-input ext-schema-textarea"
          value={value}
          rows={4}
          onChange={(e) => onChange(e.target.value)}
        />
      )
    default:
      return (
        <input
          type="text"
          className="ext-schema-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      )
  }
}
