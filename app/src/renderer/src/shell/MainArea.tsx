import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { EditorFrame } from '../views/editor/EditorFrame'
import { CodexView } from '../views/codex/CodexView'
import { DashboardView } from '../views/dashboard/DashboardView'
import { ManuscriptView } from '../views/manuscript/ManuscriptView'
import { PlotGridView } from '../views/plotgrid/PlotGridView'
import { TimelineView } from '../views/timeline/TimelineView'
import { CalendarView } from '../views/calendar/CalendarView'
import { RelationshipsView } from '../views/relationships/RelationshipsView'
import { GalleryView } from '../views/library/GalleryView'
import { ResearchView } from '../views/library/ResearchView'
import { ExportView } from '../views/export/ExportView'
import { GitView } from '../views/git/GitView'
import { SettingsView } from '../views/settings/SettingsView'
import { MapsView } from '../views/maps/MapsView'
import { ExtensionWebView } from '../views/extensions/ExtensionWebView'
import { ExtensionsView } from '../views/extensions/ExtensionsView'
import { useExtensionsStore } from '../stores/extensionsStore'
import { HostBridgeOverlays } from './HostBridgeOverlays'

/** Wraps the routed main-area content with the always-present extension-host UI
 * surfaces (toasts, busy-progress, wizard). The overlays read their state from
 * the host-bridge store, so a view switch never disturbs an in-flight dialog. */
export function MainArea(): React.JSX.Element {
  return (
    <>
      <MainAreaContent />
      <HostBridgeOverlays />
    </>
  )
}

function MainAreaContent(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const extView = useShellStore((s) => s.extView)
  const extViews = useExtensionsStore((s) => s.views)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const splitSceneId = useProjectStore((s) => s.splitSceneId)

  if (extView) {
    const view = extViews.find(
      (v) => v.extensionId === extView.extensionId && v.key === extView.key
    )
    if (view) {
      return (
        <main className="main-area">
          <ExtensionWebView view={view} />
        </main>
      )
    }
  }

  if (mainView === 'write') {
    return (
      <main className="main-area">
        {openSceneId ? (
          splitSceneId ? (
            <div className="split-editors">
              <EditorFrame pane="primary" />
              <EditorFrame pane="split" />
            </div>
          ) : (
            <EditorFrame />
          )
        ) : (
          <div className="main-placeholder">
            <h1>{t('shell.view.write')}</h1>
            <p>{t('shell.binderEmpty')}</p>
          </div>
        )}
      </main>
    )
  }

  if (mainView === 'codex') {
    return (
      <main className="main-area">
        <CodexView />
      </main>
    )
  }

  if (mainView === 'dashboard') {
    return (
      <main className="main-area">
        <DashboardView />
      </main>
    )
  }

  if (mainView === 'manuscript') {
    return (
      <main className="main-area">
        <ManuscriptView />
      </main>
    )
  }

  if (mainView === 'plotGrid') {
    return (
      <main className="main-area">
        <PlotGridView />
      </main>
    )
  }

  if (mainView === 'timeline') {
    return (
      <main className="main-area">
        <TimelineView />
      </main>
    )
  }

  if (mainView === 'calendar') {
    return (
      <main className="main-area">
        <CalendarView />
      </main>
    )
  }

  if (mainView === 'relationships') {
    return (
      <main className="main-area">
        <RelationshipsView />
      </main>
    )
  }

  if (mainView === 'gallery') {
    return (
      <main className="main-area">
        <GalleryView />
      </main>
    )
  }

  if (mainView === 'research') {
    return (
      <main className="main-area">
        <ResearchView />
      </main>
    )
  }

  if (mainView === 'export') {
    return (
      <main className="main-area">
        <ExportView />
      </main>
    )
  }

  if (mainView === 'git') {
    return (
      <main className="main-area">
        <GitView />
      </main>
    )
  }

  if (mainView === 'settings') {
    return (
      <main className="main-area">
        <SettingsView />
      </main>
    )
  }

  if (mainView === 'maps') {
    return (
      <main className="main-area">
        <MapsView />
      </main>
    )
  }

  if (mainView === 'extensions') {
    return (
      <main className="main-area">
        <ExtensionsView />
      </main>
    )
  }

  return (
    <main className="main-area">
      <div className="main-placeholder">
        <h1>{t(`shell.view.${String(mainView)}`)}</h1>
        <p>{t('shell.viewPending')}</p>
      </div>
    </main>
  )
}
