import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, Columns2, ExternalLink, Rows2, X } from 'lucide-react'
import { activityGroups, useShellStore, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

/**
 * Everything a pane offers, grouped as the activity bar groups it, plus the
 * editor - which has no rail button because a scene is reached through the
 * binder, and which a pane therefore had no way to ask for at all.
 */
const PANE_GROUPS: { key: string; views: MainView[] }[] = [
  { key: 'shell.groupWrite', views: ['write', ...activityGroups[0].views] },
  ...activityGroups.slice(1),
  { key: 'shell.groupApp', views: ['extensions', 'settings'] }
]

/**
 * The strip along the top of a pane: what it is showing, and the controls that
 * change it.
 *
 * Panes could only be retargeted by clicking into one and then using the
 * activity bar, which is an affordance nobody can see - the complaint was that
 * splitting gave you a second copy of the same view with no way to put anything
 * else in it. The name doubles as the picker, so what a pane holds and how to
 * change it are the same control.
 *
 * Hidden while the window holds a single pane, so an unsplit window looks
 * exactly as it did.
 */
export function PaneHeader({ paneId, view }: { paneId: string; view: MainView }): React.JSX.Element {
  const { t } = useTranslation()
  const setPaneView = useShellStore((s) => s.setPaneView)
  const setActivePane = useShellStore((s) => s.setActivePane)
  const splitPaneById = useShellStore((s) => s.splitPaneById)
  const closePaneById = useShellStore((s) => s.closePaneById)
  const canClose = useShellStore((s) => s.panes.kind === 'split')
  // Positioned against the viewport rather than the header, because a pane
  // clips what overflows it - in a pane split top-and-bottom the menu would
  // have been cut off at the pane's own edge with no way to reach the rest.
  const [picking, setPicking] = useState<{ left: number; top: number } | null>(null)

  const pick = (next: MainView): void => {
    setPicking(null)
    setActivePane(paneId)
    setPaneView(paneId, next)
  }

  return (
    <div className="pane-header">
      <button
        className="pane-header-view"
        aria-haspopup="menu"
        aria-expanded={picking !== null}
        title={t('panes.showInPane')}
        onClick={(event) => {
          if (picking) {
            setPicking(null)
            return
          }
          const rect = event.currentTarget.getBoundingClientRect()
          // Flipped above the button when there is no room below, so a pane
          // along the bottom of the window still opens a menu you can read.
          const height = Math.min(window.innerHeight * 0.7, 520)
          const below = window.innerHeight - rect.bottom
          setPicking({
            left: rect.left,
            top: below < height ? Math.max(8, rect.top - height) : rect.bottom
          })
        }}
      >
        <span className="pane-header-title">{t(`shell.view.${view}`)}</span>
        <ChevronDown size={14} strokeWidth={1.75} />
      </button>

      <span className="pane-header-actions">
        <button
          className="pane-header-button"
          title={t('panes.splitRight')}
          aria-label={t('panes.splitRight')}
          onClick={() => splitPaneById(paneId, 'row')}
        >
          <Columns2 size={14} strokeWidth={1.75} />
        </button>
        <button
          className="pane-header-button"
          title={t('panes.splitDown')}
          aria-label={t('panes.splitDown')}
          onClick={() => splitPaneById(paneId, 'column')}
        >
          <Rows2 size={14} strokeWidth={1.75} />
        </button>
        <button
          className="pane-header-button"
          title={t('panes.popOut')}
          aria-label={t('panes.popOut')}
          onClick={() => void popOut(view)}
        >
          <ExternalLink size={14} strokeWidth={1.75} />
        </button>
        {/* The last pane in the window stays: a content area with nothing in it
            is not a layout, it is a broken window. */}
        {canClose && (
          <button
            className="pane-header-button"
            title={t('panes.close')}
            aria-label={t('panes.close')}
            onClick={() => closePaneById(paneId)}
          >
            <X size={14} strokeWidth={1.75} />
          </button>
        )}
      </span>

      {picking && (
        <>
          <div className="pane-picker-scrim" onClick={() => setPicking(null)} />
          <div className="pane-picker" role="menu" style={{ left: picking.left, top: picking.top }}>
            {PANE_GROUPS.map((group) => (
              <div key={group.key} className="pane-picker-group">
                <div className="pane-picker-group-label">{t(group.key)}</div>
                {group.views.map((option) => (
                  <button
                    key={option}
                    role="menuitemradio"
                    aria-checked={option === view}
                    className={`pane-picker-item${option === view ? ' active' : ''}`}
                    onClick={() => pick(option)}
                  >
                    {t(`shell.view.${option}`)}
                  </button>
                ))}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

/**
 * Tears a pane off into its own window, carrying the project it is looking at
 * and, for the editor, the scene - a window that opened on whichever project
 * happened to be most recent and no scene at all was a window showing "open a
 * project" with nothing able to change it.
 */
export async function popOut(view: MainView): Promise<void> {
  const project = useProjectStore.getState()
  await window.novalist.openPaneWindow({
    view,
    projectPath: project.projectPath,
    chapterGuid: view === 'write' ? project.openChapterGuid : null,
    sceneId: view === 'write' ? project.openSceneId : null
  })
}
