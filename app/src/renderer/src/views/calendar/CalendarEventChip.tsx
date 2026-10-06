import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { type CalendarEventDto, eventTime, eventTooltip, openScene } from './calendarEvents'

export function EventChip({
  event,
  compact,
  onDragStart
}: {
  event: CalendarEventDto
  compact?: boolean
  onDragStart?(event: CalendarEventDto): void
}): React.JSX.Element {
  const selected = useSelectionStore((s) => s.sceneIds).includes(event.sceneId)
  const time = event.allDay ? '' : `${eventTime(event)} `
  return (
    <button
      className={`calendar-event${compact ? ' compact' : ''}${selected ? ' selected' : ''}`}
      draggable={Boolean(onDragStart)}
      onDragStart={() => onDragStart?.(event)}
      title={eventTooltip(event)}
      onClick={(e) => {
        e.stopPropagation()
        if (handleSceneClick(event.sceneId, e)) return
        openScene(event)
      }}
    >
      <span className="calendar-event-title">
        {time}
        {event.title}
      </span>
      {event.note && <span className="calendar-event-note">{event.note}</span>}
    </button>
  )
}
