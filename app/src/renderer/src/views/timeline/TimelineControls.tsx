import { DesktopViewActions } from '../../shell/DesktopViewFrame'

import { ArrowLeftRight, ChevronDown, ChevronLeft, ChevronRight, FileDown, Plus, ZoomIn, Milestone } from 'lucide-react'
import { rpc } from '../../rpc/client'

import { useFilterStore } from '../../stores/filterStore'

import { ZOOMS, toInputValue, type TimelineDto, type timelinePresentation } from './timelineModel'
type TimelineViewState = ReturnType<typeof timelinePresentation>

export function TimelineFilters({ availableCharacters, characterFilter, t, availableLocations, locationFilter, readingOrder, setReadingOrder, laneBy, setLaneBy, sourceFilter, setSourceFilter }: { availableCharacters: TimelineViewState['availableCharacters']; characterFilter: TimelineViewState['characterFilter']; t: TimelineViewState['t']; availableLocations: TimelineViewState['availableLocations']; locationFilter: TimelineViewState['locationFilter']; readingOrder: TimelineViewState['readingOrder']; setReadingOrder: TimelineViewState['setReadingOrder']; laneBy: TimelineViewState['laneBy']; setLaneBy: TimelineViewState['setLaneBy']; sourceFilter: TimelineViewState['sourceFilter']; setSourceFilter: TimelineViewState['setSourceFilter'] }): React.JSX.Element {
  return <>
            {availableCharacters.length > 0 && (
              <select
                className="dialog-input findreplace-scope"
                value={characterFilter}
                onChange={(e) => useFilterStore.getState().set({ character: e.target.value })}
              >
                <option value="">{t('timeline.filterCharacter')}</option>
                {availableCharacters.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            )}
            {availableLocations.length > 0 && (
              <select
                className="dialog-input findreplace-scope"
                value={locationFilter}
                onChange={(e) => useFilterStore.getState().set({ location: e.target.value })}
              >
                <option value="">{t('timeline.filterLocation')}</option>
                {availableLocations.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            )}
            <button
              className={`toolbar-button toolbar-action${readingOrder ? ' active' : ''}`}
              onClick={() => setReadingOrder(!readingOrder)}
            >
              {t(readingOrder ? 'timeline.orderReading' : 'timeline.orderChronological')}
            </button>
            <select
              className="dialog-input findreplace-scope"
              aria-label={t('timeline.lanes')}
              value={laneBy}
              onChange={(e) => setLaneBy(e.target.value)}
            >
              {['none', 'character', 'location', 'pov', 'plotline'].map((key) => (
                <option key={key} value={key}>
                  {t(`timeline.lane_${key}`)}
                </option>
              ))}
            </select>
            <select
              className="dialog-input findreplace-scope"
              value={sourceFilter}
              onChange={(e) => setSourceFilter(e.target.value)}
            >
              {['all', 'act', 'chapter', 'scene', 'manual'].map((s) => (
                <option key={s} value={s}>
                  {s === 'all'
                    ? t('timeline.filterSource')
                    : t(`timeline.source${s.charAt(0).toUpperCase()}${s.slice(1)}`)}
                </option>
              ))}
            </select>
  </>
}

export function TimelineToolbar({ setPending, t, isPhone, toolbarOpen, setToolbarOpen, setData, structures, structureOpen, setStructureOpen, availableCharacters, characterFilter, availableLocations, locationFilter, readingOrder, setReadingOrder, laneBy, setLaneBy, sourceFilter, setSourceFilter, data, chooseTimeline, setRenamingTimeline, setRemovingTimeline, setAddingTimeline, setView, pan, scrollToDate, anchorDate }: { setPending: TimelineViewState['setPending']; t: TimelineViewState['t']; isPhone: TimelineViewState['isPhone']; toolbarOpen: TimelineViewState['toolbarOpen']; setToolbarOpen: TimelineViewState['setToolbarOpen']; setData: TimelineViewState['setData']; structures: TimelineViewState['structures']; structureOpen: TimelineViewState['structureOpen']; setStructureOpen: TimelineViewState['setStructureOpen']; availableCharacters: TimelineViewState['availableCharacters']; characterFilter: TimelineViewState['characterFilter']; availableLocations: TimelineViewState['availableLocations']; locationFilter: TimelineViewState['locationFilter']; readingOrder: TimelineViewState['readingOrder']; setReadingOrder: TimelineViewState['setReadingOrder']; laneBy: TimelineViewState['laneBy']; setLaneBy: TimelineViewState['setLaneBy']; sourceFilter: TimelineViewState['sourceFilter']; setSourceFilter: TimelineViewState['setSourceFilter']; data: TimelineViewState['data']; chooseTimeline: TimelineViewState['chooseTimeline']; setRenamingTimeline: TimelineViewState['setRenamingTimeline']; setRemovingTimeline: TimelineViewState['setRemovingTimeline']; setAddingTimeline: TimelineViewState['setAddingTimeline']; setView: TimelineViewState['setView']; pan: TimelineViewState['pan']; scrollToDate: TimelineViewState['scrollToDate']; anchorDate: TimelineViewState['anchorDate'] }): React.JSX.Element {
  return (
    <div className="timeline-toolbar">
      <DesktopViewActions>
        {' '}
        <button className="dialog-button primary" onClick={() => setPending({ kind: 'create' })}>
          <Plus size={14} strokeWidth={2} />
          {t('timeline.addEvent')}
        </button>
      </DesktopViewActions>
      {/* Adding an event is what the toolbar is for; the other dozen controls
          are how the timeline is set up, and on a phone they wrapped into five
          rows above the events themselves. They fold behind one row, with the
          primary action left out where it can be reached. */}
      {isPhone && (
        <button
          type="button"
          className="toolbar-button toolbar-action"
          aria-expanded={toolbarOpen}
          onClick={() => setToolbarOpen((open) => !open)}
        >
          {toolbarOpen ? (
            <ChevronDown size={14} strokeWidth={2} />
          ) : (
            <ChevronRight size={14} strokeWidth={2} />
          )}
          {t('timeline.viewOptions')}
        </button>
      )}
      {(!isPhone || toolbarOpen) && (
        <>
          <button
            className="toolbar-button toolbar-action"
            onClick={() =>
              void (async () => {
                const output = await window.novalist.saveFile('outline.md')
                if (output) await rpc.request('export/timelineOutline', [output])
              })()
            }
          >
            <FileDown size={14} strokeWidth={2} />
            {t('timeline.exportOutline')}
          </button>
          <select
            className="dialog-input findreplace-scope"
            value=""
            aria-label={t('timeline.applyStructure')}
            onChange={(e) => {
              const id = e.target.value
              if (!id) return
              void rpc.request<TimelineDto>('timeline/applyStructureTemplate', [id]).then(setData)
            }}
          >
            <option value="">{t('timeline.applyStructure')}</option>
            {structures.map((s) => (
              <option key={s.id} value={s.id} title={s.description}>
                {s.displayName}
              </option>
            ))}
          </select>
          <button
            className={`toolbar-button toolbar-action${structureOpen ? ' active' : ''}`}
            onClick={() => setStructureOpen(!structureOpen)}
          >
            <Milestone size={14} strokeWidth={2} />
            {t('structure.title')}
          </button>
          <div className="toolbar-spacer" />
        <TimelineFilters availableCharacters={availableCharacters} characterFilter={characterFilter} t={t} availableLocations={availableLocations} locationFilter={locationFilter} readingOrder={readingOrder} setReadingOrder={setReadingOrder} laneBy={laneBy} setLaneBy={setLaneBy} sourceFilter={sourceFilter} setSourceFilter={setSourceFilter} />

          {/* Only worth showing once there is more than one. A lone select
          with a single option is a control that does nothing. */}
          {(data.timelines.length > 1 || data.activeTimelineId !== '') && (
            <select
              className="dialog-input findreplace-scope"
              value={data.activeTimelineId}
              aria-label={t('timeline.timelines')}
              onChange={(e) => void chooseTimeline(e.target.value)}
            >
              <option value="">{t('timeline.allTimelines')}</option>
              {data.timelines.map((line) => (
                <option key={line.id} value={line.id}>
                  {line.name}
                </option>
              ))}
              <option value="__add">{t('timeline.addTimeline')}</option>
            </select>
          )}
          {data.activeTimelineId !== '' && data.timelines[0]?.id !== data.activeTimelineId && (
            <>
              <button className="toolbar-button" onClick={() => setRenamingTimeline(true)}>
                {t('explorer.contextRename')}
              </button>
              <button className="toolbar-button" onClick={() => setRemovingTimeline(true)}>
                {t('explorer.contextDelete')}
              </button>
            </>
          )}
          {data.timelines.length === 1 && data.activeTimelineId === '' && (
            <button
              className="toolbar-button toolbar-action"
              onClick={() => setAddingTimeline(true)}
            >
              <Plus size={14} strokeWidth={2} />
              {t('timeline.addTimeline')}
            </button>
          )}
          <button
            className="toolbar-button toolbar-action"
            onClick={() =>
              void setView(
                data.viewMode === 'vertical' ? 'horizontal' : 'vertical',
                data.zoomLevel
              )
            }
          >
            <ArrowLeftRight size={14} strokeWidth={2} />
            {data.viewMode === 'vertical'
              ? t('timeline.viewVertical')
              : t('timeline.viewHorizontal')}
          </button>
          <button
            className="toolbar-button toolbar-action"
            onClick={() =>
              void setView(
                data.viewMode,
                ZOOMS[(ZOOMS.indexOf(data.zoomLevel) + 1) % ZOOMS.length]
              )
            }
          >
            <ZoomIn size={14} strokeWidth={2} />
            {t(
              `timeline.zoom${data.zoomLevel.charAt(0).toUpperCase() + data.zoomLevel.slice(1)}`
            )}
          </button>
          <TimelineDateNavigation t={t} pan={pan} scrollToDate={scrollToDate} anchorDate={anchorDate} />
        </>
      )}
    </div>
  )
}

function TimelineDateNavigation({ t, pan, scrollToDate, anchorDate }: { t: TimelineToolbarState['t']; pan: TimelineToolbarState['pan']; scrollToDate: TimelineToolbarState['scrollToDate']; anchorDate: TimelineToolbarState['anchorDate'] }): React.JSX.Element {
  return (
    <div className="timeline-nav">
      <button
        className="toolbar-button timeline-nav-arrow"
        aria-label={t('timeline.prev')}
        title={t('timeline.prev')}
        onClick={() => pan(-1)}
      >
        <ChevronLeft size={14} strokeWidth={2} />
      </button>
      <button
        className="toolbar-button timeline-nav-arrow"
        aria-label={t('timeline.next')}
        title={t('timeline.next')}
        onClick={() => pan(1)}
      >
        <ChevronRight size={14} strokeWidth={2} />
      </button>
      <button
        className="toolbar-button toolbar-action"
        onClick={() => scrollToDate(new Date())}
      >
        {t('timeline.today')}
      </button>
      <input
        type="date"
        className="dialog-input timeline-jump-input"
        aria-label={t('timeline.jumpTo')}
        title={t('timeline.jumpTo')}
        value={anchorDate ? toInputValue(anchorDate) : ''}
        onChange={(e) => {
          const v = e.target.value
          if (!v) return
          const [y, m, d] = v.split('-').map(Number)
          scrollToDate(new Date(y, m - 1, d))
        }}
      />
    </div>
  )
}

type TimelineToolbarState = ReturnType<typeof timelinePresentation>
