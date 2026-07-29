import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore, type ManuscriptMode } from '../../stores/manuscriptStore'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { useTargetStore } from '../../stores/targetStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from '../../shell/ManuscriptPropertyField'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import { Board } from './Board'
import { rpc as rpcClient } from '../../rpc/client'
import { LazyBlock } from '../../shell/LazyBlock'

// How many chapter blocks are built up front. Everything past this waits until
// it is scrolled near, which is what keeps a fifty-chapter book openable.
const EAGER_SECTIONS = 3

// Reserved height for a block that has not been built yet, so the scrollbar is
// the right length before anything has been measured.
const CORKBOARD_ROW_HEIGHT = 190
const CORKBOARD_CARDS_PER_ROW = 4
const OUTLINER_ROW_HEIGHT = 34

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
  const filterListId = useManuscriptStore((s) => s.filterListId)
  const [savedLists, setSavedLists] = useState<{ id: string; name: string }[]>([])

  useEffect(() => {
    void rpc
      .request<{ id: string; name: string }[]>('smartLists/list')
      .then(setSavedLists)
      .catch(() => setSavedLists([]))
  }, [])
  const sections = useManuscriptStore((s) => s.sections)
  const setMode = useManuscriptStore((s) => s.setMode)
  const setFilter = useManuscriptStore((s) => s.setFilter)
  const load = useManuscriptStore((s) => s.load)
  const groupBy = useManuscriptStore((s) => s.groupBy)
  const composed = useManuscriptStore((s) => s.composed)
  const freeform = useManuscriptStore((s) => s.freeform)
  const definitions = useManuscriptPropsStore((s) => s.definitions)

  useEffect(() => {
    void load()
  }, [load, filterStatus, composed])

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  // Only single-valued things a scene actually carries: dropping a card has to
  // be able to write the answer, which a chapter's status or act is not.
  const groupings = useMemo(() => [
    { key: 'stage', label: t('stages.title') },
    { key: 'chapter', label: t('shell.chapters') },
    { key: 'pov', label: t('common.povWatermark') },
    ...definitions
      .filter((d) => d.scope === 'Scene' && d.type !== 'Date')
      .map((d) => ({ key: `prop:${d.key}`, label: d.label }))
  ], [definitions, t])

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
        {mode === 'corkboard' && (
          <label className="manuscript-filters match-toggle">
            <input
              type="checkbox"
              checked={freeform}
              onChange={(e) => useManuscriptStore.getState().setFreeform(e.target.checked)}
            />
            {t('corkboard.freeform')}
          </label>
        )}
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
        {savedLists.length > 0 && (
          <select
            className="dialog-input manuscript-list-filter"
            value={filterListId}
            aria-label={t('manuscript.filterList')}
            onChange={(e) => void useManuscriptStore.getState().applyList(e.target.value)}
          >
            <option value="">{t('manuscript.filterListNone')}</option>
            {savedLists.map((list) => (
              <option key={list.id} value={list.id}>
                {list.name}
              </option>
            ))}
          </select>
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

interface CardPlacement {
  sceneId: string
  x: number
  y: number
}

/**
 * The corkboard, either grouped by chapter in reading order or freeform.
 *
 * Reading order is the arrangement the binder already shows. Planning on index
 * cards is about the ones it cannot: three piles for three threads, a row of
 * scenes that happen the same night, an outlier pushed aside because it does
 * not fit yet.
 */
function Corkboard(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const freeform = useManuscriptStore((s) => s.freeform)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const chapters = useProjectStore((s) => s.chapters)

  /**
   * The colour of whatever label a scene carries, if any. Built once per
   * chapter change rather than walked per card - the flat scan this replaced
   * was quadratic, and a five-hundred-scene book paid for it on every render.
   */
  const labelColors = useMemo(() => {
    const map = new Map<string, string>()
    for (const chapter of chapters)
      for (const scene of chapter.scenes)
        if (scene.labelColor) map.set(scene.id, scene.labelColor)
    return map
  }, [chapters])
  const labelColor = (sceneId: string): string | undefined => labelColors.get(sceneId)

  if (freeform) return <FreeformCorkboard labelColor={labelColor} />

  return (
    <div className="corkboard">
      {sections.map((section, index) => (
        <LazyBlock
          key={section.chapterGuid}
          eager={index < EAGER_SECTIONS}
          estimatedHeight={
            CORKBOARD_ROW_HEIGHT *
            Math.max(1, Math.ceil(section.scenes.length / CORKBOARD_CARDS_PER_ROW))
          }
        >
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
        </LazyBlock>
      ))}
    </div>
  )
}

/**
 * Cards the writer places, rather than cards the reading order places.
 *
 * Positions are per scene and persist, so the arrangement is still there next
 * week. A scene that has never been placed takes the slot it would have had in
 * reading order, which is why turning freeform on shows the book as it stands
 * instead of every card stacked in the corner.
 */
function FreeformCorkboard({
  labelColor
}: {
  labelColor: (sceneId: string) => string | undefined
}): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const [places, setPlaces] = useState<Record<string, { x: number; y: number }>>({})
  const dragging = useRef<{ sceneId: string; dx: number; dy: number } | null>(null)

  const load = async (): Promise<void> => {
    const placements = await rpcClient.request<CardPlacement[]>('corkboard/placements')
    setPlaces(Object.fromEntries(placements.map((p) => [p.sceneId, { x: p.x, y: p.y }])))
  }

  useEffect(() => {
    void load()
  }, [sections])

  const scenes = sections.flatMap((section) =>
    section.scenes.map((scene) => ({ ...scene, chapterGuid: section.chapterGuid }))
  )

  // The board is as tall and wide as the furthest card, plus a card's worth of
  // room to drop the next one into.
  const extent = scenes.reduce(
    (acc, scene) => {
      const place = places[scene.sceneId]
      if (!place) return acc
      return { x: Math.max(acc.x, place.x + 260), y: Math.max(acc.y, place.y + 200) }
    },
    { x: 0, y: 0 }
  )

  const onPointerDown = (sceneId: string, e: React.PointerEvent<HTMLDivElement>): void => {
    // Only a drag on the card body moves it. The synopsis box and the title
    // button are things to use, not handles to pull.
    if ((e.target as HTMLElement).closest('textarea, button')) return
    const place = places[sceneId] ?? { x: 0, y: 0 }
    dragging.current = { sceneId, dx: e.clientX - place.x, dy: e.clientY - place.y }
    e.currentTarget.setPointerCapture(e.pointerId)
  }

  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>): void => {
    const drag = dragging.current
    if (!drag) return
    setPlaces((current) => ({
      ...current,
      [drag.sceneId]: {
        x: Math.max(0, Math.round(e.clientX - drag.dx)),
        y: Math.max(0, Math.round(e.clientY - drag.dy))
      }
    }))
  }

  const onPointerUp = (): void => {
    const drag = dragging.current
    dragging.current = null
    if (!drag) return
    const place = places[drag.sceneId]
    if (!place) return
    // Written on release rather than on every move: a drag across the board is
    // hundreds of positions and only the last one is an answer.
    void rpcClient.request('corkboard/setPosition', [drag.sceneId, place.x, place.y])
  }

  return (
    <div className="corkboard corkboard-freeform">
      <div className="corkboard-freeform-bar">
        <button
          className="dashboard-range"
          onClick={() => {
            void rpcClient
              .request<CardPlacement[]>('corkboard/reset')
              .then((placements) =>
                setPlaces(Object.fromEntries(placements.map((p) => [p.sceneId, { x: p.x, y: p.y }])))
              )
          }}
        >
          {t('corkboard.arrange')}
        </button>
        <span className="settings-hint">{t('corkboard.freeformHint')}</span>
      </div>
      <div
        className="corkboard-surface"
        style={{ width: extent.x || undefined, height: extent.y || undefined }}
      >
        {scenes.map((scene) => {
          const place = places[scene.sceneId]
          if (!place) return null
          return (
            <div
              key={scene.sceneId}
              className={`corkboard-card corkboard-card-placed${
                selectedIds.includes(scene.sceneId) ? ' selected' : ''
              }`}
              style={{
                left: place.x,
                top: place.y,
                ...(labelColor(scene.sceneId)
                  ? { borderLeft: `3px solid ${labelColor(scene.sceneId)}` }
                  : {})
              }}
              onPointerDown={(e) => onPointerDown(scene.sceneId, e)}
              onPointerMove={onPointerMove}
              onPointerUp={onPointerUp}
            >
              <button
                className="corkboard-card-title"
                onClick={(e) => {
                  if (handleSceneClick(scene.sceneId, e)) return
                  void useProjectStore.getState().openScene(scene.chapterGuid, scene.sceneId)
                }}
              >
                {scene.title}
              </button>
              <textarea
                className="corkboard-synopsis"
                placeholder={t('sceneNotes.synopsisPlaceholder')}
                defaultValue={scene.synopsis ?? ''}
                onBlur={(e) => void setSynopsis(scene.chapterGuid, scene.sceneId, e.target.value)}
              />
              <div className="corkboard-card-words">
                {scene.wordCount.toLocaleString()} {t('shell.words')}
              </div>
            </div>
          )
        })}
      </div>
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
      {sections.map((section, index) => (
        <LazyBlock
          key={section.chapterGuid}
          eager={index < EAGER_SECTIONS}
          estimatedHeight={OUTLINER_ROW_HEIGHT * Math.max(1, section.scenes.length)}
        >
          {section.scenes.map((scene) => (
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
          ))}
        </LazyBlock>
      ))}
    </div>
  )
}
