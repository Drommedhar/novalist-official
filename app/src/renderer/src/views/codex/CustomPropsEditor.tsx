import { MotionPresence } from '../../shell/MotionPresence'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { InputDialog } from '../../shell/InputDialog'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'

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
  const record = useCodexStore(state => state.selectedRecord)
  const [props, setProps] = useState<CustomProp[]>([])
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    if (!selectedId) return
    void rpc
      .request<CustomProp[]>('entities/customProps', [entityType, selectedId])
      .then(setProps)
      .catch(() => setProps([]))
  }, [entityType, selectedId, record])

  if (!selectedId) return null

  const save = (key: string, value: string | null): void => {
    void rpc
      .request<CustomProp[]>('entities/setCustomProp', [entityType, selectedId, key, value])
      .then(setProps)
  }

  const input = (prop: CustomProp, value: string, editable: boolean, change: (next: string | null) => void): React.JSX.Element => {
    if (prop.propType === 'Bool') return <input type="checkbox" checked={value === 'true'} aria-disabled={!editable}
      onChange={event => change(event.target.checked ? 'true' : 'false')} />
    if (prop.propType === 'Enum') return <select className="outliner-input" value={value} aria-disabled={!editable}
      onChange={event => change(event.target.value)}>
      {!prop.enumOptions.includes(value) && <option value={value}>{value}</option>}
      {prop.enumOptions.map(option => <option key={option} value={option}>{option}</option>)}
    </select>
    const type = prop.propType === 'Int' ? 'number' : prop.propType === 'Date' ? 'date' : 'text'
    if (!metadataBinding(record, `customProperties:${prop.key}`)) return <input className="outliner-input" type={type}
      defaultValue={value} onBlur={event => save(prop.key, event.target.value)} />
    return <input className="outliner-input" type={type}
      value={value} readOnly={!editable} onChange={event => change(event.target.value)} />
  }

  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.customProperties')}</div>
      {props.map((prop) => (
        <div key={prop.key} className="entity-rel-row">
          <span className="codex-row-name">{prop.key}</span>
          <SharedMetadataField<string | null> binding={metadataBinding(record, `customProperties:${prop.key}`)} value={prop.value}
            onChange={next => save(prop.key, next)}>
            {({ value, editable, change }) => <>
              {input(prop, value ?? '', editable, change)}
              <button className="binder-expand" aria-label={`${t('explorer.contextDelete')} ${prop.key}`} onClick={() => change(null)}>
                <X size={12} strokeWidth={2} />
              </button>
            </>}
          </SharedMetadataField>
        </div>
      ))}
      <button className="binder-rail-item" onClick={() => setAdding(true)}>
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addProperty')}
      </button>
      <MotionPresence>{adding && (
        <InputDialog
          title={t('entityEditor.addProperty')}
          onCancel={() => setAdding(false)}
          onSubmit={(key) => {
            setAdding(false)
            save(key, '')
          }}
        />
      )}</MotionPresence>
    </div>
  )
}
