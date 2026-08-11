import { useEffect, useState } from 'react'
import { ListTree, PanelLeft, PanelRightOpen } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Binder } from './Binder'
import { MainArea } from './MainArea'
import { MobileInspectorSheet } from './MobileInspectorSheet'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

/**
 * iPad (regular horizontal size class) layout: a persistent binder beside the
 * routed main view, with the native Liquid Glass sidebar (RendererHostPage)
 * supplying the top-level destination switcher to its left.
 *
 * Unlike the phone's single-pane MobileShell this renders the real MainArea, so
 * every desktop view is reachable rather than the phone's five tabs. The
 * inspector stays a slide-over rather than a third docked pane, which keeps the
 * editor usable in portrait on the smaller iPads.
 *
 * A narrow window (Split View / Slide Over) drops to the compact size class and
 * MobileShell renders the phone layout instead - this component only ever sees
 * regular width.
 */

// Below this the sidebar + binder + editor no longer leave the editor a usable
// column (iPad mini and 11-inch in portrait), so the binder starts collapsed and
// the user opens it from the top bar. Landscape always clears it.
const BINDER_AUTO_WIDTH = 900

/**
 * The binder's share of a tablet window.
 *
 * Not the desktop `binderWidth`, which this used to take: that is a fraction of
 * the DISPLAY tuned for a window opening maximised on a large monitor, and on a
 * tablet the same fraction lands on the desktop MINIMUM - 0.15 of an 11-inch
 * iPad's 1194pt is 179 - so the column arrived at its floor and every chapter
 * title, scene filter and draft picker in it was truncated mid-word. There is no
 * divider to drag on a tablet either, so that default was the last word.
 *
 * Measured off the window rather than the screen, because a tablet rotates and
 * `screen.availWidth` does not describe the pane after a rotation or in Split
 * View. The bounds keep a 13-inch from spending 26% on a list of short titles
 * and keep the narrowest window that still shows the binder legible.
 */
const TABLET_BINDER_FRACTION = 0.26
const TABLET_BINDER_MIN = 260
const TABLET_BINDER_MAX = 380

function tabletBinderWidth(windowWidth: number): number {
  const share = windowWidth * TABLET_BINDER_FRACTION
  return Math.round(Math.min(TABLET_BINDER_MAX, Math.max(TABLET_BINDER_MIN, share)))
}

export function TabletShell(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const [inspectorOpen, setInspectorOpen] = useState(false)
  // null = follow the window width; true/false = the user's explicit choice,
  // which survives rotation until they change it again.
  const [binderOverride, setBinderOverride] = useState<boolean | null>(null)
  const [wideEnough, setWideEnough] = useState(() => window.innerWidth >= BINDER_AUTO_WIDTH)
  const [binderWidth, setBinderWidth] = useState(() => tabletBinderWidth(window.innerWidth))
  // The native sidebar owns its own width; we only tell it which state to be in.
  // Held in the store so it survives this component unmounting on a layout flip.
  const sidebarCollapsed = useShellStore((s) => s.sidebarCollapsed)
  const setSidebarCollapsed = useShellStore((s) => s.setSidebarCollapsed)

  useEffect(() => {
    const onResize = (): void => {
      setWideEnough(window.innerWidth >= BINDER_AUTO_WIDTH)
      setBinderWidth(tabletBinderWidth(window.innerWidth))
    }
    window.addEventListener('resize', onResize)
    onResize()
    return () => window.removeEventListener('resize', onResize)
  }, [])

  const binderOpen = binderOverride ?? wideEnough

  // Re-assert the sidebar state whenever this shell (re)mounts. Coming back from
  // the phone layout the native side may have been rebuilt, and the toggle must
  // describe the sidebar that is actually on screen.
  useEffect(() => {
    window.novalist.setSidebarCollapsed?.(sidebarCollapsed)
    // Mount-time resync only; the toggle pushes its own changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // The expanded sidebar overlays the content, so the native side collapses it on
  // a tap outside or once a destination is picked. Mirror that back or the
  // toggle's pressed state would drift from the sidebar actually on screen.
  useEffect(() => {
    const w = window as unknown as { __novalistSidebarCollapsed?: (collapsed: boolean) => void }
    w.__novalistSidebarCollapsed = (collapsed: boolean) =>
      useShellStore.getState().setSidebarCollapsed(collapsed)
    return () => {
      delete w.__novalistSidebarCollapsed
    }
  }, [])

  // The inspector only has content for a scene, so close it when the user
  // navigates away rather than leaving an empty slide-over up.
  useEffect(() => {
    if (mainView !== 'write' && mainView !== 'manuscript') setInspectorOpen(false)
  }, [mainView])

  const inspectorAvailable =
    (mainView === 'write' && !!openSceneId) || mainView === 'manuscript'

  return (
    <div className="tablet-shell">
      <div className="tablet-topbar">
        <button
          type="button"
          className="tablet-topbar-btn"
          aria-label={t('shell.toggleSidebar')}
          aria-pressed={!sidebarCollapsed}
          onClick={() => {
            const next = !sidebarCollapsed
            setSidebarCollapsed(next)
            window.novalist.setSidebarCollapsed?.(next)
          }}
        >
          <PanelLeft size={18} strokeWidth={2} />
        </button>
        <button
          type="button"
          className="tablet-topbar-btn"
          aria-label={t('shell.toggleBinder')}
          aria-pressed={binderOpen}
          onClick={() => setBinderOverride(!binderOpen)}
        >
          <ListTree size={18} strokeWidth={2} />
        </button>
        <span className="tablet-topbar-title">{t(`shell.view.${mainView}`)}</span>
        {inspectorAvailable && (
          <button
            type="button"
            className="tablet-topbar-btn tablet-topbar-btn-end"
            aria-label={t('shell.inspector')}
            onClick={() => setInspectorOpen(true)}
          >
            <PanelRightOpen size={18} strokeWidth={2} />
          </button>
        )}
      </div>
      <div className="tablet-body">
        {binderOpen && (
          <div className="tablet-binder" style={{ width: binderWidth }}>
            <Binder />
          </div>
        )}
        <div className="tablet-main">
          <MainArea />
        </div>
      </div>
      {inspectorOpen && <MobileInspectorSheet onClose={() => setInspectorOpen(false)} />}
    </div>
  )
}
