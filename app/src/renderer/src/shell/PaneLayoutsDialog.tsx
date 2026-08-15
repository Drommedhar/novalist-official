import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { DEFAULT_LAYOUT, matchingLayout, useShellStore } from '../stores/shellStore'

/**
 * The arrangements of panes a writer named, and the way back to one pane.
 *
 * These used to be a dropdown on the main toolbar, beside the split and close
 * buttons - which put a control over the shape of the whole window on the bar
 * that belongs to the open project. Splitting the content area is the
 * application's business, so it moved to the View menu, and the named
 * arrangements came with it.
 *
 * Not the same thing as Workspace layouts, which remember panel widths and
 * which view you were in. This remembers how the content area was divided.
 */
export function PaneLayoutsDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const layouts = useShellStore((s) => s.layouts)
  const current = useShellStore((s) => matchingLayout(s.panes, s.layouts))
  const [name, setName] = useState('')

  const save = (): void => {
    if (name.trim().length === 0) return
    useShellStore.getState().saveLayout(name.trim())
    setName('')
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card" role="dialog" aria-label={t('panes.layouts')}>
        <h2 className="dialog-title">{t('panes.layouts')}</h2>

        <div className="layouts-save">
          <input
            className="dialog-input"
            placeholder={t('panes.layoutName')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && save()}
          />
          <button className="btn-primary" disabled={name.trim().length === 0} onClick={save}>
            {t('panes.saveLayout')}
          </button>
        </div>

        <div className="layouts-list">
          {/* The way back to one pane, above the writer's own arrangements. */}
          <div className="layouts-row">
            <button
              className="layouts-apply"
              aria-current={current === DEFAULT_LAYOUT ? 'true' : undefined}
              onClick={() => {
                useShellStore.getState().resetPanes()
                onClose()
              }}
            >
              {t('panes.defaultLayout')}
            </button>
          </div>
          {layouts.map((layout) => (
            <div key={layout.name} className="layouts-row">
              <button
                className="layouts-apply"
                aria-current={current === layout.name ? 'true' : undefined}
                onClick={() => {
                  useShellStore.getState().applyLayout(layout.name)
                  onClose()
                }}
              >
                {layout.name}
              </button>
              <button
                className="icon-button"
                title={t('panes.deleteLayout')}
                aria-label={t('panes.deleteLayout')}
                onClick={() => useShellStore.getState().deleteLayout(layout.name)}
              >
                <Trash2 size={14} strokeWidth={1.75} />
              </button>
            </div>
          ))}
        </div>

        <div className="dialog-actions">
          <button className="btn-secondary" onClick={onClose}>
            {t('dialog.close')}
          </button>
        </div>
      </div>
    </div>
  )
}
