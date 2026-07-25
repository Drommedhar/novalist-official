import { useEffect, useRef, useState } from 'react'
import { ChevronLeft, PanelRightOpen } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Binder } from './Binder'
import { EditorFrame } from '../views/editor/EditorFrame'
import { DashboardView } from '../views/dashboard/DashboardView'
import { CodexView } from '../views/codex/CodexView'
import { MobileWikiView } from '../views/wiki/MobileWikiView'
import { SettingsView } from '../views/settings/SettingsView'
import { TimelineView } from '../views/timeline/TimelineView'
import { PlotGridView } from '../views/plotgrid/PlotGridView'
import { CalendarView } from '../views/calendar/CalendarView'
import { MobileInspectorSheet } from './MobileInspectorSheet'
import { TabletShell } from './TabletShell'
import { useShellStore, type MobileTab, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useCodexStore } from '../stores/codexStore'
import './mobile.css'

// Plan menu items, in the order shown in the native popover. Index maps back from
// window.__novalistPlanSelect. 'findReplace' opens the dialog, not a view.
// The native UITabBar's items, in the order RendererHostPage builds them, so a
// tab can be named back to the index the bar highlights by.
const NATIVE_TAB_ORDER: MobileTab[] = [
  'dashboard',
  'manuscript',
  'codex',
  'planning',
  'settings'
]

type PlanningTarget = MainView | 'findReplace'
const PLANNING_TARGETS: PlanningTarget[] = ['timeline', 'plotGrid', 'calendar', 'findReplace']
const PLANNING_LABEL_KEYS = [
  'shell.view.timeline',
  'shell.view.plotGrid',
  'shell.view.calendar',
  'findReplace.title'
]

/**
 * iPad sidebar destinations, in the order the native side lists them. MUST stay
 * in step with the SidebarItems table in RendererHostPage.cs: the web pushes
 * localized titles positionally, and taps come back as these keys.
 *
 * This is the desktop activity bar (shellStore.activityGroups) plus Write and
 * Settings, minus Git - no `git` binary in the iOS sandbox.
 */
const TABLET_DESTINATIONS: MainView[] = [
  'dashboard',
  'write',
  'manuscript',
  'timeline',
  'plotGrid',
  'calendar',
  'relationships',
  'codex',
  'wiki',
  'maps',
  'research',
  'gallery',
  'export',
  'settings'
]

/**
 * Mobile layout root. Picks between two shells based on the native horizontal
 * size class, which RendererHostPage announces through window.__novalistLayout:
 *
 *   - compact (iPhone, and a narrow iPad Split View / Slide Over window): the
 *     single-pane layout below. The desktop multi-pane shell collapses to one
 *     full-screen view at a time; navigation is the native iOS Liquid Glass
 *     UITabBar, which calls window.__novalistTab to switch tabs here. Scope:
 *     dashboard, manuscript (chapter/scene list -> editor), codex, a Plan drawer,
 *     and settings.
 *   - regular (iPad): TabletShell - a persistent binder beside the real MainArea,
 *     switched by a native Liquid Glass sidebar carrying every desktop view.
 *
 * Because the size class drives both, resizing an iPad window moves the app
 * between the two layouts without a reload, and the web's chrome insets always
 * match the chrome actually on screen.
 */
export function MobileShell(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const layout = useShellStore((s) => s.mobileLayout)
  const tab = useShellStore((s) => s.mobileTab)
  const setTab = useShellStore((s) => s.setMobileTab)
  const setFindReplaceOpen = useShellStore((s) => s.setFindReplaceOpen)
  const mainView = useShellStore((s) => s.mainView)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const closeTab = useProjectStore((s) => s.closeTab)
  // Writing-hub sheet (Context / Footnotes / Notes) raised from the editor.
  const [inspectorOpen, setInspectorOpen] = useState(false)
  // Plan tab: which planning mode is showing, and whether the picker drawer is up.
  const [planningView, setPlanningView] = useState<MainView>('timeline')
  const [planningDrawerOpen, setPlanningDrawerOpen] = useState(false)
  // Codex tab: switch between editing (Codex) and reading (Wiki).
  const [codexMode, setCodexMode] = useState<'codex' | 'wiki'>('codex')

  // The native side owns the size class, so it also owns which layout we render.
  // requestLayout covers the case where the first size-class pass ran before this
  // bundle finished loading and its announcement was therefore lost.
  useEffect(() => {
    const w = window as unknown as { __novalistLayout?: (mode: string) => void }
    w.__novalistLayout = (mode: string) =>
      useShellStore.getState().setMobileLayout(mode === 'tablet' ? 'tablet' : 'phone')
    window.novalist.requestLayout?.()
    return () => {
      delete w.__novalistLayout
    }
  }, [])

  // Expose the layout to CSS. On documentElement rather than a class on the shell
  // so overlays that escape the shell subtree (sheets, dialogs) are scoped too.
  useEffect(() => {
    document.documentElement.dataset.mobileLayout = layout
  }, [layout])

  // Localize the native iOS chrome: the native side ships English fallbacks; the
  // web owns i18n, so push translated titles (in the native tab / sidebar order)
  // on mount and whenever the language changes. Both are pushed regardless of the
  // current size class, so a rotation into the other layout finds them ready.
  useEffect(() => {
    const push = (): void => {
      window.novalist.setTabTitles?.([
        t('mobile.tab.dashboard'),
        t('mobile.tab.write'),
        t('mobile.tab.codex'),
        t('mobile.tab.planning'),
        t('mobile.tab.settings')
      ])
      window.novalist.setSidebarTitles?.(TABLET_DESTINATIONS.map((v) => t(`shell.view.${v}`)))
    }
    push()
    i18n.on('languageChanged', push)
    return () => {
      i18n.off('languageChanged', push)
    }
  }, [t, i18n])

  // Bridge for the native tab bar / sidebar (RendererHostPage -> EvaluateJavaScript).
  useEffect(() => {
    const w = window as unknown as { __novalistTab?: (key: string) => void }
    w.__novalistTab = (key: string) => {
      // The iPad sidebar sends MainView keys straight through - it has one entry
      // per destination, so there is no tab-to-view mapping and no Plan drawer.
      if (layout === 'tablet') {
        if (key === 'codex') useCodexStore.setState({ selectedId: null, selectedRecord: null })
        useShellStore.getState().setMainView(key as MainView)
        return
      }
      // Plan only pops (toggles) its menu over the current view; it does NOT switch
      // the view until a mode is picked (that happens in selectPlanning).
      if (key === 'planning') {
        setPlanningDrawerOpen((open) => !open)
        return
      }
      // Tapping the Codex tab always returns to its list root (clears any open
      // entity detail), so it behaves like re-tapping a native tab.
      if (key === 'codex') {
        useCodexStore.setState({ selectedId: null, selectedRecord: null })
      }
      setPlanningDrawerOpen(false)
      setTab(key as MobileTab)
    }
    return () => {
      delete w.__novalistTab
    }
  }, [setTab, layout])

  // The native Liquid Glass tab bar floats above web content and would occlude the
  // writing-hub bottom sheet, so hide it while that sheet is up. The planning menu
  // sits ABOVE the tab bar, so the bar stays visible for it. The iPad sidebar is
  // on the leading edge and never overlaps the slide-over, so it stays put.
  useEffect(() => {
    if (layout === 'tablet') return
    window.novalist.setNavVisible?.(!inspectorOpen)
  }, [inspectorOpen, layout])

  // Keep the sidebar highlight on the destination actually shown: the web can
  // change mainView on its own (opening a scene from the binder switches to
  // Write), not only through a sidebar tap.
  useEffect(() => {
    if (layout !== 'tablet') return
    window.novalist.setSidebarSelection?.(mainView)
  }, [layout, mainView])

  // The phone navigates via the tab rather than the pane tree, so mainView is
  // kept in sync with it for two reasons: the status bar, the palette and the
  // menu commands all describe where the writer is by reading it, and several
  // views gate their data fetch on it (DashboardView only fetches when mainView
  // is 'dashboard'), so a stale value leaves them on "Connecting to core". The
  // tablet drives mainView directly, so this must not run there and overwrite
  // the sidebar's choice.
  useEffect(() => {
    if (layout === 'tablet') return
    const map: Record<MobileTab, MainView> = {
      dashboard: 'dashboard',
      manuscript: 'write',
      codex: 'codex',
      planning: planningView,
      settings: 'settings'
    }
    useShellStore.getState().setMainView(map[tab])
  }, [tab, planningView, layout])

  // The native bar highlights what was tapped, so a tab switched from here (the
  // first-run tour walks them) has to be pushed across, or the bar names one
  // place while the screen shows another.
  useEffect(() => {
    window.novalist.setSelectedTab?.(NATIVE_TAB_ORDER.indexOf(tab))
  }, [tab])

  const selectPlanning = (target: PlanningTarget): void => {
    setPlanningDrawerOpen(false)
    if (target === 'findReplace') {
      setFindReplaceOpen(true)
      return
    }
    // Only now switch to the Plan tab and show the chosen mode.
    setPlanningView(target)
    setTab('planning')
  }

  // The Plan menu is rendered natively (same Liquid Glass as the tab bar, anchored
  // to the Plan button). Drive its visibility + localized labels from here, and
  // receive selection / dismissal back through window callbacks.
  useEffect(() => {
    const w = window as unknown as {
      __novalistPlanSelect?: (index: number) => void
      __novalistPlanDismiss?: () => void
    }
    w.__novalistPlanSelect = (index: number) => {
      const target = PLANNING_TARGETS[index]
      if (target) selectPlanning(target)
    }
    w.__novalistPlanDismiss = () => setPlanningDrawerOpen(false)
    return () => {
      delete w.__novalistPlanSelect
      delete w.__novalistPlanDismiss
    }
  }, [])

  useEffect(() => {
    window.novalist.setPlanningMenuOpen?.(
      planningDrawerOpen,
      PLANNING_LABEL_KEYS.map((k) => t(k))
    )
  }, [planningDrawerOpen, t])

  const inEditor = tab === 'manuscript' && !!openSceneId

  // A tab starts at the top. .mobile-content is one scroller shared by every
  // tab, so its offset outlived the view in it: leaving a scrolled Dashboard for
  // Settings dropped the writer into the middle of Settings. It only appeared to
  // behave when the next tab was too short to hold the offset.
  //
  // Declared above the tablet return: it is a hook, and React counts hooks per
  // render. Rotating an iPad between the two layouts changes which branch runs,
  // so a hook below the return would change the count and tear the tree down.
  const contentRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (contentRef.current) contentRef.current.scrollTop = 0
  }, [tab, planningView, codexMode, inEditor])

  // iPad: the two-pane layout takes over completely. Every hook above has already
  // run, so a resize that flips the layout never changes the hook order.
  if (layout === 'tablet') return <TabletShell />

  let content: React.JSX.Element
  if (tab === 'dashboard') content = <DashboardView />
  else if (tab === 'codex')
    content = (
      <div className="mobile-codex">
        <div className="mobile-segment" role="tablist" aria-label={t('shell.view.codex')}>
          <button
            type="button"
            role="tab"
            aria-selected={codexMode === 'codex'}
            className={`mobile-segment-btn${codexMode === 'codex' ? ' active' : ''}`}
            onClick={() => setCodexMode('codex')}
          >
            {t('shell.view.codex')}
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={codexMode === 'wiki'}
            className={`mobile-segment-btn${codexMode === 'wiki' ? ' active' : ''}`}
            onClick={() => setCodexMode('wiki')}
          >
            {t('shell.view.wiki')}
          </button>
        </div>
        <div className="mobile-codex-body">
          {codexMode === 'codex' ? <CodexView /> : <MobileWikiView />}
        </div>
      </div>
    )
  else if (tab === 'settings') content = <SettingsView />
  else if (tab === 'planning')
    content =
      planningView === 'plotGrid' ? (
        <PlotGridView />
      ) : planningView === 'calendar' ? (
        <CalendarView />
      ) : (
        <TimelineView />
      )
  else content = inEditor ? <EditorFrame /> : <Binder />

  return (
    <div className="mobile-shell">
      {inEditor && (
        <div className="mobile-editor-bar">
          <button
            type="button"
            className="mobile-back"
            aria-label={t('shell.view.manuscript')}
            onClick={() => {
              const pane = useProjectStore.getState().activeEditorPaneId
              if (pane && openChapterGuid && openSceneId) void closeTab(pane, openSceneId)
            }}
          >
            <ChevronLeft size={20} strokeWidth={2} />
            <span>{t('shell.chapters')}</span>
          </button>
          <button
            type="button"
            className="mobile-editor-inspector"
            aria-label={t('shell.inspector')}
            onClick={() => setInspectorOpen(true)}
          >
            <PanelRightOpen size={20} strokeWidth={2} />
          </button>
        </div>
      )}
      <div className="mobile-content" ref={contentRef}>
        {content}
      </div>
      {inEditor && inspectorOpen && (
        <MobileInspectorSheet onClose={() => setInspectorOpen(false)} />
      )}
    </div>
  )
}
