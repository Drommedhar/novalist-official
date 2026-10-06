import { useProjectStore } from '../../stores/projectStore'

import { type timelinePresentation } from './timelineModel'
type TimelineViewState = ReturnType<typeof timelinePresentation>

export function TimelineEventGroups({ data, laneBy, readingOrder, highlightedKey, groupRefs, matchesFilters, setPending, openLinkedChapter, t, spanOf, renderChip }: { data: TimelineViewState['data']; laneBy: TimelineViewState['laneBy']; readingOrder: TimelineViewState['readingOrder']; highlightedKey: TimelineViewState['highlightedKey']; groupRefs: TimelineViewState['groupRefs']; matchesFilters: TimelineViewState['matchesFilters']; setPending: TimelineViewState['setPending']; openLinkedChapter: TimelineViewState['openLinkedChapter']; t: TimelineViewState['t']; spanOf: TimelineViewState['spanOf']; renderChip: TimelineViewState['renderChip'] }): React.JSX.Element {
  return <>
      <div className={`timeline-body ${data.viewMode}`} hidden={laneBy !== 'none' || readingOrder}>
        {data.groups.map((group) => (
          <div
            key={group.key}
            className={`timeline-group${highlightedKey === group.key ? ' highlighted' : ''}`}
            ref={(el) => {
              if (el) groupRefs.current.set(group.key, el)
              else groupRefs.current.delete(group.key)
            }}
          >
            <div className="timeline-group-label">{group.label}</div>
            {group.events.filter(matchesFilters).map((event) => (
              <div
                key={event.id}
                className={`timeline-event source-${event.source}`}
                role={event.sceneId || event.isManual || event.chapterGuid ? 'button' : undefined}
                tabIndex={event.sceneId || event.isManual || event.chapterGuid ? 0 : undefined}
                onKeyDown={(e) => {
                  if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) {
                    e.preventDefault()
                    e.currentTarget.click()
                  }
                }}
                onClick={() => {
                  if (event.isManual) setPending({ kind: 'edit', event })
                  else if (event.chapterGuid && event.sceneId)
                    void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)
                  else if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                }}
                onContextMenu={(e) => {
                  if (!event.isManual) return
                  e.preventDefault()
                  setPending({ kind: 'delete', event })
                }}
              >
                <span className="timeline-event-dot" />
                <div className="timeline-event-body">
                  <div className="timeline-event-head">
                    <div className="timeline-event-title">{event.title}</div>
                    <span className={`timeline-source-pill source-${event.source}`}>
                      {t(`timeline.${event.source}Event`)}
                    </span>
                    {event.narrativeMode && (
                      <span className="timeline-mode-pill">
                        {t(`timeline.mode_${event.narrativeMode}`)}
                      </span>
                    )}
                  </div>
                  {event.dateStr && (
                    <div className="timeline-event-date">
                      {event.endDateStr ? `${event.dateStr} - ${event.endDateStr}` : event.dateStr}
                    </div>
                  )}
                  {/* A span drawn rather than described. Duration was computed
                      and printed as "3 weeks" beside a dot, so a war spanning
                      ten chapters and a pregnancy spanning twenty could not be
                      compared and their overlap was invisible. */}
                  {spanOf(event) && (
                    <div className="timeline-span-track" title={t('timeline.spanBar')}>
                      <div
                        className="timeline-span-bar"
                        style={{
                          marginLeft: `${spanOf(event)!.offset}%`,
                          width: `${spanOf(event)!.width}%`
                        }}
                      />
                    </div>
                  )}
                  {event.description && (
                    <div className="timeline-event-desc">{event.description}</div>
                  )}
                  {(event.characters.length > 0 || event.locations.length > 0) && (
                    <div className="timeline-event-chips">
                      {event.characters.map((name) => renderChip(name, `c-${name}`))}
                      {event.locations.map((name) => renderChip(name, `l-${name}`))}
                    </div>
                  )}
                  {event.isManual && event.chapterGuid && (
                    <button
                      className="timeline-open-chapter"
                      onClick={(e) => {
                        e.stopPropagation()
                        if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                      }}
                    >
                      {t('timeline.openChapter')}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        ))}
        {/* On a timeline of its own, "add chapter dates" is advice that
            cannot help: chapters belong to the first timeline. */}
        {data.groups.length === 0 && (
          <p className="codex-empty">
            {t(
              data.activeTimelineId !== '' && data.timelines[0]?.id !== data.activeTimelineId
                ? 'timeline.noEventsOnTimeline'
                : 'timeline.noEvents'
            )}
          </p>
        )}
      </div>
  </>
}

export function TimelineComparison({ laneBy, lanes, setPending, openLinkedChapter, readingOrder, data, matchesFilters, t }: { laneBy: TimelineViewState['laneBy']; lanes: TimelineViewState['lanes']; setPending: TimelineViewState['setPending']; openLinkedChapter: TimelineViewState['openLinkedChapter']; readingOrder: TimelineViewState['readingOrder']; data: TimelineViewState['data']; matchesFilters: TimelineViewState['matchesFilters']; t: TimelineViewState['t'] }): React.JSX.Element {
  return <>
      {laneBy !== 'none' && (
        <div className="timeline-lanes">
          {lanes.map((lane) => (
            <div key={lane.key || 'ungrouped'} className="timeline-lane">
              <div className="timeline-lane-head">
                <span className="timeline-lane-title">{lane.label}</span>
                <span className="timeline-lane-count">{lane.events.length}</span>
              </div>
              <div className="timeline-lane-events">
                {lane.events.map((event) => (
                  <button
                    key={`${lane.key}-${event.id}`}
                    className={`timeline-lane-event source-${event.source}`}
                    title={event.dateStr}
                    onClick={() => {
                      if (event.isManual) setPending({ kind: 'edit', event })
                      else if (event.chapterGuid && event.sceneId)
                        void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)
                      else if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                    }}
                  >
                    {event.title}
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
      {readingOrder && laneBy === 'none' && (
        <div className="timeline-reading">
          {data.groups
            .flatMap((g) => g.events)
            .filter(matchesFilters)
            .filter((event) => event.readingIndex > 0)
            .sort((a, b) => a.readingIndex - b.readingIndex)
            .map((event) => (
              <button
                key={event.id}
                className="timeline-reading-row"
                onClick={() => {
                  if (event.chapterGuid && event.sceneId)
                    void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)
                }}
              >
                {/* R is where the reader meets it, C the date it happens on.
                    Seeing both is the only way to read a flashback correctly. */}
                <span className="timeline-order-badge">R:{event.readingIndex}</span>
                <span className="timeline-order-badge chrono">
                  {event.dateStr || t('timeline.undated')}
                </span>
                {event.narrativeMode && (
                  <span className="timeline-mode-pill">
                    {t(`timeline.mode_${event.narrativeMode}`)}
                  </span>
                )}
                <span className="timeline-reading-title">{event.title}</span>
              </button>
            ))}
        </div>
      )}
  </>
}
