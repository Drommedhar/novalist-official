import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Columns2, ExternalLink, Rows2, X } from 'lucide-react'
import { DEFAULT_LAYOUT, matchingLayout, paneLeaves, useShellStore } from '../stores/shellStore'
import { InputDialog } from './InputDialog'
import { popOut } from './PaneHeader'

/**
 * Splitting the content area, and the layouts a writer named.
 *
 * In the status bar because it belongs to the window rather than to any view:
 * putting it in a toolbar would mean one view owning a control that changes all
 * of them.
 */
export function PaneControls(): React.JSX.Element {
  const { t } = useTranslation()
  const splitActivePane = useShellStore((s) => s.splitActivePane)
  const closeActivePane = useShellStore((s) => s.closeActivePane)
  const layouts = useShellStore((s) => s.layouts)
  const saveLayout = useShellStore((s) => s.saveLayout)
  const applyLayout = useShellStore((s) => s.applyLayout)
  const deleteLayout = useShellStore((s) => s.deleteLayout)
  const paneCount = useShellStore((s) => paneLeaves(s.panes).length)
  const current = useShellStore((s) => matchingLayout(s.panes, s.layouts))
  const resetPanes = useShellStore((s) => s.resetPanes)
  const activeView = useShellStore((s) => s.mainView)
  const [naming, setNaming] = useState(false)

  return (
    <span className="toolbar-panes">
      <button
        className="toolbar-button"
        title={t('panes.splitRight')}
        aria-label={t('panes.splitRight')}
        onClick={() => splitActivePane('row')}
      >
        <Columns2 size={16} strokeWidth={1.75} />
      </button>
      <button
        className="toolbar-button"
        title={t('panes.splitDown')}
        aria-label={t('panes.splitDown')}
        onClick={() => splitActivePane('column')}
      >
        <Rows2 size={16} strokeWidth={1.75} />
      </button>
      {/* Only once there is a pane to close. The last one stays: a content area
          with nothing in it is not a layout, it is a broken window. */}
      {paneCount > 1 && (
        <button
          className="toolbar-button"
          title={t('panes.close')}
          aria-label={t('panes.close')}
          onClick={() => closeActivePane()}
        >
          <X size={16} strokeWidth={1.75} />
        </button>
      )}

      {/* The Codex on a second monitor while the manuscript stays where it is.
          The window runs the same renderer against the same backend, so what
          is in it is the real view rather than a copy. */}
      <button
        className="toolbar-button"
        title={t('panes.popOut')}
        aria-label={t('panes.popOut')}
        onClick={() => void popOut(activeView)}
      >
        <ExternalLink size={16} strokeWidth={1.75} />
      </button>

      {/* Reads as the layout the window is in, and falls back to naming itself
          when the window is in none. The current layout is worked out from the
          panes rather than remembered, so dragging a divider or closing a pane
          drops the name by itself - it would otherwise still claim to be
          "Drafting" after the writer had taken the arrangement apart. */}
      <select
        className="toolbar-panes-layouts"
        aria-label={t('panes.layouts')}
        value={current}
        onChange={(e) => {
          const value = e.target.value
          if (value === '__save') setNaming(true)
          else if (value === DEFAULT_LAYOUT) resetPanes()
          else if (value.startsWith('__forget:')) deleteLayout(value.slice('__forget:'.length))
          else if (value) applyLayout(value)
        }}
      >
        {/* The control's own name, never an entry to pick: choosing it did
            nothing, which is not something a menu item should do. */}
        <option value="" disabled hidden>
          {t('panes.layouts')}
        </option>
        {/* The way back to one pane, above the writer's own layouts and outside
            the group they can forget entries from. */}
        <option value={DEFAULT_LAYOUT}>{t('panes.defaultLayout')}</option>
        {layouts.length > 0 && (
          <optgroup label={t('panes.savedLayouts')}>
            {layouts.map((layout) => (
              <option key={layout.name} value={layout.name}>
                {layout.name}
              </option>
            ))}
          </optgroup>
        )}
        {/* Kept apart from the layouts above so "Save this layout" cannot be
            read as a layout by that name. */}
        <optgroup label={t('panes.manageLayouts')}>
          <option value="__save">{t('panes.saveLayout')}</option>
          {layouts.map((layout) => (
            <option key={`forget-${layout.name}`} value={`__forget:${layout.name}`}>
              {t('panes.deleteLayout')}: {layout.name}
            </option>
          ))}
        </optgroup>
      </select>

      {naming && (
        <InputDialog
          title={t('panes.layoutName')}
          onCancel={() => setNaming(false)}
          onSubmit={(name) => {
            setNaming(false)
            if (name.trim()) saveLayout(name.trim())
          }}
        />
      )}
    </span>
  )
}
