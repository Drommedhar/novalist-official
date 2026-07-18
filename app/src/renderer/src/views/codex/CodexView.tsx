import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useCodexStore, type EntityType } from '../../stores/codexStore'

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
  const entityType = useCodexStore((s) => s.entityType)
  const entities = useCodexStore((s) => s.entities)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const setType = useCodexStore((s) => s.setType)
  const refresh = useCodexStore((s) => s.refresh)
  const select = useCodexStore((s) => s.select)

  useEffect(() => {
    void refresh()
  }, [refresh])

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
        </div>
        <div className="codex-detail">
          {record ? (
            <dl className="codex-fields">
              {Object.entries(record)
                .filter(
                  ([key, value]) =>
                    !HIDDEN_FIELDS.has(key) &&
                    typeof value === 'string' &&
                    value.trim().length > 0
                )
                .map(([key, value]) => (
                  <div key={key} className="codex-field">
                    <dt>{key}</dt>
                    <dd>{String(value)}</dd>
                  </div>
                ))}
            </dl>
          ) : (
            <p className="codex-empty">{t('codexHub.selectHint')}</p>
          )}
        </div>
      </div>
    </div>
  )
}
