import { useEffect } from 'react'
import { ChevronLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Binder } from './Binder'
import { EditorFrame } from '../views/editor/EditorFrame'
import { DashboardView } from '../views/dashboard/DashboardView'
import { CodexView } from '../views/codex/CodexView'
import { SettingsView } from '../views/settings/SettingsView'
import { useShellStore, type MobileTab } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
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
  const { t } = useTranslation()
  const tab = useShellStore((s) => s.mobileTab)
  const setTab = useShellStore((s) => s.setMobileTab)
  const setFindReplaceOpen = useShellStore((s) => s.setFindReplaceOpen)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const closeTab = useProjectStore((s) => s.closeTab)

  // Bridge for the native UITabBar (RendererHostPage -> EvaluateJavaScript).
  useEffect(() => {
    const w = window as unknown as { __novalistTab?: (key: string) => void }
    w.__novalistTab = (key: string) => {
      if (key === 'search') {
        setFindReplaceOpen(true)
        return
      }
      setTab(key as MobileTab)
    }
    return () => {
      delete w.__novalistTab
    }
  }, [setTab, setFindReplaceOpen])

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
            <span>{t('shell.view.manuscript')}</span>
          </button>
        </div>
      )}
      <div className="mobile-content">{content}</div>
    </div>
  )
}
