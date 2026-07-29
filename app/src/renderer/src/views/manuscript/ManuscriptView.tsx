import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore, type ManuscriptMode } from '../../stores/manuscriptStore'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { useTargetStore } from '../../stores/targetStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from '../../shell/ManuscriptPropertyField'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import { Board } from './Board'

interface ManuscriptWindow extends Window {
  setManuscript(sectionsJson: string): void
  setTheme(
    bg: string,
    fg: string,
    caret: string,
    selectionBg: string,
    accent: string,
    subtle: string,
    divider: string,
    scrollbarThumb?: string,
    scrollbarThumbHover?: string,
    scrollbarThumbActive?: string
  ): void
  setFont(family: string, size: number): void
  setReadingComfort(lineHeight: number, letterSpacing: number): void
}

const MODES: ManuscriptMode[] = ['manuscript', 'corkboard', 'outliner', 'board']
const FILTERS = ['All', 'Outline', 'FirstDraft', 'Final']

export function ManuscriptView(): React.JSX.Element {
  const { t } = useTranslation()
  const mode = useManuscriptStore((s) => s.mode)
  const filterStatus = useManuscriptStore((s) => s.filterStatus)
  const sections = useManuscriptStore((s) => s.sections)
  const setMode = useManuscriptStore((s) => s.setMode)
  const setFilter = useManuscriptStore((s) => s.setFilter)
  const load = useManuscriptStore((s) => s.load)
  const groupBy = useManuscriptStore((s) => s.groupBy)
  const composed = useManuscriptStore((s) => s.composed)
  const definitions = useManuscriptPropsStore((s) => s.definitions)

  useEffect(() => {
    void load()
  }, [load, filterStatus, composed])

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  // Only single-valued things a scene actually carries: dropping a card has to
  // be able to write the answer, which a chapter's status or act is not.
  const groupings = [
    { key: 'stage', label: t('stages.title') },
    { key: 'chapter', label: t('shell.chapters') },
    { key: 'pov', label: t('common.povWatermark') },
    ...definitions
      .filter((d) => d.scope === 'Scene' && d.type !== 'Date')
      .map((d) => ({ key: `prop:${d.key}`, label: d.label }))
  ]

  return (
    <div className="manuscript">
      <div className="manuscript-toolbar">
        <div className="manuscript-modes">
          {MODES.map((m) => (
            <button
              key={m}
              className={`codex-tab${mode === m ? ' active' : ''}`}
              onClick={() => setMode(m)}
            >
              {t(`manuscript.viewMode${m.charAt(0).toUpperCase() + m.slice(1)}`)}
            </button>
          ))}
        </div>
        {mode === 'board' && (
          <div className="manuscript-filters">
            <label className="settings-hint" htmlFor="board-group">
              {t('board.groupBy')}
            </label>
            <select
              id="board-group"
              className="inspector-input"
              value={groupBy}
              onChange={(e) => useManuscriptStore.getState().setGroupBy(e.target.value)}
            >
              {groupings.map((g) => (
                <option key={g.key} value={g.key}>
                  {g.label}
                </option>
              ))}
            </select>
          </div>
        )}
        {/* A composed run is a state the writer has to be able to leave. */}
        {composed !== null && (
          <div className="manuscript-filters">
            <span className="settings-hint">
              {t('manuscript.composedCount', { count: composed.length })}
            </span>
            <button
              className="dashboard-range"
              onClick={() => void useManuscriptStore.getState().compose(null)}
            >
              {t('manuscript.showWholeBook')}
            </button>
          </div>
        )}
        <div className="manuscript-filters">
          {FILTERS.map((f) => (
            <button
              key={f}
              className={`dashboard-range${filterStatus === f ? ' active' : ''}`}
              onClick={() => void setFilter(f)}
            >
              {f === 'All' ? t('manuscript.filterAll') : t(`dashboard.status${f}`)}
            </button>
          ))}
        </div>
      </div>
      {mode === 'manuscript' && <ManuscriptFrame />}
      {mode === 'corkboard' && <Corkboard />}
      {mode === 'outliner' && <Outliner />}
      {mode === 'board' && <Board />}
      {sections.length === 0 && mode !== 'manuscript' && (
        <p className="codex-empty">{t('shell.binderEmpty')}</p>
      )}
      {mode !== 'manuscript' && <SceneBulkBar />}
    </div>
  )
}

function ManuscriptFrame(): React.JSX.Element {
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const readyRef = useRef(false)
  const sections = useManuscriptStore((s) => s.sections)

  const push = (): void => {
    const win = iframeRef.current?.contentWindow as ManuscriptWindow | null
    if (!win || !readyRef.current) return
    const style = getComputedStyle(document.documentElement)
    const token = (name: string): string => style.getPropertyValue(name).trim()
    win.setTheme(
      token('--nl-surface-editor'),
      token('--nl-text'),
      token('--nl-text'),
      token('--nl-surface-selected'),
      token('--nl-accent'),
      token('--nl-text-subtle'),
      token('--nl-border'),
      // Separate document: browser-painted scrollbars need the colours pushed.
      token('--nl-scrollbar-thumb'),
      token('--nl-scrollbar-thumb-hover'),
      token('--nl-scrollbar-thumb-active')
    )
    // Manuscript mode is the same prose in a longer strip, so it reads with the
    // writer's own face and leading rather than the page's defaults.
    const eff = useSettingsStore.getState().view?.effective
    if (eff) {
      win.setFont(eff.editorFontFamily, eff.editorFontSize)
      win.setReadingComfort(eff.editorLineHeight, eff.editorLetterSpacing)
    }
    const payload = sections.map((s) => ({
      chapterGuid: s.chapterGuid,
      chapterTitle: s.chapterTitle,
      status: s.status,
      act: s.act,
      scenes: s.scenes.map((sc) => ({
        sceneId: sc.sceneId,
        title: sc.title,
        html: sc.html,
        wordCount: sc.wordCount
      }))
    }))
    win.setManuscript(JSON.stringify(payload))
  }

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return
    const store = useManuscriptStore.getState()

    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframe.contentWindow) return
      const raw = (event.data as { novalistManuscript?: string })?.novalistManuscript
      if (typeof raw !== 'string') return
      let message: { type: string; [key: string]: unknown }
      try {
        message = JSON.parse(raw)
      } catch {
        return
      }
      switch (message.type) {
        case 'ready':
          readyRef.current = true
          push()
          break
        case 'sceneContentChanged':
          store.onSceneContentChanged(
            String(message.sceneId),
            String(message.html ?? ''),
            String(message.plainText ?? ''),
            Number(message.wordCount ?? 0)
          )
          break
        case 'cycleStatus':
          void store.cycleStatus(String(message.chapterGuid))
          break
        case 'openScene':
          void useProjectStore
            .getState()
            .openScene(String(message.chapterGuid), String(message.sceneId))
          break
        default:
          break
      }
    }

    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Re-push whenever sections change (e.g. filter or status cycle).
  useEffect(() => {
    push()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sections])

  return (
    <iframe
      ref={iframeRef}
      className="editor-frame"
      src="./editor/manuscript-editor.html"
      title="manuscript"
      sandbox="allow-scripts allow-same-origin"
    />
  )
}

function Corkboard(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const chapters = useProjectStore((s) => s.chapters)

  /** The colour of whatever label a scene carries, if any. */
  const labelColor = (sceneId: string): string | undefined =>
    chapters.flatMap((c) => c.scenes).find((s) => s.id === sceneId)?.labelColor ?? undefined

  return (
    <div className="corkboard">
      {sections.map((section) => (
        <div key={section.chapterGuid}>
          <div className="corkboard-chapter">{section.chapterTitle}</div>
          <div className="corkboard-cards">
            {section.scenes.map((scene) => (
              <div
                key={scene.sceneId}
                className={`corkboard-card${selectedIds.includes(scene.sceneId) ? ' selected' : ''}`}
                // A label the writer named, drawn as the edge of the card. The
                // colour has been on the model for years with nothing reading it.
                style={
                  labelColor(scene.sceneId)
                    ? { borderLeft: `3px solid ${labelColor(scene.sceneId)}` }
                    : undefined
                }
              >
                {/* The title is the selection handle: the synopsis box under it
                    must stay a plain text field, modifier keys and all. */}
                <button
                  className="corkboard-card-title"
                  onClick={(e) => {
                    if (handleSceneClick(scene.sceneId, e)) return
                    void useProjectStore.getState().openScene(section.chapterGuid, scene.sceneId)
                  }}
                >
                  {scene.title}
                </button>
                <textarea
                  className="corkboard-synopsis"
                  placeholder={t('sceneNotes.synopsisPlaceholder')}
                  defaultValue={scene.synopsis ?? ''}
                  onBlur={(e) =>
                    void setSynopsis(section.chapterGuid, scene.sceneId, e.target.value)
                  }
                />
                <div className="corkboard-card-words">
                  {scene.wordCount.toLocaleString()} {t('shell.words')}
                </div>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}

function Outliner(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const setPov = useManuscriptStore((s) => s.setPov)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const targets = useTargetStore((s) => s.targets)
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sceneValues = useManuscriptPropsStore((s) => s.sceneValues)

  useEffect(() => {
    void useTargetStore.getState().load()
    void useManuscriptPropsStore.getState().load()
  }, [])

  // The writer picks which of their fields is worth a column; a dozen fields
  // is not a dozen columns anybody wants to read.
  const columns = definitions.filter((d) => d.scope === 'Scene' && d.showInOutliner)
  const grid = {
    gridTemplateColumns: `180px 160px 1fr 100px 70px 80px${' 120px'.repeat(columns.length)}`
  }

  return (
    <div className="outliner">
      <div className="outliner-row outliner-head" style={grid}>
        <span>{t('shell.chapters')}</span>
        <span>{t('shell.scenes')}</span>
        <span>{t('sceneNotes.synopsisTitle')}</span>
        <span>{t('common.povWatermark')}</span>
        <span>{t('shell.words')}</span>
        <span>{t('targets.column')}</span>
        {columns.map((property) => (
          <span key={property.key}>{property.label}</span>
        ))}
      </div>
      {sections.flatMap((section) =>
        section.scenes.map((scene) => (
          <div
            key={scene.sceneId}
            className={`outliner-row${selectedIds.includes(scene.sceneId) ? ' selected' : ''}`}
            style={grid}
          >
            <span className="outliner-cell">{section.chapterTitle}</span>
            <button
              className="outliner-cell outliner-scene-title"
              onClick={(e) => {
                if (handleSceneClick(scene.sceneId, e)) return
                void useProjectStore.getState().openScene(section.chapterGuid, scene.sceneId)
              }}
            >
              {scene.title}
            </button>
            <input
              className="outliner-input"
              defaultValue={scene.synopsis ?? ''}
              onBlur={(e) => void setSynopsis(section.chapterGuid, scene.sceneId, e.target.value)}
            />
            <input
              className="outliner-input"
              defaultValue={scene.pov ?? ''}
              placeholder={t('common.povWatermark')}
              onBlur={(e) => void setPov(section.chapterGuid, scene.sceneId, e.target.value)}
            />
            <span className="outliner-cell outliner-words">
              {scene.wordCount.toLocaleString()}
            </span>
            {/* Editable in place: the Outliner is where a writer sets targets
                across a run of scenes, and a dialog per scene would be worse. */}
            <input
              className="outliner-input outliner-target"
              type="number"
              min={0}
              defaultValue={
                targets.find((tg) => tg.kind === 'scene' && tg.id === scene.sceneId)?.target ?? ''
              }
              placeholder={t('targets.none')}
              onBlur={(e) =>
                void useTargetStore
                  .getState()
                  .setScene(section.chapterGuid, scene.sceneId, Number(e.target.value) || null)
              }
            />
            {columns.map((property) => (
              <ManuscriptPropertyField
                key={property.key}
                className="outliner-input"
                property={property}
                value={sceneValues[scene.sceneId]?.[property.key] ?? ''}
                onCommit={(value) =>
                  void useManuscriptPropsStore
                    .getState()
                    .setSceneValue(scene.sceneId, property.key, value)
                }
              />
            ))}
          </div>
        ))
      )}
    </div>
  )
}
