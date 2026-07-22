import { useEffect, useState } from 'react'
import { ChevronLeft, PanelRightOpen } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Binder } from './Binder'
import { EditorFrame } from '../views/editor/EditorFrame'
import { DashboardView } from '../views/dashboard/DashboardView'
import { CodexView } from '../views/codex/CodexView'
import { SettingsView } from '../views/settings/SettingsView'
import { MobileInspectorSheet } from './MobileInspectorSheet'
import { useShellStore, type MobileTab, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useCodexStore } from '../stores/codexStore'
import './mobile.css'

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

  // Localize the native iOS tab bar: the native side ships English fallbacks; the
  // web owns i18n, so push translated titles (in the native tab order) on mount
  // and whenever the language changes.
  useEffect(() => {
    const push = (): void => {
      window.novalist.setTabTitles?.([
        t('mobile.tab.dashboard'),
        t('mobile.tab.write'),
        t('mobile.tab.codex'),
        t('mobile.tab.search'),
        t('mobile.tab.more')
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
      if (key === 'search') {
        setFindReplaceOpen(true)
        return
      }
      // Tapping the Codex tab always returns to its list root (clears any open
      // entity detail), so it behaves like re-tapping a native tab.
      if (key === 'codex') {
        useCodexStore.setState({ selectedId: null, selectedRecord: null })
      }
      setTab(key as MobileTab)
    }
    return () => {
      delete w.__novalistTab
    }
  }, [setTab, setFindReplaceOpen])

  // The native Liquid Glass tab bar is a native view that always floats above web
  // content, so it would occlude a web bottom sheet. Hide it while the writing-hub
  // sheet is up (it's a focused modal with its own dismiss); restore on close.
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
      more: 'settings'
    }
    useShellStore.getState().setMainView(map[tab])
  }, [tab])

  const inEditor = tab === 'manuscript' && !!openSceneId

  let content: React.JSX.Element
  if (tab === 'dashboard') content = <DashboardView />
  else if (tab === 'codex') content = <CodexView />
  else if (tab === 'more') content = <SettingsView />
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
