import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CalendarDates, type CalendarDate } from './calendarDates'

interface GoToProps {
  dates: CalendarDates
  anchor: CalendarDate
  jumpTo(date: CalendarDate): void
  close(): void
}

export function CalendarGoTo({ dates, anchor, jumpTo, close }: GoToProps): React.JSX.Element {
  const { t } = useTranslation()
  const [value, setValue] = useState(() => dates.key(anchor))
  const [error, setError] = useState(false)
  return (
    <form id="calendar-go-to" className="calendar-go-to"
      onSubmit={(event) => {
        event.preventDefault()
        const target = dates.parse(value)
        if (!target) { setError(true); return }
        jumpTo(target)
        close()
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          close()
        }
      }}
    >
      <label htmlFor="calendar-go-to-input">{t('calendar.goToDate')}</label>
      <input id="calendar-go-to-input" className="inspector-input" type="text" autoFocus
        value={value} onFocus={(event) => event.currentTarget.select()}
        onChange={(event) => { setValue(event.target.value); setError(false) }}
        aria-invalid={error}
        aria-describedby={`calendar-go-to-hint${error ? ' calendar-go-to-error' : ''}`}
      />
      <button className="dashboard-range" type="submit">{t('calendar.go')}</button>
      <span id="calendar-go-to-hint" className="settings-hint">
        {t(dates.custom ? 'calendar.goToCustomHint' : 'calendar.goToGregorianHint')}
      </span>
      {error && <span id="calendar-go-to-error" className="calendar-go-to-error" role="alert">
        {t('calendar.invalidDate')}
      </span>}
    </form>
  )
}
