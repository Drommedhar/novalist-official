import { useTranslation } from 'react-i18next'
import { CalendarDates, type CalendarDate } from './calendarDates'
import { EventChip } from './CalendarEventChip'
import { eventTime, eventTooltip, layoutTimed, openScene, pad2, type CalendarEventDto, type CalendarMode } from './calendarEvents'
interface GridProps { dates: CalendarDates; byDate: ReadonlyMap<string, CalendarEventDto[]>; reschedule(target: string): Promise<void>; setDragging(event: CalendarEventDto): void }
interface NavigationProps { dates: CalendarDates; anchor: CalendarDate; jumpTo(date: CalendarDate, mode?: CalendarMode): void }
const HOURS = Array.from({ length: 24 }, (_, i) => i)
export function CalendarWeek({ dates, weekDays, byDate, reschedule, setDragging }: GridProps & { weekDays: CalendarDate[] }): React.JSX.Element {
return (<div className="calendar-week-grid">
    <div className="calendar-week-allday">
      <div className="calendar-week-corner" />
      {weekDays.map((day) => {
        const key = dates.key(day)
        const allDay = (byDate.get(key) ?? []).filter((e) => e.allDay)
        return (
          <div
            key={key}
            className={`calendar-allday-col${dates.isToday(day) ? ' today' : ''}`}
            onDragOver={(e) => e.preventDefault()}
            onDrop={() => void reschedule(key)}
          >
            <div className="calendar-week-daylabel">
              {dates.weekdays[dates.weekday(day)]} {day.day}
            </div>
            {allDay.map((event) => (
              <EventChip
                key={`${event.sceneId}-${key}`}
                event={event}
                compact
                onDragStart={setDragging}
              />
            ))}
          </div>
        )
      })}
    </div>
    <div className="calendar-week-body">
      <div className="calendar-week-hours">
        <div className="calendar-hour-gutter">
          {HOURS.map((h) => (
            <div key={h} className="calendar-hour-label">
              {pad2(h)}:00
            </div>
          ))}
        </div>
        {weekDays.map((day) => <WeekTimedColumn key={dates.key(day)} day={day} dates={dates} byDate={byDate} reschedule={reschedule} setDragging={setDragging} />)}
      </div>
    </div>
  </div>)
}
function WeekTimedColumn({ day, dates, byDate, reschedule, setDragging }: GridProps & { day: CalendarDate }): React.JSX.Element {
  const key = dates.key(day)
  const timed = layoutTimed((byDate.get(key) ?? []).filter((e) => !e.allDay))
  return (
    <div
      key={key}
      className={`calendar-day-track${dates.isToday(day) ? ' today' : ''}`}
      onDragOver={(e) => e.preventDefault()}
      onDrop={() => void reschedule(key)}
    >
      {HOURS.map((h) => (
        <div key={h} className="calendar-hour-line" />
      ))}
      {timed.map((item) => (
        <button
          key={`${item.event.sceneId}-${key}`}
          className="calendar-timed-event"
          draggable
          onDragStart={() => setDragging(item.event)}
          style={{
            top: `${item.topPx}px`,
            height: `${item.heightPx}px`,
            left: `${item.leftPct}%`,
            width: `${item.widthPct}%`
          }}
          title={eventTooltip(item.event)}
          onClick={() => openScene(item.event)}
        >
          <span className="calendar-timed-title">
            {eventTime(item.event)} {item.event.title}
          </span>
          <span className="calendar-timed-chapter">{item.event.chapterTitle}</span>
          {item.event.note && (
            <span className="calendar-timed-note">{item.event.note}</span>
          )}
        </button>
      ))}
    </div>
  )
}
export function CalendarMonth({ dates, anchor, byDate, reschedule, setDragging, jumpTo }: GridProps & NavigationProps): React.JSX.Element {
  const monthStart = { ...anchor, day: 1 }
  const monthGridStart = dates.startOfWeek(monthStart)
  const monthCells = Math.ceil(
    (dates.weekday(monthStart) + dates.daysInMonth(anchor)) / dates.weekdays.length
  ) * dates.weekdays.length
  return (<div className="calendar-month-wrap">
    <div className="calendar-month-weekdays">
      {dates.weekdays.map((name, index) => (
        <div key={index} className="calendar-weekday">
          {name}
        </div>
      ))}
    </div>
    <div className="calendar-month">
      {Array.from({ length: monthCells }, (_, i) => {
        const day = dates.addDays(monthGridStart, i)
        const key = dates.key(day)
        const dayEvents = byDate.get(key) ?? []
        const inMonth = day.month === anchor.month && day.year === anchor.year
        return (
          <div
            key={key}
            className={`calendar-cell${inMonth ? '' : ' outside'}${dates.isToday(day) ? ' today' : ''}`}
            role="button"
            tabIndex={0}
            onDragOver={(e) => e.preventDefault()}
            onDrop={() => void reschedule(key)}
            onClick={() => jumpTo(day, 'week')}
            onKeyDown={(e) => {
              if (e.target !== e.currentTarget) return
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                jumpTo(day, 'week')
              }
            }}
          >
            <div className="calendar-cell-date">{day.day}</div>
            {dayEvents.slice(0, 3).map((event) => (
              <EventChip
                key={`${event.sceneId}-${key}`}
                event={event}
                compact
                onDragStart={setDragging}
              />
            ))}
            {dayEvents.length > 3 && (
              <div className="calendar-overflow">+{dayEvents.length - 3}</div>
            )}
          </div>
        )
      })}
    </div>
  </div>)
}
export function CalendarYear({ dates, anchor, eventsByMonth, jumpTo }: NavigationProps & { eventsByMonth: ReadonlyMap<number, CalendarEventDto[]> }): React.JSX.Element {
const { t } = useTranslation(); return (<div className="calendar-year">
    {dates.months.map((_, index) => {
      const m = index + 1
      const monthEvents = eventsByMonth.get(m) ?? []
      return (
        <div key={m} className="calendar-year-month dashboard-card">
          <button
            className="calendar-year-head"
            onClick={() => jumpTo({ year: anchor.year, month: m, day: 1 }, 'month')}
          >
            <span className="calendar-year-title">{dates.months[m - 1]}</span>
            <span className="calendar-year-scenecount">
              {t('calendar.sceneCount', { count: monthEvents.length })}
            </span>
          </button>
          <div className="calendar-year-list">
            {monthEvents.map((event) => (
              <button
                key={event.sceneId}
                className="calendar-event"
                title={eventTooltip(event)}
                onClick={() => openScene(event)}
              >
                <span className="calendar-event-title">{event.title}</span>
              </button>
            ))}
          </div>
        </div>
      )
    })}
  </div>)
}
