import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore, type ManuscriptMode } from '../../stores/manuscriptStore'
import { rpc } from '../../rpc/client'
import { COLOUR_DIMENSIONS, type ColourDimension } from './sceneColour'
import { FilterBar } from '../../shell/FilterBar'
import { useFilterStore } from '../../stores/filterStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import { Board } from './Board'
import { ManuscriptFrame } from './ManuscriptFrame'
import { Corkboard } from './Corkboard'
import { Outliner } from './Outliner'
import { useContentTransition } from '../../shell/useContentTransition'

const MODES: ManuscriptMode[] = ['manuscript', 'corkboard', 'outliner', 'board']

const FILTERS = ['All', 'Outline', 'FirstDraft', 'Final']

function useManuscriptView() {
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
  const colourBy = useManuscriptStore((s) => s.colourBy)
  const definitions = useManuscriptPropsStore((s) => s.definitions)

  useEffect(() => {
    void load()
  }, [load, filterStatus, composed])

  // The shared status chip and the manuscript's own filter are the same
  // statement about the book, so one follows the other rather than the writer
  // setting it twice and wondering which won.
  const sharedStatus = useFilterStore((s) => s.filter.status)
  useEffect(() => {
    const wanted = sharedStatus || 'All'
    if (wanted !== useManuscriptStore.getState().filterStatus) void setFilter(wanted)
  }, [sharedStatus, setFilter])

  // The rest of the shared filter narrows the book too. Reloading on the whole
  // filter rather than on status alone is the fix for chips that were set,
  // stored, and then read by nothing.
  const sharedCharacter = useFilterStore((s) => s.filter.character)
  const sharedLocation = useFilterStore((s) => s.filter.location)
  const sharedPlotline = useFilterStore((s) => s.filter.plotline)
  const sharedStage = useFilterStore((s) => s.filter.stage)
  useEffect(() => {
    void load()
  }, [load, sharedCharacter, sharedLocation, sharedPlotline, sharedStage])

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  // Only single-valued things a scene actually carries: dropping a card has to
  // be able to write the answer, which a chapter's status or act is not.
  const groupings = useMemo(
    () => [
      { key: 'stage', label: t('stages.title') },
      { key: 'chapter', label: t('shell.chapters') },
      { key: 'pov', label: t('common.povWatermark') },
      ...definitions
        .filter((d) => d.scope === 'Scene' && d.type !== 'Date')
        .map((d) => ({ key: `prop:${d.key}`, label: d.label }))
    ],
    [definitions, t]
  )

  return { mode, setMode, t, freeform, colourBy, groupBy, groupings, composed, savedLists, filterListId, filterStatus, setFilter, sections }
}

export function ManuscriptView(): React.JSX.Element {
  const { mode, setMode, t, freeform, colourBy, groupBy, groupings, composed, savedLists, filterListId, filterStatus, setFilter, sections } = useManuscriptView()
  const contentRef = useRef<HTMLDivElement>(null)
  useContentTransition(contentRef, mode)

  return (
    <div className="manuscript" ref={contentRef}>
      {/* One filter model, shared live with the Timeline. Above the toolbar
          because it narrows the book rather than changing how it is drawn. */}
      <FilterBar />
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
        {/* What the card's edge means. A label is a colour the writer chose;
            the rest are derived from something already typed and coloured by
            hash, so they work the moment there are values to colour. */}
        {mode === 'corkboard' && (
          <div className="manuscript-filters">
            <label className="settings-hint" htmlFor="corkboard-colour">
              {t('corkboard.colourBy')}
            </label>
            <select
              id="corkboard-colour"
              className="inspector-input"
              value={colourBy}
              onChange={(e) =>
                useManuscriptStore.getState().setColourBy(e.target.value as ColourDimension)
              }
            >
              {COLOUR_DIMENSIONS.map((dim) => (
                <option key={dim} value={dim}>
                  {t(`corkboard.colour_${dim}`)}
                </option>
              ))}
            </select>
          </div>
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
