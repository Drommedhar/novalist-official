import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { InputDialog } from '../../shell/InputDialog'

interface CustomProp {
  key: string
  value: string
  propType: string
  enumOptions: string[]
}

/** Typed custom properties for the selected entity (template-resolved types). */
export function CustomPropsEditor(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const [props, setProps] = useState<CustomProp[]>([])
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    if (!selectedId) return
    void rpc
      .request<CustomProp[]>('entities/customProps', [entityType, selectedId])
      .then(setProps)
      .catch(() => setProps([]))
  }, [entityType, selectedId])

  if (!selectedId) return null

  const save = (key: string, value: string | null): void => {
    void rpc
      .request<CustomProp[]>('entities/setCustomProp', [entityType, selectedId, key, value])
      .then(setProps)
  }

  const input = (prop: CustomProp): React.JSX.Element => {
    switch (prop.propType) {
      case 'Bool':
        return (
          <input
            type="checkbox"
            checked={prop.value === 'true'}
            onChange={(e) => save(prop.key, e.target.checked ? 'true' : 'false')}
          />
        )
      case 'Int':
        return (
          <input
            className="outliner-input"
            type="number"
            defaultValue={prop.value}
            onBlur={(e) => save(prop.key, e.target.value)}
          />
        )
      case 'Date':
        return (
          <input
            className="outliner-input"
            type="date"
            defaultValue={prop.value}
            onChange={(e) => save(prop.key, e.target.value)}
          />
        )
      case 'Enum':
        return (
          <select
            className="outliner-input"
            value={prop.value}
            onChange={(e) => save(prop.key, e.target.value)}
          >
            {!prop.enumOptions.includes(prop.value) && <option value={prop.value}>{prop.value}</option>}
            {prop.enumOptions.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        )
      default:
        return (
          <input
            className="outliner-input"
            defaultValue={prop.value}
            onBlur={(e) => save(prop.key, e.target.value)}
          />
        )
    }
  }

  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.customProperties')}</div>
      {props.map((prop) => (
        <div key={prop.key} className="entity-rel-row">
          <span className="codex-row-name">{prop.key}</span>
          {input(prop)}
          <button
            className="binder-expand"
            aria-label={`${t('explorer.contextDelete')} ${prop.key}`}
            onClick={() => save(prop.key, null)}
          >
            <X size={12} strokeWidth={2} />
          </button>
        </div>
      ))}
      <button className="binder-rail-item" onClick={() => setAdding(true)}>
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addProperty')}
      </button>
      {adding && (
        <InputDialog
          title={t('entityEditor.addProperty')}
          onCancel={() => setAdding(false)}
          onSubmit={(key) => {
            setAdding(false)
            save(key, '')
          }}
        />
      )}
    </div>
  )
}
