import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface SceneLabel {
  key: string
  label: string
  color: string
}

/**
 * The labels a scene in this book can carry.
 *
 * A scene has held a label colour since long before anything read it: a bare
 * hex string with no name and no surface that showed it. A colour nobody named
 * tells a reader nothing, so a label has a name first and a colour second.
 */
export function SceneLabelsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [labels, setLabels] = useState<SceneLabel[]>([])
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void rpc.request<SceneLabel[]>('labels/list').then(setLabels).catch(() => setLabels([]))
  }, [])

  const edit = (index: number, patch: Partial<SceneLabel>): void => {
    setDirty(true)
    setLabels(labels.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('labels.intro')}</div>

      {labels.map((label, index) => (
        <div key={label.key} className="match-row">
          <input
            className="inspector-input"
            value={label.label}
            placeholder={t('labels.namePlaceholder')}
            onChange={(e) => edit(index, { label: e.target.value })}
          />
          <input
            className="dialog-input settings-color"
            type="color"
            aria-label={t('labels.colour')}
            value={label.color}
            onChange={(e) => edit(index, { color: e.target.value })}
          />
          <button
            className="dialog-button danger"
            title={t('labels.remove')}
            onClick={() => {
              setDirty(true)
              setLabels(labels.filter((_, i) => i !== index))
            }}
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}

      <div className="settings-button-row">
        <button
          className="dialog-button"
          onClick={() => {
            setDirty(true)
            // A key the writer never sees, so renaming a label later cannot
            // orphan the scenes already carrying it.
            setLabels([
              ...labels,
              { key: `label-${labels.length + 1}-${Date.now()}`, label: '', color: '#8b8b8b' }
            ])
          }}
        >
          <Plus size={14} /> {t('labels.add')}
        </button>
        <button
          className="dialog-button primary"
          disabled={!dirty}
          onClick={() =>
            void rpc.request<SceneLabel[]>('labels/set', [labels]).then((saved) => {
              setLabels(saved)
              setDirty(false)
            })
          }
        >
          {t('dialog.save')}
        </button>
      </div>
      {dirty && <div className="settings-hint">{t('labels.unsaved')}</div>}
    </div>
  )
}
