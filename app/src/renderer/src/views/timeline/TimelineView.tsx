import { timelinePresentation, type TimelineDto, type Pending } from './timelineModel'
export type { TimelineEventDto } from './timelineModel'
import { TimelineToolbar } from './TimelineControls'
import { TimelineEventGroups, TimelineComparison } from './TimelineEvents'
import { TimelineDialogs } from './TimelineDialogs'

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { rpc } from '../../rpc/client'
import { StructurePanel } from './StructurePanel'

import { useFilterStore } from '../../stores/filterStore'
import { useIsPhone } from '../../shell/useIsPhone'
import { FilterBar } from '../../shell/FilterBar'

import './timeline.css'

export function useTimelineData() {
  const { t } = useTranslation()
  const [data, setData] = useState<TimelineDto | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const isPhone = useIsPhone()
  /** Phone only: whether the timeline's setup controls are showing. */
  const [toolbarOpen, setToolbarOpen] = useState(false)
  const [sourceFilter, setSourceFilter] = useState('all')
  // Filtering hides the threads being compared, which is exactly wrong for
  // "does this POV vanish for eighty pages". Lanes show them side by side.
  const [laneBy, setLaneBy] = useState('none')
  // A flashback sorts by its date like everything else, which is right for
  // chronology and wrong for "what does the reader meet next".
  const [readingOrder, setReadingOrder] = useState(false)
  // Plotlines are stored by id; a lane headed with a GUID says nothing.
  const [plotlineNames, setPlotlineNames] = useState<Record<string, string>>({})
  // The shared model rather than local state: narrowing to one character in the
  // Manuscript and finding the Timeline still showing everyone is the thing
  // this replaces.
  const shared = useFilterStore((s) => s.filter)
  const characterFilter = shared.character
  const locationFilter = shared.location
  // Naming a new timeline, or renaming the one being shown.
  const [addingTimeline, setAddingTimeline] = useState(false)
  const [renamingTimeline, setRenamingTimeline] = useState(false)
  const [removingTimeline, setRemovingTimeline] = useState(false)
  const [structureOpen, setStructureOpen] = useState(false)
  const [structures, setStructures] = useState<
    { id: string; displayName: string; description: string }[]
  >([])
  const [anchorDate, setAnchorDate] = useState<Date | null>(null)
  const [highlightedKey, setHighlightedKey] = useState<string | null>(null)
  const groupRefs = useRef<Map<string, HTMLDivElement>>(new Map())

  useEffect(() => {
    void rpc.request<TimelineDto>('timeline/get').then(setData)
    void rpc
      .request<{ id: string; displayName: string; description: string }[]>(
        'timeline/structureTemplates'
      )
      .then(setStructures)
      .catch(() => setStructures([]))
  }, [])

  // Plotlines are stored by id; a lane headed with a GUID says nothing.
  useEffect(() => {
    void rpc
      .request<{ plotlines: { id: string; name: string }[] }>('plot/grid')
      .then((grid) =>
        setPlotlineNames(Object.fromEntries(grid.plotlines.map((p) => [p.id, p.name])))
      )
      .catch(() => setPlotlineNames({}))
  }, [])

  return { t, data, setData, pending, setPending, isPhone, toolbarOpen, setToolbarOpen, sourceFilter, setSourceFilter, laneBy, setLaneBy, readingOrder, setReadingOrder, plotlineNames, characterFilter, locationFilter, addingTimeline, setAddingTimeline, renamingTimeline, setRenamingTimeline, removingTimeline, setRemovingTimeline, structureOpen, setStructureOpen, structures, setStructures, anchorDate, setAnchorDate, highlightedKey, setHighlightedKey, groupRefs }
}

export function TimelineView(): React.JSX.Element {
  const base = useTimelineData()
  if (!base.data) return <div className="main-placeholder">{base.t('shell.backendConnecting')}</div>
  const state = timelinePresentation({ ...base, data: base.data })
  const { setPending, t, isPhone, toolbarOpen, setToolbarOpen, setData, structures, structureOpen, setStructureOpen, availableCharacters, characterFilter, availableLocations, locationFilter, readingOrder, setReadingOrder, laneBy, setLaneBy, sourceFilter, setSourceFilter, data, chooseTimeline, setRenamingTimeline, setRemovingTimeline, setAddingTimeline, setView, pan, scrollToDate, anchorDate, setStructures, lanes, openLinkedChapter, matchesFilters, highlightedKey, groupRefs, spanOf, renderChip, addingTimeline, renamingTimeline, removingTimeline, pending, save, manualId } = state
  return (
    <div className="timeline">
      <FilterBar />
      <TimelineToolbar setPending={setPending} t={t} isPhone={isPhone} toolbarOpen={toolbarOpen} setToolbarOpen={setToolbarOpen} setData={setData} structures={structures} structureOpen={structureOpen} setStructureOpen={setStructureOpen} availableCharacters={availableCharacters} characterFilter={characterFilter} availableLocations={availableLocations} locationFilter={locationFilter} readingOrder={readingOrder} setReadingOrder={setReadingOrder} laneBy={laneBy} setLaneBy={setLaneBy} sourceFilter={sourceFilter} setSourceFilter={setSourceFilter} data={data} chooseTimeline={chooseTimeline} setRenamingTimeline={setRenamingTimeline} setRemovingTimeline={setRemovingTimeline} setAddingTimeline={setAddingTimeline} setView={setView} pan={pan} scrollToDate={scrollToDate} anchorDate={anchorDate} />

      {/* A sub-view of the Timeline rather than its own place: structure is
          what the timeline is about, and it has no meaning without one. */}
      {structureOpen && <StructurePanel onTemplatesChanged={setStructures} />}
          <TimelineComparison laneBy={laneBy} lanes={lanes} setPending={setPending} openLinkedChapter={openLinkedChapter} readingOrder={readingOrder} data={data} matchesFilters={matchesFilters} t={t} />

          <TimelineEventGroups data={data} laneBy={laneBy} readingOrder={readingOrder} highlightedKey={highlightedKey} groupRefs={groupRefs} matchesFilters={matchesFilters} setPending={setPending} openLinkedChapter={openLinkedChapter} t={t} spanOf={spanOf} renderChip={renderChip} />

          <TimelineDialogs addingTimeline={addingTimeline} t={t} setAddingTimeline={setAddingTimeline} setData={setData} renamingTimeline={renamingTimeline} data={data} setRenamingTimeline={setRenamingTimeline} removingTimeline={removingTimeline} setRemovingTimeline={setRemovingTimeline} pending={pending} setPending={setPending} save={save} manualId={manualId} />

    </div>
  )
}
