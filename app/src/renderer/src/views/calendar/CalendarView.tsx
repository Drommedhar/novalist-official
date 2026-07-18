import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'

interface CalendarEventDto {
  date: string
  chapterGuid: string
  sceneId: string
  title: string
  chapterTitle: string
  synopsis: string | null
  allDay: boolean
  startHour: number
  startMinute: number
}

type CalendarMode = 'week' | 'month' | 'year'

const iso = (d: Date): string =>
  `${d.getFullYear().toString().padStart(4, '0')}-${(d.getMonth() + 1)
    .toString()
    .padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`

const startOfWeek = (d: Date): Date => {
  const copy = new Date(d)
  const day = (copy.getDay() + 6) % 7
  copy.setDate(copy.getDate() - day)
  return copy
}

const addDays = (d: Date, days: number): Date => {
  const copy = new Date(d)
  copy.setDate(copy.getDate() + days)
  return copy
}

export function CalendarView(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [mode, setMode] = useState<CalendarMode>('week')
  const [anchor, setAnchor] = useState<Date>(new Date())
  const [events, setEvents] = useState<CalendarEventDto[]>([])
  const [anchorLoaded, setAnchorLoaded] = useState(false)
  const [dragging, setDragging] = useState<CalendarEventDto | null>(null)

  const reschedule = async (target: string): Promise<void> => {
    if (!dragging) return
    await rpc.request('calendar/reschedule', [dragging.chapterGuid, dragging.sceneId, target])
    setDragging(null)
    await load()
  }

  const load = useCallback(async (): Promise<void> => {
    let from: Date
    let to: Date
    if (mode === 'week') {
      from = startOfWeek(anchor)
      to = addDays(from, 6)
    } else if (mode === 'month') {
      from = startOfWeek(new Date(anchor.getFullYear(), anchor.getMonth(), 1))
      to = addDays(from, 41)
    } else {
      from = new Date(anchor.getFullYear(), 0, 1)
      to = new Date(anchor.getFullYear(), 11, 31)
    }
    setEvents(await rpc.request<CalendarEventDto[]>('calendar/get', [iso(from), iso(to)]))
  }, [anchor, mode])

  useEffect(() => {
    if (mainView !== 'calendar') return
    if (!anchorLoaded) {
      void rpc.request<string | null>('calendar/getAnchor').then((saved) => {
        if (saved) {
          const parsed = new Date(saved)
          if (!Number.isNaN(parsed.getTime())) setAnchor(parsed)
        }
        setAnchorLoaded(true)
      })
      return
    }
    void load()
  }, [mainView, anchorLoaded, load])

  const pan = (direction: -1 | 1): void => {
    const next = new Date(anchor)
    if (mode === 'week') next.setDate(next.getDate() + 7 * direction)
    else if (mode === 'month') next.setMonth(next.getMonth() + direction)
    else next.setFullYear(next.getFullYear() + direction)
    setAnchor(next)
    void rpc.request('calendar/setAnchor', [iso(next)])
  }

  const byDate = new Map<string, CalendarEventDto[]>()
  for (const event of events) {
    const list = byDate.get(event.date) ?? []
    list.push(event)
    byDate.set(event.date, list)
  }
  for (const list of byDate.values()) {
    list.sort((a, b) =>
      a.allDay === b.allDay
        ? a.startHour * 60 + a.startMinute - (b.startHour * 60 + b.startMinute)
        : a.allDay
          ? -1
          : 1
    )
  }

  const monthName = (m: number): string =>
    new Date(2000, m, 1).toLocaleString(i18n.language, { month: 'long' })

  const headerLabel =
    mode === 'week'
      ? `${iso(startOfWeek(anchor))} - ${iso(addDays(startOfWeek(anchor), 6))}`
      : mode === 'month'
        ? `${monthName(anchor.getMonth())} ${anchor.getFullYear()}`
        : String(anchor.getFullYear())

  return (
    <div className="calendar">
      <div className="timeline-toolbar">
        {(['week', 'month', 'year'] as CalendarMode[]).map((m) => (
          <button
            key={m}
            className={`dashboard-range${mode === m ? ' active' : ''}`}
            onClick={() => setMode(m)}
          >
            {t(`calendar.${m}View`)}
          </button>
        ))}
        <div className="toolbar-spacer" />
        <button className="toolbar-button" onClick={() => pan(-1)} title={t('timeline.prev')}>
          <ChevronLeft size={15} strokeWidth={2} />
        </button>
        <span className="calendar-header-label">{headerLabel}</span>
        <button className="toolbar-button" onClick={() => pan(1)} title={t('timeline.next')}>
          <ChevronRight size={15} strokeWidth={2} />
        </button>
      </div>

      {mode === 'week' && (
        <div className="calendar-week">
          {Array.from({ length: 7 }, (_, i) => {
            const day = addDays(startOfWeek(anchor), i)
            const key = iso(day)
            return (
              <div
                key={key}
                className="calendar-day-col"
                onDragOver={(e) => e.preventDefault()}
                onDrop={() => void reschedule(key)}
              >
                <div className="calendar-day-head">
                  {day.toLocaleString(i18n.language, { weekday: 'short' })} {day.getDate()}
                </div>
                {(byDate.get(key) ?? []).map((event) => (
                  <EventChip key={`${event.sceneId}-${key}`} event={event} onDragStart={setDragging} />
                ))}
              </div>
            )
          })}
        </div>
      )}

      {mode === 'month' && (
        <div className="calendar-month">
          {Array.from({ length: 42 }, (_, i) => {
            const day = addDays(startOfWeek(new Date(anchor.getFullYear(), anchor.getMonth(), 1)), i)
            const key = iso(day)
            const dayEvents = byDate.get(key) ?? []
            const inMonth = day.getMonth() === anchor.getMonth()
            return (
              <div
                key={key}
                className={`calendar-cell${inMonth ? '' : ' outside'}`}
                onDragOver={(e) => e.preventDefault()}
                onDrop={() => void reschedule(key)}
              >
                <div className="calendar-cell-date">{day.getDate()}</div>
                {dayEvents.slice(0, 3).map((event) => (
                  <EventChip key={`${event.sceneId}-${key}`} event={event} compact onDragStart={setDragging} />
                ))}
                {dayEvents.length > 3 && (
                  <div className="calendar-overflow">+{dayEvents.length - 3}</div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {mode === 'year' && (
        <div className="calendar-year">
          {Array.from({ length: 12 }, (_, m) => {
            const count = events.filter((e) => Number(e.date.slice(5, 7)) === m + 1).length
            return (
              <button
                key={m}
                className="calendar-year-month dashboard-card"
                onClick={() => {
                  setAnchor(new Date(anchor.getFullYear(), m, 1))
                  setMode('month')
                }}
              >
                <div className="dashboard-card-title">{monthName(m)}</div>
                <div className="calendar-year-count">{count > 0 ? count : ''}</div>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

function EventChip({
  event,
  compact,
  onDragStart
}: {
  event: CalendarEventDto
  compact?: boolean
  onDragStart?(event: CalendarEventDto): void
}): React.JSX.Element {
  const time = event.allDay
    ? ''
    : `${event.startHour.toString().padStart(2, '0')}:${event.startMinute
        .toString()
        .padStart(2, '0')} `
  return (
    <button
      className={`calendar-event${compact ? ' compact' : ''}`}
      draggable={Boolean(onDragStart)}
      onDragStart={() => onDragStart?.(event)}
      title={`${event.chapterTitle} - ${event.title}${event.synopsis ? `\n${event.synopsis}` : ''}`}
      onClick={() => void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)}
    >
      {time}
      {event.title}
    </button>
  )
}
