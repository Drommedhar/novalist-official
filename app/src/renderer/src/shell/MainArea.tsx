import { Fragment, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
  paneLeaves,
  useShellStore,
  type MainView,
  type PaneNode
} from '../stores/shellStore'
import { editorPane, useProjectStore } from '../stores/projectStore'
import { PaneHeader } from './PaneHeader'
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
import { AboutView } from '../views/about/AboutView'
import { useExtensionsStore } from '../stores/extensionsStore'
import { HostBridgeOverlays } from './HostBridgeOverlays'

/** Wraps the routed main-area content with the always-present extension-host UI
 * surfaces (toasts, busy-progress, wizard). The overlays read their state from
 * the host-bridge store, so a view switch never disturbs an in-flight dialog.
 *
 * `headers` forces the per-pane header on in a window that holds a single pane:
 * a torn-off pane has no activity bar, so without it there would be no way to
 * change what that window is showing. */
export function MainArea({ headers = 'auto' }: { headers?: 'auto' | 'always' }): React.JSX.Element {
  const panes = useShellStore((s) => s.panes)
  return (
    <>
      <PaneTree node={panes} headers={headers} />
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
function PaneTree({
  node,
  headers
}: {
  node: PaneNode
  headers: 'auto' | 'always'
}): React.JSX.Element {
  const activePaneId = useShellStore((s) => s.activePaneId)
  const setActivePane = useShellStore((s) => s.setActivePane)
  const only = useShellStore((s) => paneLeaves(s.panes).length < 2)
  const leafRef = useRef<HTMLDivElement>(null)
  const extView = useShellStore((s) => s.extView)

  // A new view starts at the top. The .main-area scroller belongs to the pane,
  // not to the view inside it, so switching views left it wherever the last one
  // had been scrolled to - open Settings from a scrolled Dashboard and it came
  // up in the middle. It only looked right when the new view was too short to
  // hold the old offset and the browser clamped it away.
  //
  // Reset here rather than in each of the two dozen view branches: this is the
  // one place every view passes through, and it reads the element instead of
  // holding a ref, so a branch that renders its own .main-area cannot miss it.
  useEffect(() => {
    if (node.kind !== 'leaf') return
    const area = leafRef.current?.querySelector('.main-area')
    if (area) area.scrollTop = 0
  }, [node.kind === 'leaf' ? node.view : null, extView])

  if (node.kind === 'split') {
    return (
      <div className={`pane-split ${node.direction}`}>
        {node.children.map((child, i) => (
          <Fragment key={child.id}>
            {/* Between the slots rather than inside one, so a drag belongs to
                the boundary it moves rather than to either neighbour. */}
            {i > 0 && <PaneDivider split={node} index={i} />}
            <div
              className="pane-slot"
              style={{ flexBasis: `${node.sizes[i] ?? 100 / node.children.length}%` }}
            >
              <PaneTree node={child} headers={headers} />
            </div>
          </Fragment>
        ))}
      </div>
    )
  }

  return (
    <div
      ref={leafRef}
      className={`pane-leaf${!only && node.id === activePaneId ? ' active' : ''}`}
      // Capture, so clicking anything inside a pane makes it the active one
      // without every view having to know panes exist.
      onPointerDownCapture={() => setActivePane(node.id)}
    >
      {(headers === 'always' || !only) && <PaneHeader paneId={node.id} view={node.view} />}
      <MainAreaContent view={node.view} paneId={node.id} />
    </div>
  )
}

/**
 * The boundary between two panes, dragged to change their proportions.
 *
 * Splits shipped fixed at fifty-fifty because nothing ever called the store's
 * resize action - a manuscript beside a narrow column of notes was a shape the
 * data model allowed and the screen would not give you.
 */
function PaneDivider({
  split,
  index
}: {
  split: Extract<PaneNode, { kind: 'split' }>
  index: number
}): React.JSX.Element {
  const { t } = useTranslation()
  const setPaneSizes = useShellStore((s) => s.setPaneSizes)
  const row = split.direction === 'row'

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>): void => {
    event.preventDefault()
    const container = event.currentTarget.parentElement
    if (!container) return
    const total = row ? container.clientWidth : container.clientHeight
    if (total <= 0) return
    const start = row ? event.clientX : event.clientY
    const sizes = split.sizes.slice()
    const before = sizes[index - 1]
    const after = sizes[index]
    const handle = event.currentTarget
    handle.setPointerCapture(event.pointerId)

    const move = (e: PointerEvent): void => {
      const delta = ((row ? e.clientX : e.clientY) - start) / total * 100
      // A pane can be made small but never nothing: a slot dragged to zero is
      // one the writer can no longer grab to bring back.
      const shift = Math.max(-before + 10, Math.min(after - 10, delta))
      const next = sizes.slice()
      next[index - 1] = before + shift
      next[index] = after - shift
      setPaneSizes(split.id, next)
    }
    const up = (): void => {
      handle.releasePointerCapture(event.pointerId)
      handle.removeEventListener('pointermove', move)
      handle.removeEventListener('pointerup', up)
    }
    handle.addEventListener('pointermove', move)
    handle.addEventListener('pointerup', up)
  }

  return (
    <div
      className={`pane-divider ${split.direction}`}
      role="separator"
      aria-label={t('panes.resize')}
      aria-orientation={row ? 'vertical' : 'horizontal'}
      onPointerDown={onPointerDown}
    />
  )
}

function MainAreaContent({ view, paneId }: { view: MainView; paneId: string }): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = view
  const extView = useShellStore((s) => s.extView)
  const extViews = useExtensionsStore((s) => s.views)
  // This pane's own scene: two editors side by side are two scenes, not one
  // scene drawn twice.
  const openSceneId = useProjectStore((s) => editorPane(s, paneId).sceneId)
  const isLoaded = useProjectStore((s) => s.isLoaded)

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
          <EditorFrame paneId={paneId} />
        ) : (
          <div className="main-placeholder">
            <h1>{t('shell.view.write')}</h1>
            {/* A pane split off to hold a second scene starts empty, which is
                the point - it is waiting for a different scene rather than
                showing another copy of the one next to it. */}
            <p>{t(isLoaded ? 'shell.paneAwaitingScene' : 'shell.binderEmpty')}</p>
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

  if (mainView === 'about') {
    return (
      <main className="main-area">
        <AboutView />
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
