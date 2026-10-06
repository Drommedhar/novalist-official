import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import {
  INSPECTOR_MAX,
  INSPECTOR_MIN,
  panelWidthForShell,
  savePanelSize,
  useShellStore
} from '../stores/shellStore'
import { rpc } from '../rpc/client'
import { LinksPanel } from './LinksPanel'
import { DarlingsPanel } from './DarlingsPanel'
import { TasksPanel } from './TasksPanel'
import { CraftPanel } from './CraftPanel'
import { RubricPanel } from './RubricPanel'
import { ContextPanel } from './ContextPanel'
import { AnnotationsPanel } from './AnnotationsPanel'
import { SuggestionsPanel } from './SuggestionsPanel'
import { InboxPanel } from './InboxPanel'
import { PanelResizer } from './PanelResizer'
import { SceneNotesFields } from './SceneNotesFields'
import { useContentTransition } from './useContentTransition'
import './inspector.css'

interface SceneMeta {
  storyDate?: string
  isoDate?: string | null
}

/**
 * Right-hand context sidebar, mirroring the desktop Context / Footnotes tabs.
 * Notes are available here and in the optional bottom dock; snapshots stay in their dialog.
 */
export function Inspector({ onReadyChange, prepareHidden = false }: {
  onReadyChange?(ready: boolean): void
  prepareHidden?: boolean
}): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const inspectorTab = useShellStore((s) => s.inspectorTab)
  const contentRef = useRef<HTMLDivElement>(null)
  useContentTransition(contentRef, inspectorTab)
  const setInspectorTab = useShellStore((s) => s.setInspectorTab)
  const preferredInspectorWidth = useShellStore((s) => s.inspectorWidth)
  const shellWidth = useShellStore((s) => s.shellWidth)
  const inspectorWidth = panelWidthForShell(
    Math.max(preferredInspectorWidth, shellWidth >= 1800 ? 368 : 344),
    shellWidth,
    INSPECTOR_MIN,
    INSPECTOR_MAX
  )
  const setInspectorWidth = useShellStore((s) => s.setInspectorWidth)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)
  const sceneIndex = chapter ? chapter.scenes.findIndex((sc) => sc.id === openSceneId) + 1 : 0
  const sceneTotal = chapter?.scenes.length ?? 0

  const [storyDate, setStoryDate] = useState('')
  const [isoDate, setIsoDate] = useState<string | null>(null)
  const [metaReady, setMetaReady] = useState(false)
  const [contextReady, setContextReady] = useState(false)
  const [linksReady, setLinksReady] = useState(false)
  const linksLoaded = useCallback(() => setLinksReady(true), [])

  useEffect(() => {
    onReadyChange?.(!scene || (metaReady && (inspectorTab !== 'context' || (contextReady && linksReady))))
  }, [scene, metaReady, contextReady, linksReady, inspectorTab, onReadyChange])

  useEffect(() => {
    let cancelled = false
    setMetaReady(false)
    setContextReady(false)
    setLinksReady(false)
    setStoryDate('')
    setIsoDate(null)
    if (openChapterGuid && openSceneId) {
      // The resolved story date lives in the manifest; fetch on switch.
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => {
          if (cancelled) return
          setStoryDate(meta.storyDate ?? '')
          setIsoDate(meta.isoDate ?? null)
        })
        .catch(() => {})
        .finally(() => { if (!cancelled) setMetaReady(true) })
    }
    return () => { cancelled = true }
  }, [openChapterGuid, openSceneId])

  if (!openSceneId || !openChapterGuid || !scene) {
    return (
      <aside className="inspector" style={{ width: inspectorWidth }}>
        <PanelResizer
          edge="left"
          width={inspectorWidth}
          onResize={setInspectorWidth}
          onResizeEnd={(px) => savePanelSize({ inspectorWidth: px })}
        />
        <div className="inspector-header">{t('shell.inspector')}</div>
        <div className="inspector-placeholder">{t('shell.inspectorEmpty')}</div>
      </aside>
    )
  }

  const weekday = isoDate
    ? new Date(`${isoDate}T00:00:00`).toLocaleDateString(i18n.language, { weekday: 'long' })
    : null
  const dateDisplay = storyDate ? (weekday ? `${storyDate} · ${weekday}` : storyDate) : ''
  const positionText =
    sceneTotal > 0
      ? chapter?.title
        ? t('context.sceneOfChapter')
            .replace('{0}', chapter.title)
            .replace('{1}', String(sceneIndex))
            .replace('{2}', String(sceneTotal))
        : t('context.sceneOf').replace('{0}', String(sceneIndex)).replace('{1}', String(sceneTotal))
      : ''

  return (
    <aside className="inspector" style={{ width: inspectorWidth }}>
      <PanelResizer
          edge="left"
          width={inspectorWidth}
          onResize={setInspectorWidth}
          onResizeEnd={(px) => savePanelSize({ inspectorWidth: px })}
        />
      <div className="inspector-scene-summary">
        <h2 className="inspector-title">{t('desktopRefresh.sceneDetails')}</h2>
        <div className="inspector-header">{scene.title}</div>
        {positionText && <div className="inspector-subtitle">{positionText}</div>}
        {dateDisplay && <div className="inspector-date">{dateDisplay}</div>}
        <div className="inspector-meta">
          {scene.wordCount.toLocaleString()} {t('shell.words')}
        </div>
      </div>
      <div className="inspector-tabs">
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'context' ? ' active' : ''}`}
          aria-pressed={inspectorTab === 'context'} onClick={() => setInspectorTab('context')}
        >
          {t('context.tab')}
        </button>
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'footnotes' ? ' active' : ''}`}
          aria-pressed={inspectorTab === 'footnotes'} onClick={() => setInspectorTab('footnotes')}
        >
          {t('footnotes.tab')}
        </button>
        {/* Every open note in the book, not only this scene's: a note you
            cannot find again is a note you did not leave. */}
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'inbox' ? ' active' : ''}`}
          aria-pressed={inspectorTab === 'inbox'} onClick={() => setInspectorTab('inbox')}
        >
          {t('inbox.tab')}
        </button>
        <button type="button" className={`inspector-tab${inspectorTab === 'notes' ? ' active' : ''}`}
          aria-pressed={inspectorTab === 'notes'} onClick={() => setInspectorTab('notes')}>{t('desktopRefresh.notes')}</button>
      </div>
      <div className="inspector-body" ref={contentRef}>
        {inspectorTab === 'notes' && <SceneNotesFields idPrefix="inspector" />}
        {inspectorTab === 'context' && (
          <>
            <ContextPanel chapterGuid={openChapterGuid} sceneId={openSceneId} onReadyChange={setContextReady} prepareHidden={prepareHidden} />
            <LinksPanel chapterGuid={openChapterGuid} sceneId={openSceneId} onReady={linksLoaded} />
            {/* Descriptive analysis says what a scene is. This asks whether it
                works, and says what to try when the answer is no. */}
            <details className="codex-match">
              <summary>{t('rubric.title')}</summary>
              <RubricPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            </details>
            {/* Somewhere to look when the page is blank or the beat will not
                come. The alternative is a browser tab, which is where writing
                sessions go to die. */}
            <details className="codex-match">
              <summary>{t('craft.title')}</summary>
              <CraftPanel />
            </details>
          </>
        )}
        {inspectorTab === 'footnotes' && (
          <AnnotationsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
        )}
        {inspectorTab === 'inbox' && (
          <>
            {/* Somebody else's proposed edits to this scene. They were under
                Footnotes, which is where a writer's own asides live and the
                last place anybody looks for a change another person asked
                for. This is the tab for things waiting on an answer, and the
                book-wide list of scenes with edits is already in it. */}
            <SuggestionsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            <InboxPanel />
            {/* Unfinished business, all in one tab: open notes, prose set
                aside, and the things that belong to no scene at all. */}
            <TasksPanel />
            {/* Same tab as the open notes: both are things the writer set down
                and meant to come back to. */}
            <DarlingsPanel />
          </>
        )}
      </div>
    </aside>
  )
}
