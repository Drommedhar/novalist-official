import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { useLayoutStore } from '../stores/layoutStore'

interface WorkspaceLayoutsDialogProps {
  onClose(): void
}

/**
 * Saving, applying and deleting named workspace layouts.
 *
 * A dialog rather than a view: it is a way of getting somewhere, and a
 * destination you have to navigate to in order to change where you are is the
 * wrong shape for that.
 */
export function WorkspaceLayoutsDialog({
  onClose
}: WorkspaceLayoutsDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const layouts = useLayoutStore((s) => s.layouts)
  const [name, setName] = useState('')

  const save = (): void => {
    if (name.trim().length === 0) return
    useLayoutStore.getState().save(name)
    setName('')
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="dialog-card" role="dialog" aria-label={t('layouts.title')}>
        <h2 className="dialog-title">{t('layouts.title')}</h2>
        <p className="settings-hint">{t('layouts.hint')}</p>

        <div className="layouts-save">
          <input
            className="dialog-input"
            placeholder={t('layouts.namePlaceholder')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && save()}
          />
          <button className="btn-primary" disabled={name.trim().length === 0} onClick={save}>
            {t('layouts.save')}
          </button>
        </div>

        {layouts.length === 0 ? (
          <p className="codex-empty">{t('layouts.empty')}</p>
        ) : (
          <div className="layouts-list">
            {layouts.map((layout) => (
              <div key={layout.name} className="layouts-row">
                <button
                  className="layouts-apply"
                  onClick={() => {
                    useLayoutStore.getState().apply(layout.name)
                    onClose()
                  }}
                >
                  {layout.name}
                </button>
                <button
                  className="binder-row-action"
                  aria-label={t('layouts.delete')}
                  title={t('layouts.delete')}
                  onClick={() => useLayoutStore.getState().remove(layout.name)}
                >
                  <Trash2 size={15} strokeWidth={2} />
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="dialog-actions">
          <button className="btn-secondary" onClick={onClose}>
            {t('dialog.close')}
          </button>
        </div>
      </div>
    </div>
  )
}
