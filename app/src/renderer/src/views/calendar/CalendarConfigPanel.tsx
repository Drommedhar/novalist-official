import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface CalendarConfig {
  type: string
  yearLabel: string
  monthNames: string[]
  daysPerMonth: number[]
  weekdayNames: string[]
  yearLength: number
}

/** What a brand-new custom calendar starts from, so the editor is never empty. */
const STARTER_MONTHS = ['First Month', 'Second Month', 'Third Month']
const STARTER_DAYS = [30, 30, 30]
const STARTER_WEEKDAYS = ['Firstday', 'Secondday', 'Thirdday', 'Fourthday', 'Fifthday']

/**
 * Editor for the book's in-world calendar.
 *
 * A secondary-world story rarely runs on twelve Gregorian months and a seven-day
 * week, and forcing in-world dates into real ones makes every duration wrong.
 * The parsing and arithmetic already understood a custom calendar; this is the
 * surface that lets a writer define one.
 */
export function CalendarConfigPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [config, setConfig] = useState<CalendarConfig | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void rpc.request<CalendarConfig>('calendar/getConfig').then(setConfig)
  }, [])

  const save = async (next: CalendarConfig): Promise<void> => {
    setConfig(next)
    setBusy(true)
    try {
      setConfig(
        await rpc.request<CalendarConfig>('calendar/setConfig', [
          next.type,
          next.yearLabel,
          next.monthNames,
          next.daysPerMonth,
          next.weekdayNames
        ])
      )
    } finally {
      setBusy(false)
    }
  }

  if (!config) return <p className="settings-hint">{t('calendarConfig.loading')}</p>

  const isCustom = config.type === 'Custom'

  const switchType = (custom: boolean): void => {
    // Switching to custom with nothing defined would leave an unusable
    // calendar, so seed it with something the writer can edit down.
    if (custom && config.monthNames.length === 0) {
      void save({
        ...config,
        type: 'Custom',
        monthNames: [...STARTER_MONTHS],
        daysPerMonth: [...STARTER_DAYS],
        weekdayNames: [...STARTER_WEEKDAYS]
      })
      return
    }
    void save({ ...config, type: custom ? 'Custom' : 'Gregorian' })
  }

  const setMonth = (index: number, name: string, days: number): void => {
    const monthNames = [...config.monthNames]
    const daysPerMonth = [...config.daysPerMonth]
    monthNames[index] = name
    daysPerMonth[index] = days
    void save({ ...config, monthNames, daysPerMonth })
  }

  const addMonth = (): void =>
    void save({
      ...config,
      monthNames: [...config.monthNames, ''],
      daysPerMonth: [...config.daysPerMonth, 30]
    })

  const removeMonth = (index: number): void =>
    void save({
      ...config,
      monthNames: config.monthNames.filter((_, i) => i !== index),
      daysPerMonth: config.daysPerMonth.filter((_, i) => i !== index)
    })

  const setWeekday = (index: number, name: string): void => {
    const weekdayNames = [...config.weekdayNames]
    weekdayNames[index] = name
    void save({ ...config, weekdayNames })
  }

  return (
    <div className="calendar-config">
      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={isCustom}
          disabled={busy}
          onChange={(e) => switchType(e.target.checked)}
        />
        {t('calendarConfig.useCustom')}
      </label>
      <div className="settings-hint">{t('calendarConfig.useCustomDesc')}</div>

      {isCustom && (
        <>
          <label className="inspector-label" htmlFor="calendar-year-label">
            {t('calendarConfig.yearLabel')}
          </label>
          <input
            id="calendar-year-label"
            className="inspector-input"
            value={config.yearLabel}
            placeholder={t('calendarConfig.yearLabelPlaceholder')}
            onChange={(e) => void save({ ...config, yearLabel: e.target.value })}
          />
          <div className="settings-hint">{t('calendarConfig.yearLabelDesc')}</div>

          <h4>{t('calendarConfig.months')}</h4>
          {config.monthNames.map((name, i) => (
            <div key={i} className="calendar-config-row">
              <input
                className="inspector-input"
                value={name}
                placeholder={t('calendarConfig.monthName')}
                onChange={(e) => setMonth(i, e.target.value, config.daysPerMonth[i] ?? 30)}
              />
              <input
                className="inspector-input calendar-config-days"
                type="number"
                min={1}
                value={config.daysPerMonth[i] ?? 30}
                onChange={(e) => setMonth(i, name, Number(e.target.value) || 1)}
              />
              <button className="dialog-button" onClick={() => removeMonth(i)}>
                <Trash2 size={14} />
              </button>
            </div>
          ))}
          <button className="dialog-button" onClick={addMonth}>
            <Plus size={14} /> {t('calendarConfig.addMonth')}
          </button>
          <div className="settings-hint">
            {t('calendarConfig.yearLength', { days: config.yearLength })}
          </div>

          <h4>{t('calendarConfig.weekdays')}</h4>
          {config.weekdayNames.map((name, i) => (
            <div key={i} className="calendar-config-row">
              <input
                className="inspector-input"
                value={name}
                onChange={(e) => setWeekday(i, e.target.value)}
              />
              <button
                className="dialog-button"
                onClick={() =>
                  void save({
                    ...config,
                    weekdayNames: config.weekdayNames.filter((_, j) => j !== i)
                  })
                }
              >
                <Trash2 size={14} />
              </button>
            </div>
          ))}
          <button
            className="dialog-button"
            onClick={() => void save({ ...config, weekdayNames: [...config.weekdayNames, ''] })}
          >
            <Plus size={14} /> {t('calendarConfig.addWeekday')}
          </button>

          <p className="settings-hint">{t('calendarConfig.dateFormatHint')}</p>
        </>
      )}
    </div>
  )
}
