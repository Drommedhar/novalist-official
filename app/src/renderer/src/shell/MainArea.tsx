import { Fragment, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
  paneLeaves,
  findPane,
  useShellStore,
  type MainView,
  type PaneNode
} from '../stores/shellStore'
import { editorPane, useProjectStore } from '../stores/projectStore'
import { PaneHeader } from './PaneHeader'
import { ViewIntro } from './ViewIntro'
import { DesktopViewFrame } from './DesktopViewFrame'
import { EditorFrame } from '../views/editor/EditorFrame'
import { CodexView } from '../views/codex/CodexView'
import { WikiView } from '../views/wiki/WikiView'
import { DashboardView } from '../views/dashboard/DashboardView'
import { ManuscriptView } from '../views/manuscript/ManuscriptView'
import { PlotGridView } from '../views/plotgrid/PlotGridView'
import { DraftsView } from '../views/drafts/DraftsView'
import { TimelineView } from '../views/timeline/TimelineView'
import { CalendarView } from '../views/calendar/CalendarView'
import { RelationshipsView } from '../views/relationships/RelationshipsView'
import { DialogueView } from '../views/dialogue/DialogueView'
import { NarrationView } from '../views/narration/NarrationView'
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
import {
  extensionsAvailable,
  ExtensionsUnavailableView
} from '../views/extensions/ExtensionsUnavailable'
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
  const suspended = useProjectStore((s) => s.workspaceSuspended)
  return (
    <>
      {!suspended && <PaneTree node={panes} headers={headers} />}
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
  const focused = useShellStore((s) => s.focusMode && s.mainView === 'write' && !s.extView)

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
              className={`pane-slot${focused && !findPane(child, activePaneId) ? ' focus-hidden' : ''}`}
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
      data-view={node.view}
      className={`pane-leaf${!only && node.id === activePaneId ? ' active' : ''}`}
      // Capture, so clicking anything inside a pane makes it the active one
      // without every view having to know panes exist.
      onPointerDownCapture={() => setActivePane(node.id)}
      onFocusCapture={() => setActivePane(node.id)}
    >
      {(headers === 'always' || !only) && <PaneHeader paneId={node.id} view={node.view} />}
      {/* What this view is for, the first time it is opened. Above the content
          rather than over it: a screen you are being told about is a screen you
          should be able to see. */}
      <ViewIntro view={node.view} />
      <DesktopViewFrame view={node.view}>
        <MainAreaContent view={node.view} paneId={node.id} />
      </DesktopViewFrame>
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
      const delta = (((row ? e.clientX : e.clientY) - start) / total) * 100
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
  const extView = useShellStore((s) => s.extView)
  const keepEditor = useShellStore((s) => s.focusMode && s.focusPaneId === paneId)
  const openSceneId = useProjectStore((s) => editorPane(s, paneId).sceneId)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const writing = view === 'write' && !extView
  return (
    <>
      {(writing || keepEditor) && (
        <main className="main-area" style={writing ? undefined : { display: 'none' }}>
          {openSceneId ? (
            <EditorFrame paneId={paneId} />
          ) : (
            <div className="main-placeholder">
              <h1>{t('shell.view.write')}</h1>
              <p>{t(isLoaded ? 'shell.paneAwaitingScene' : 'shell.binderEmpty')}</p>
            </div>
          )}
        </main>
      )}
      {!writing && <NonWritingContent view={view} />}
    </>
  )
}

const CONTENT_VIEWS: Partial<Record<MainView, React.ComponentType>> = {
  codex: CodexView,
  wiki: WikiView,
  dashboard: DashboardView,
  manuscript: ManuscriptView,
  drafts: DraftsView,
  plotGrid: PlotGridView,
  timeline: TimelineView,
  calendar: CalendarView,
  relationships: RelationshipsView,
  dialogue: DialogueView,
  narration: NarrationView,
  canvas: CanvasView,
  style: StyleView,
  series: SeriesView,
  languages: LanguagesView,
  gallery: GalleryView,
  research: ResearchView,
  expose: ExposeView,
  export: ExportView,
  git: GitView,
  settings: SettingsView,
  maps: MapsView,
  about: AboutView,
  extensions: AvailableExtensions
}

function AvailableExtensions(): React.JSX.Element {
  return extensionsAvailable() ? <ExtensionsView /> : <ExtensionsUnavailableView />
}

function NonWritingContent({ view }: { view: MainView }): React.JSX.Element {
  const { t } = useTranslation()
  const extView = useShellStore((s) => s.extView)
  const extViews = useExtensionsStore((s) => s.views)
  const extension = extView && extViews.find(
    (candidate) => candidate.extensionId === extView.extensionId && candidate.key === extView.key
  )
  const Content = CONTENT_VIEWS[view]
  return (
    <main className="main-area">
      {extension ? <ExtensionWebView view={extension} /> : Content ? <Content /> : (
        <div className="main-placeholder">
          <h1>{t(`shell.view.${String(view)}`)}</h1>
          <p>{t('shell.viewPending')}</p>
        </div>
      )}
    </main>
  )
}
