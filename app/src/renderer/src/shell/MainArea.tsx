import { useTranslation } from 'react-i18next'
import {
  paneLeaves,
  useShellStore,
  type MainView,
  type PaneNode
} from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { EditorFrame } from '../views/editor/EditorFrame'
import { CodexView } from '../views/codex/CodexView'
import { WikiView } from '../views/wiki/WikiView'
import { DashboardView } from '../views/dashboard/DashboardView'
import { ManuscriptView } from '../views/manuscript/ManuscriptView'
import { PlotGridView } from '../views/plotgrid/PlotGridView'
import { TimelineView } from '../views/timeline/TimelineView'
import { CalendarView } from '../views/calendar/CalendarView'
import { RelationshipsView } from '../views/relationships/RelationshipsView'
import { DialogueView } from '../views/dialogue/DialogueView'
import { StyleView } from '../views/style/StyleView'
import { CanvasView } from '../views/canvas/CanvasView'
import { GalleryView } from '../views/library/GalleryView'
import { LanguagesView } from '../views/languages/LanguagesView'
import { SeriesView } from '../views/series/SeriesView'
import { ResearchView } from '../views/library/ResearchView'
import { ExportView } from '../views/export/ExportView'
import { ExposeView } from '../views/expose/ExposeView'
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
  const panes = useShellStore((s) => s.panes)
  return (
    <>
      <PaneTree node={panes} />
      <HostBridgeOverlays />
    </>
  )
}

/**
 * The content area as a tree of panes.
 *
 * It used to be one view at a time, with the editor allowed to split in two, so
 * a writer wanting the manuscript, the Codex and their notes at once had to
 * pick two and keep swapping for the third.
 *
 * A pane is outlined when it is the active one, because every command that
 * changes a view - the activity bar, the palette, a link in a panel - lands
 * there, and a writer needs to know where their next click will go.
 */
function PaneTree({ node }: { node: PaneNode }): React.JSX.Element {
  const activePaneId = useShellStore((s) => s.activePaneId)
  const setActivePane = useShellStore((s) => s.setActivePane)
  const only = useShellStore((s) => paneLeaves(s.panes).length < 2)

  if (node.kind === 'split') {
    return (
      <div className={`pane-split ${node.direction}`}>
        {node.children.map((child, i) => (
          <div
            key={child.id}
            className="pane-slot"
            style={{ flexBasis: `${node.sizes[i] ?? 100 / node.children.length}%` }}
          >
            <PaneTree node={child} />
          </div>
        ))}
      </div>
    )
  }

  return (
    <div
      className={`pane-leaf${!only && node.id === activePaneId ? ' active' : ''}`}
      // Capture, so clicking anything inside a pane makes it the active one
      // without every view having to know panes exist.
      onPointerDownCapture={() => setActivePane(node.id)}
    >
      <MainAreaContent view={node.view} />
    </div>
  )
}

function MainAreaContent({ view }: { view: MainView }): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = view
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

  if (mainView === 'wiki') {
    return (
      <main className="main-area">
        <WikiView />
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

  if (mainView === 'dialogue') {
    return (
      <main className="main-area">
        <DialogueView />
      </main>
    )
  }

  if (mainView === 'canvas') {
    return (
      <main className="main-area">
        <CanvasView />
      </main>
    )
  }

  if (mainView === 'style') {
    return (
      <main className="main-area">
        <StyleView />
      </main>
    )
  }

  if (mainView === 'series') {
    return (
      <main className="main-area">
        <SeriesView />
      </main>
    )
  }

  if (mainView === 'languages') {
    return (
      <main className="main-area">
        <LanguagesView />
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

  if (mainView === 'expose') {
    return (
      <main className="main-area">
        <ExposeView />
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
