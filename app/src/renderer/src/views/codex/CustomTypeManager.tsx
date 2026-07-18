import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil, Plus, Trash2, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ConfirmDialog } from '../../shell/ConfirmDialog'

export interface CustomTypeDefinition {
  typeKey: string
  displayName: string
  displayNamePlural: string
  source: string
  defaultFields: {
    key: string
    displayName: string
    type: string
    defaultValue: string
    enumOptions: string[] | null
    required: boolean
  }[]
  features: { includeImages: boolean; includeRelationships: boolean; includeSections: boolean }
}

const FIELD_TYPES = ['String', 'Int', 'Bool', 'Date', 'Enum', 'Timespan', 'EntityRef']

interface FieldRow {
  key: string
  displayName: string
  type: string
  defaultValue: string
  enumOptionsText: string
  required: boolean
}

interface FormState {
  typeKey: string | null
  displayName: string
  displayNamePlural: string
  fields: FieldRow[]
  includeImages: boolean
  includeRelationships: boolean
  includeSections: boolean
}

const EMPTY_FORM: FormState = {
  typeKey: null,
  displayName: '',
  displayNamePlural: '',
  fields: [],
  includeImages: true,
  includeRelationships: false,
  includeSections: true
}

export function CustomTypeManager({
  types,
  onChanged,
  onClose
}: {
  types: CustomTypeDefinition[]
  onChanged: (types: CustomTypeDefinition[]) => void
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [form, setForm] = useState<FormState | null>(null)
  const [deleting, setDeleting] = useState<CustomTypeDefinition | null>(null)

  const patchField = (index: number, patch: Partial<FieldRow>): void => {
    if (!form) return
    setForm({
      ...form,
      fields: form.fields.map((f, i) => (i === index ? { ...f, ...patch } : f))
    })
  }

  const save = async (): Promise<void> => {
    if (!form || !form.displayName.trim()) return
    const updated = await rpc.request<CustomTypeDefinition[]>('entities/saveCustomType', [
      {
        typeKey: form.typeKey,
        displayName: form.displayName,
        displayNamePlural: form.displayNamePlural || null,
        fields: form.fields
          .filter((f) => f.displayName.trim() || f.key.trim())
          .map((f) => ({
            key: f.key || null,
            displayName: f.displayName,
            type: f.type,
            defaultValue: f.defaultValue || null,
            enumOptions:
              f.type === 'Enum' && f.enumOptionsText.trim()
                ? f.enumOptionsText.split(',').map((o) => o.trim()).filter(Boolean)
                : null,
            required: f.required
          })),
        includeImages: form.includeImages,
        includeRelationships: form.includeRelationships,
        includeSections: form.includeSections
      }
    ])
    onChanged(updated)
    setForm(null)
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className="dialog-card type-manager-card"
        role="dialog"
        aria-label={t('codexHub.manageTypes')}
      >
        <div className="type-manager-title-row">
          <div className="dialog-title">
            {form
              ? form.typeKey
                ? t('entityPanel.editType')
                : t('entityPanel.newEntityType')
              : t('codexHub.manageTypes')}
          </div>
          <button className="binder-expand" aria-label={t('dialog.cancel')} onClick={onClose}>
            <X size={14} strokeWidth={2} />
          </button>
        </div>

        {!form && (
          <>
            <div className="type-manager-list">
              {types.map((def) => (
                <div key={def.typeKey} className="type-manager-row">
                  <span className="type-manager-name">
                    {def.displayName}
                    <span className="codex-row-detail"> {def.displayNamePlural}</span>
                  </span>
                  {def.source === 'user' && (
                    <>
                      <button
                        className="binder-expand"
                        aria-label={t('entityPanel.editType')}
                        onClick={() =>
                          setForm({
                            typeKey: def.typeKey,
                            displayName: def.displayName,
                            displayNamePlural: def.displayNamePlural,
                            fields: def.defaultFields.map((f) => ({
                              key: f.key,
                              displayName: f.displayName,
                              type: f.type,
                              defaultValue: f.defaultValue,
                              enumOptionsText: (f.enumOptions ?? []).join(', '),
                              required: f.required
                            })),
                            includeImages: def.features.includeImages,
                            includeRelationships: def.features.includeRelationships,
                            includeSections: def.features.includeSections
                          })
                        }
                      >
                        <Pencil size={13} strokeWidth={2} />
                      </button>
                      <button
                        className="binder-expand"
                        aria-label={t('entityPanel.deleteType')}
                        onClick={() => setDeleting(def)}
                      >
                        <Trash2 size={13} strokeWidth={2} />
                      </button>
                    </>
                  )}
                </div>
              ))}
              {types.length === 0 && <p className="codex-empty">{t('codexHub.emptyHint')}</p>}
            </div>
            <div className="dialog-actions">
              <button className="dialog-button primary" onClick={() => setForm(EMPTY_FORM)}>
                <Plus size={13} strokeWidth={2} /> {t('entityPanel.newEntityType')}
              </button>
            </div>
          </>
        )}

        {form && (
          <>
            <label className="inspector-label">{t('entityTypeManager.displayName')}</label>
            <input
              className="dialog-input"
              autoFocus
              placeholder={t('entityTypeManager.displayNameWatermark')}
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
            />
            <label className="inspector-label">{t('entityTypeManager.pluralName')}</label>
            <input
              className="dialog-input"
              placeholder={t('entityTypeManager.pluralNameWatermark')}
              value={form.displayNamePlural}
              onChange={(e) => setForm({ ...form, displayNamePlural: e.target.value })}
            />

            <label className="inspector-label">{t('entityTypeManager.fields')}</label>
            <div className="type-manager-fields">
              {form.fields.map((field, index) => (
                <div key={index} className="type-manager-field">
                  <input
                    className="dialog-input"
                    placeholder={t('entityTypeManager.fieldName')}
                    value={field.displayName}
                    onChange={(e) => patchField(index, { displayName: e.target.value })}
                  />
                  <select
                    className="dialog-input type-manager-type"
                    value={field.type}
                    onChange={(e) => patchField(index, { type: e.target.value })}
                  >
                    {FIELD_TYPES.map((ft) => (
                      <option key={ft} value={ft}>
                        {ft}
                      </option>
                    ))}
                  </select>
                  <input
                    className="dialog-input"
                    placeholder={t('entityTypeManager.defaultValue')}
                    value={field.defaultValue}
                    onChange={(e) => patchField(index, { defaultValue: e.target.value })}
                  />
                  {field.type === 'Enum' && (
                    <input
                      className="dialog-input"
                      placeholder={t('entityTypeManager.enumOptions')}
                      value={field.enumOptionsText}
                      onChange={(e) => patchField(index, { enumOptionsText: e.target.value })}
                    />
                  )}
                  <label className="type-manager-check">
                    <input
                      type="checkbox"
                      checked={field.required}
                      onChange={(e) => patchField(index, { required: e.target.checked })}
                    />
                    {t('entityTypeManager.required')}
                  </label>
                  <button
                    className="binder-expand"
                    aria-label={t('entityTypeManager.removeField')}
                    onClick={() =>
                      setForm({ ...form, fields: form.fields.filter((_, i) => i !== index) })
                    }
                  >
                    <Trash2 size={13} strokeWidth={2} />
                  </button>
                </div>
              ))}
              <button
                className="binder-rail-item"
                onClick={() =>
                  setForm({
                    ...form,
                    fields: [
                      ...form.fields,
                      {
                        key: '',
                        displayName: '',
                        type: 'String',
                        defaultValue: '',
                        enumOptionsText: '',
                        required: false
                      }
                    ]
                  })
                }
              >
                {t('entityTypeManager.addField')}
              </button>
            </div>

            <label className="inspector-label">{t('entityTypeManager.features')}</label>
            <div className="type-manager-features">
              <label className="type-manager-check">
                <input
                  type="checkbox"
                  checked={form.includeImages}
                  onChange={(e) => setForm({ ...form, includeImages: e.target.checked })}
                />
                {t('entityTypeManager.includeImages')}
              </label>
              <label className="type-manager-check">
                <input
                  type="checkbox"
                  checked={form.includeRelationships}
                  onChange={(e) => setForm({ ...form, includeRelationships: e.target.checked })}
                />
                {t('entityTypeManager.includeRelationships')}
              </label>
              <label className="type-manager-check">
                <input
                  type="checkbox"
                  checked={form.includeSections}
                  onChange={(e) => setForm({ ...form, includeSections: e.target.checked })}
                />
                {t('entityTypeManager.includeSections')}
              </label>
            </div>

            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setForm(null)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button primary"
                disabled={!form.displayName.trim()}
                onClick={() => void save()}
              >
                {t('dialog.ok')}
              </button>
            </div>
          </>
        )}

        {deleting && (
          <ConfirmDialog
            title={t('entityPanel.deleteType')}
            message={t('entityPanel.deleteTypeConfirm').replace('{0}', deleting.displayName)}
            onCancel={() => setDeleting(null)}
            onConfirm={() => {
              const target = deleting
              setDeleting(null)
              void rpc
                .request<CustomTypeDefinition[]>('entities/deleteCustomType', [target.typeKey])
                .then(onChanged)
            }}
          />
        )}
      </div>
    </div>
  )
}
