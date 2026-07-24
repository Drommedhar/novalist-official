import { useEffect, useState } from 'react'
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
import { useShellStore, type MobileTab, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useCodexStore } from '../stores/codexStore'
import './mobile.css'

// Plan menu items, in the order shown in the native popover. Index maps back from
// window.__novalistPlanSelect. 'findReplace' opens the dialog, not a view.
type PlanningTarget = MainView | 'findReplace'
const PLANNING_TARGETS: PlanningTarget[] = ['timeline', 'plotGrid', 'calendar', 'findReplace']
const PLANNING_LABEL_KEYS = [
  'shell.view.timeline',
  'shell.view.plotGrid',
  'shell.view.calendar',
  'findReplace.title'
]

/**
 * Single-pane mobile layout. The desktop multi-pane shell (activity bar + binder
 * + main + inspector) collapses to one full-screen view at a time; navigation is
 * the native iOS Liquid Glass UITabBar overlaid by RendererHostPage, which calls
 * window.__novalistTab to switch tabs here. v1 scope: dashboard, manuscript
 * (chapter/scene list -> editor), codex, and a More sheet (settings). Planning /
 * world / publish views are feature-flagged off (see ActivityBar mobileHiddenViews).
 */
export function MobileShell(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const tab = useShellStore((s) => s.mobileTab)
  const setTab = useShellStore((s) => s.setMobileTab)
  const setFindReplaceOpen = useShellStore((s) => s.setFindReplaceOpen)
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

  // Localize the native iOS tab bar: the native side ships English fallbacks; the
  // web owns i18n, so push translated titles (in the native tab order) on mount
  // and whenever the language changes.
  useEffect(() => {
    const push = (): void => {
      window.novalist.setTabTitles?.([
        t('mobile.tab.dashboard'),
        t('mobile.tab.write'),
        t('mobile.tab.codex'),
        t('mobile.tab.planning'),
        t('mobile.tab.settings')
      ])
    }
    push()
    i18n.on('languageChanged', push)
    return () => {
      i18n.off('languageChanged', push)
    }
  }, [t, i18n])

  // Bridge for the native UITabBar (RendererHostPage -> EvaluateJavaScript).
  useEffect(() => {
    const w = window as unknown as { __novalistTab?: (key: string) => void }
    w.__novalistTab = (key: string) => {
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
  }, [setTab])

  // The native Liquid Glass tab bar floats above web content and would occlude the
  // writing-hub bottom sheet, so hide it while that sheet is up. The planning menu
  // sits ABOVE the tab bar, so the bar stays visible for it.
  useEffect(() => {
    window.novalist.setNavVisible?.(!inspectorOpen)
  }, [inspectorOpen])

  // Mobile navigates via the tab, but several views gate their data fetch on
  // mainView (e.g. DashboardView only fetches when mainView === 'dashboard').
  // Keep mainView in sync with the tab so those views load on return instead of
  // getting stuck on "Connecting to core".
  useEffect(() => {
    const map: Record<MobileTab, MainView> = {
      dashboard: 'dashboard',
      manuscript: 'write',
      codex: 'codex',
      planning: planningView,
      settings: 'settings'
    }
    useShellStore.getState().setMainView(map[tab])
  }, [tab, planningView])

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
              if (openChapterGuid && openSceneId) void closeTab('primary', openSceneId)
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
      <div className="mobile-content">{content}</div>
      {inEditor && inspectorOpen && (
        <MobileInspectorSheet onClose={() => setInspectorOpen(false)} />
      )}
    </div>
  )
}
