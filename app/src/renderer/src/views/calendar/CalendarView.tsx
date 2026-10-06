import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useRef, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import { useBookScope } from '../../stores/projectStore'
import './calendar.css'
import { CalendarConfigPanel } from './CalendarConfigPanel'
import { CalendarDates, type CalendarDate } from './calendarDates'
import { type CalendarMode } from './calendarEvents'
import { CalendarMonth, CalendarWeek, CalendarYear } from './CalendarGrids'
import { CalendarGoTo } from './CalendarToolbar'
import { useCalendarModel, type CalendarModel } from './useCalendarModel'

export function CalendarView(): React.JSX.Element {
  const scope = useBookScope()
  return <BookCalendarView key={scope} />
}

function BookCalendarView(): React.JSX.Element {
  const { t } = useTranslation()
  const model = useCalendarModel()
  const { dates, anchor, mode, configOpen, savedConfig, byDate, eventsByMonth, reschedule, setDragging, jumpTo } = model
  if (!dates || !anchor) return <p className="settings-hint">{t('calendarConfig.loading')}</p>
  const weekStart = dates.startOfWeek(anchor)
  const weekDays = Array.from({ length: dates.weekdays.length }, (_, i) => dates.addDays(weekStart, i))
  const headerLabel = mode === 'week'
    ? `${dates.label(weekStart)} - ${dates.label(weekDays[weekDays.length - 1])}`
    : mode === 'month'
      ? `${dates.months[anchor.month - 1]} ${dates.formatYear(anchor.year)}`
      : dates.formatYear(anchor.year)
  return (
    <div className="calendar" style={{ '--cal-weekdays': dates.weekdays.length } as CSSProperties}>
      <CalendarToolbar model={model} dates={dates} anchor={anchor} headerLabel={headerLabel} />
      {configOpen && (
        <div className="calendar-config-shell">
          <CalendarConfigPanel onSaved={(next) => void savedConfig(next)} />
        </div>
      )}
      {mode === 'week' && <CalendarWeek dates={dates} weekDays={weekDays} byDate={byDate} reschedule={reschedule} setDragging={setDragging} />}
      {mode === 'month' && <CalendarMonth dates={dates} anchor={anchor} byDate={byDate} reschedule={reschedule} setDragging={setDragging} jumpTo={jumpTo} />}
      {mode === 'year' && <CalendarYear dates={dates} anchor={anchor} eventsByMonth={eventsByMonth} jumpTo={jumpTo} />}
      <SceneBulkBar />
    </div>
  )
}

interface ToolbarProps { model: CalendarModel; dates: CalendarDates; anchor: CalendarDate; headerLabel: string }
function CalendarToolbar({ model, dates, anchor, headerLabel }: ToolbarProps): React.JSX.Element {
  const { t } = useTranslation()
  const goToButton = useRef<HTMLButtonElement>(null)
  const { mode, setMode, goToOpen, setGoToOpen, storyStart, jumpTo, pan, configOpen, setConfigOpen } = model
  return <>
    <div className="timeline-toolbar">
      {(['week', 'month', 'year'] as CalendarMode[]).map((m) => (
        <button key={m} className={`dashboard-range${mode === m ? ' active' : ''}`} onClick={() => setMode(m)}>
          {t(`calendar.${m}View`)}
        </button>
      ))}
      <div className="toolbar-spacer" />
      <button ref={goToButton} className={`dashboard-range${goToOpen ? ' active' : ''}`}
        aria-expanded={goToOpen} aria-controls="calendar-go-to"
        onClick={() => setGoToOpen((open) => !open)}>
        {t('calendar.goToDate')}
      </button>
      <button className="dashboard-range" disabled={!storyStart}
        title={t(storyStart ? 'calendar.storyStartHint' : 'calendar.noStoryDates')}
        onClick={() => { if (storyStart) jumpTo(storyStart) }}>
        {t('calendar.storyStart')}
      </button>
      <div className="calendar-nav">
        <button className="toolbar-button" onClick={() => pan(-1)} title={t('timeline.prev')}>
          <ChevronLeft size={15} strokeWidth={2} />
        </button>
        <span className="calendar-header-label">{headerLabel}</span>
        <button className="toolbar-button" onClick={() => pan(1)} title={t('timeline.next')}>
          <ChevronRight size={15} strokeWidth={2} />
        </button>
      </div>
      <button className={`dashboard-range${configOpen ? ' active' : ''}`}
        onClick={() => setConfigOpen((open) => !open)}>{t('calendarConfig.title')}</button>
    </div>
    {goToOpen && <CalendarGoTo dates={dates} anchor={anchor} jumpTo={jumpTo} close={() => {
      setGoToOpen(false)
      goToButton.current?.focus()
    }} />}
  </>
}
