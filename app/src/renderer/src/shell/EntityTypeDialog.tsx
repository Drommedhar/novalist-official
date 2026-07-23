import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'

const BUILT_IN: { typeKey: string; labelKey: string }[] = [
  { typeKey: 'character', labelKey: 'codexHub.characters' },
  { typeKey: 'location', labelKey: 'codexHub.locations' },
  { typeKey: 'item', labelKey: 'codexHub.items' },
  { typeKey: 'lore', labelKey: 'codexHub.lore' }
]

/**
 * Asks which kind of Codex entity to create for an already-known name — used by
 * the editor's "create from a name you just typed" capture flows. Picking a type
 * resolves immediately; there is no second confirmation step.
 */
export function EntityTypeDialog({
  name,
  onPick,
  onCancel
}: {
  name: string
  onPick(typeKey: string): void
  onCancel(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [customTypes, setCustomTypes] = useState<{ typeKey: string; displayName: string }[]>([])

  useEffect(() => {
    let cancelled = false
    void rpc
      .request<{ typeKey: string; displayName: string }[]>('entities/customTypes')
      .then((types) => {
        if (!cancelled) setCustomTypes(types)
      })
      .catch(() => {
        // A project without custom types simply offers the built-ins.
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  const title = t('capture.createEntityTitle', { name })

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={title}>
        <div className="dialog-title">{title}</div>
        <div className="dialog-hint">{t('capture.createEntityHint')}</div>
        <div className="entity-type-choices">
          {BUILT_IN.map((type) => (
            <button
              key={type.typeKey}
              type="button"
              className="entity-type-choice"
              onClick={() => onPick(type.typeKey)}
            >
              {t(type.labelKey)}
            </button>
          ))}
          {customTypes.map((type) => (
            <button
              key={type.typeKey}
              type="button"
              className="entity-type-choice"
              onClick={() => onPick(type.typeKey)}
            >
              {type.displayName}
            </button>
          ))}
        </div>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
        </div>
      </div>
    </div>
  )
}
