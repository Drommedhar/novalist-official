import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore, type ManuscriptMode } from '../../stores/manuscriptStore'
import { rpc } from '../../rpc/client'
import { COLOUR_DIMENSIONS, type ColourDimension } from './sceneColour'
import { FilterBar } from '../../shell/FilterBar'
import { useFilterStore } from '../../stores/filterStore'
import { useHostBridgeStore } from '../../stores/hostBridgeStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import { Board } from './Board'
import { ManuscriptFrame } from './ManuscriptFrame'
import { Corkboard } from './Corkboard'
import { Outliner } from './Outliner'
import { useContentTransition } from '../../shell/useContentTransition'
import { useSharedProjectRevision } from '../../collaboration/sharedProjectNotifications'

const MODES: ManuscriptMode[] = ['manuscript', 'corkboard', 'outliner', 'board']

function reportLoadFailure(error: unknown): void {
  useHostBridgeStore.getState().pushToast(error instanceof Error ? error.message : String(error))
}

function useManuscriptView() {
  const { t } = useTranslation()
  const mode = useManuscriptStore((s) => s.mode)
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
  const load = useManuscriptStore((s) => s.load)
  const groupBy = useManuscriptStore((s) => s.groupBy)
  const composed = useManuscriptStore((s) => s.composed)
  const freeform = useManuscriptStore((s) => s.freeform)
  const colourBy = useManuscriptStore((s) => s.colourBy)
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sharedRevision = useSharedProjectRevision()

  const filter = useFilterStore((s) => s.filter)
  useEffect(() => {
    void load().catch(reportLoadFailure)
  }, [load, filter, composed, sharedRevision])

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

  return { mode, setMode, t, freeform, colourBy, groupBy, groupings, composed, savedLists, filterListId, sections }
}

export function ManuscriptView(): React.JSX.Element {
  const { mode, setMode, t, freeform, colourBy, groupBy, groupings, composed, savedLists, filterListId, sections } = useManuscriptView()
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
        {/* Labels and stages use the writer's configured colours;
            viewpoint and act names get stable colours from their text. */}
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
              onClick={() => void useManuscriptStore.getState().compose(null).catch(reportLoadFailure)}
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
            onChange={(e) => void useManuscriptStore.getState().applyList(e.target.value).catch(reportLoadFailure)}
          >
            <option value="">{t('manuscript.filterListNone')}</option>
            {savedLists.map((list) => (
              <option key={list.id} value={list.id}>
                {list.name}
              </option>
            ))}
          </select>
        )}
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
