import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Settings2, Trash2 } from 'lucide-react'
import { useCodexStore, type EntityType } from '../../stores/codexStore'
import { rpc } from '../../rpc/client'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { EntityListsEditor } from './EntityListsEditor'
import { CustomTypeManager, type CustomTypeDefinition } from './CustomTypeManager'
import { EntityImages } from './EntityImages'
import { CustomPropsEditor } from './CustomPropsEditor'
import { OverridesEditor } from './OverridesEditor'

const TYPES: { type: EntityType; key: string }[] = [
  { type: 'character', key: 'codexHub.characters' },
  { type: 'location', key: 'codexHub.locations' },
  { type: 'item', key: 'codexHub.items' },
  { type: 'lore', key: 'codexHub.lore' }
]

/** Fields shown in the generic detail pane when present and non-empty. */
const HIDDEN_FIELDS = new Set(['id', 'isWorldBible', 'images', 'relationships', 'sections', 'customProperties'])

export function CodexView(): React.JSX.Element {
  const { t } = useTranslation()
  const [customTypes, setCustomTypes] = useState<CustomTypeDefinition[]>([])
  const [typeManagerOpen, setTypeManagerOpen] = useState(false)
  const entityType = useCodexStore((s) => s.entityType)
  const entities = useCodexStore((s) => s.entities)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const setType = useCodexStore((s) => s.setType)
  const refresh = useCodexStore((s) => s.refresh)
  const select = useCodexStore((s) => s.select)
  const updateField = useCodexStore((s) => s.updateField)
  const create = useCodexStore((s) => s.create)
  const remove = useCodexStore((s) => s.remove)
  const [pending, setPending] = useState<'create' | 'delete' | null>(null)
  const [templates, setTemplates] = useState<{ id: string; name: string }[]>([])
  const [templateId, setTemplateId] = useState<string>('')

  useEffect(() => {
    void refresh()
    void rpc
      .request<CustomTypeDefinition[]>('entities/customTypes')
      .then(setCustomTypes)
      .catch(() => setCustomTypes([]))
  }, [refresh])

  const selected = entities.find((e) => e.id === selectedId)

  return (
    <div className="codex">
      <div className="codex-tabs">
        {TYPES.map(({ type, key }) => (
          <button
            key={type}
            className={`codex-tab${entityType === type ? ' active' : ''}`}
            onClick={() => void setType(type)}
          >
            {t(key)}
          </button>
        ))}
        {customTypes.map((custom) => (
          <button
            key={custom.typeKey}
            className={`codex-tab${entityType === custom.typeKey ? ' active' : ''}`}
            onClick={() => void setType(custom.typeKey)}
          >
            {custom.displayNamePlural}
          </button>
        ))}
        <button className="codex-tab codex-tab-manage" onClick={() => setTypeManagerOpen(true)}>
          <Settings2 size={13} strokeWidth={2} /> {t('codexHub.manageTypes')}
        </button>
      </div>
      <div className="codex-body">
        <div className="codex-list">
          {entities.map((entity) => (
            <button
              key={entity.id}
              className={`codex-row${selectedId === entity.id ? ' active' : ''}`}
              onClick={() => void select(entity.id)}
            >
              {entity.imagePath ? (
                <img
                  className="codex-thumb"
                  src={`novalist-project:///${encodeURI(entity.imagePath)}`}
                  alt=""
                />
              ) : (
                <span className="codex-thumb codex-thumb-empty" aria-hidden="true">
                  {entity.name.slice(0, 1).toUpperCase()}
                </span>
              )}
              <span className="codex-row-text">
                <span className="codex-row-name">{entity.name}</span>
                {entity.detail && <span className="codex-row-detail">{entity.detail}</span>}
              </span>
              {entity.isWorldBible && <span className="codex-wb">{t('common.wbBadge')}</span>}
            </button>
          ))}
          {entities.length === 0 && <p className="codex-empty">{t('codexHub.emptyHint')}</p>}
          <button
            className="binder-rail-item"
            onClick={() => {
              setTemplateId('')
              void rpc
                .request<{ id: string; name: string }[]>('entities/templates', [entityType])
                .then(setTemplates)
                .catch(() => setTemplates([]))
              setPending('create')
            }}
          >
            <Plus size={14} strokeWidth={2} />
            {t('codexHub.newEntry')}
          </button>
        </div>
        <div className="codex-detail">
          {record ? (
            <>
              <div className="codex-detail-actions">
                <button
                  className="dialog-button danger"
                  onClick={() => setPending('delete')}
                >
                  <Trash2 size={13} strokeWidth={2} /> {t('explorer.contextDelete')}
                </button>
              </div>
              <dl className="codex-fields">
                {Object.entries(record)
                  .filter(
                    ([key, value]) => !HIDDEN_FIELDS.has(key) && typeof value === 'string'
                  )
                  .map(([key, value]) => (
                    <div key={`${selectedId}-${key}`} className="codex-field">
                      <dt>{key}</dt>
                      <dd>
                        <input
                          className="outliner-input codex-field-input"
                          defaultValue={String(value)}
                          onBlur={(e) => {
                            if (e.target.value !== value) void updateField(key, e.target.value)
                          }}
                        />
                      </dd>
                    </div>
                  ))}
              </dl>
              <EntityImages />
              <CustomPropsEditor />
              <OverridesEditor />
              <EntityListsEditor />
            </>
          ) : (
            <p className="codex-empty">{t('codexHub.selectHint')}</p>
          )}
        </div>
      </div>
      {pending === 'create' && (
        <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && setPending(null)}>
          <div className="dialog-card" role="dialog" aria-label={t('codexHub.newEntry')}>
            <div className="dialog-title">{t('codexHub.newEntry')}</div>
            <input
              id="codex-create-name"
              className="dialog-input"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === 'Escape') setPending(null)
                if (e.key === 'Enter') {
                  const name = (e.target as HTMLInputElement).value.trim()
                  if (name) {
                    setPending(null)
                    void create(name, templateId || null)
                  }
                }
              }}
            />
            {templates.length > 0 && (
              <select
                className="dialog-input"
                value={templateId}
                onChange={(e) => setTemplateId(e.target.value)}
              >
                <option value="">{t('welcome.template')}</option>
                {templates.map((tpl) => (
                  <option key={tpl.id} value={tpl.id}>
                    {tpl.name}
                  </option>
                ))}
              </select>
            )}
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setPending(null)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button primary"
                onClick={() => {
                  const input = document.getElementById('codex-create-name') as HTMLInputElement | null
                  const name = input?.value.trim()
                  if (name) {
                    setPending(null)
                    void create(name, templateId || null)
                  }
                }}
              >
                {t('dialog.ok')}
              </button>
            </div>
          </div>
        </div>
      )}
      {typeManagerOpen && (
        <CustomTypeManager
          types={customTypes}
          onChanged={(updated) => {
            setCustomTypes(updated)
            if (
              !['character', 'location', 'item', 'lore'].includes(entityType) &&
              !updated.some((d) => d.typeKey === entityType)
            ) {
              void setType('character')
            }
          }}
          onClose={() => setTypeManagerOpen(false)}
        />
      )}
      {pending === 'delete' && selected && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={selected.name}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void remove(selected.id, selected.isWorldBible)
          }}
        />
      )}
    </div>
  )
}
