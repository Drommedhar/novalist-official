import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { savePanelSize, useShellStore } from '../stores/shellStore'
import { rpc } from '../rpc/client'
import { LinksPanel } from './LinksPanel'
import { DarlingsPanel } from './DarlingsPanel'
import { TasksPanel } from './TasksPanel'
import { RubricPanel } from './RubricPanel'
import { ContextPanel } from './ContextPanel'
import { AnnotationsPanel } from './AnnotationsPanel'
import { SuggestionsPanel } from './SuggestionsPanel'
import { InboxPanel } from './InboxPanel'
import { PanelResizer } from './PanelResizer'
import './inspector.css'

interface SceneMeta {
  storyDate?: string
  isoDate?: string | null
}

/**
 * Right-hand context sidebar, mirroring the desktop Context / Footnotes tabs.
 * Scene notes + synopsis live in the bottom dock; snapshots live in a dialog.
 */
export function Inspector(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const inspectorTab = useShellStore((s) => s.inspectorTab)
  const setInspectorTab = useShellStore((s) => s.setInspectorTab)
  const inspectorWidth = useShellStore((s) => s.inspectorWidth)
  const setInspectorWidth = useShellStore((s) => s.setInspectorWidth)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)
  const sceneIndex = chapter ? chapter.scenes.findIndex((sc) => sc.id === openSceneId) + 1 : 0
  const sceneTotal = chapter?.scenes.length ?? 0

  const [storyDate, setStoryDate] = useState('')
  const [isoDate, setIsoDate] = useState<string | null>(null)

  useEffect(() => {
    setStoryDate('')
    setIsoDate(null)
    if (openChapterGuid && openSceneId) {
      // The resolved story date lives in the manifest; fetch on switch.
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => {
          setStoryDate(meta.storyDate ?? '')
          setIsoDate(meta.isoDate ?? null)
        })
        .catch(() => {})
    }
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
      <div className="inspector-tabs">
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'context' ? ' active' : ''}`}
          onClick={() => setInspectorTab('context')}
        >
          {t('context.tab')}
        </button>
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'footnotes' ? ' active' : ''}`}
          onClick={() => setInspectorTab('footnotes')}
        >
          {t('footnotes.tab')}
        </button>
        {/* Every open note in the book, not only this scene's: a note you
            cannot find again is a note you did not leave. */}
        <button
          type="button"
          className={`inspector-tab${inspectorTab === 'inbox' ? ' active' : ''}`}
          onClick={() => setInspectorTab('inbox')}
        >
          {t('inbox.tab')}
        </button>
      </div>
      <div className="inspector-body">
        {inspectorTab === 'context' && (
          <>
            <div className="inspector-header">{scene.title}</div>
            {positionText && <div className="inspector-subtitle">{positionText}</div>}
            {dateDisplay && <div className="inspector-date">{dateDisplay}</div>}
            <div className="inspector-meta">
              {scene.wordCount.toLocaleString()} {t('shell.words')}
            </div>
            <ContextPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            <LinksPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            {/* Descriptive analysis says what a scene is. This asks whether it
                works, and says what to try when the answer is no. */}
            <details className="codex-match">
              <summary>{t('rubric.title')}</summary>
              <RubricPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            </details>
          </>
        )}
        {inspectorTab === 'footnotes' && (
          <>
            <SuggestionsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
            <AnnotationsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
          </>
        )}
        {inspectorTab === 'inbox' && (
          <>
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
