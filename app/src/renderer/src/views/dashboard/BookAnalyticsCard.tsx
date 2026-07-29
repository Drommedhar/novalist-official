import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import './analytics.css'

interface Distribution {
  key: string
  label: string
  sceneCount: number
  wordCount: number
  percent: number
}

interface Presence {
  entityId: string
  label: string
  totalScenes: number
  /** Parallel to the chapter titles. */
  scenesPerChapter: number[]
}

interface BookAnalytics {
  chapterTitles: string[]
  pov: Distribution[]
  acts: Distribution[]
  characters: Presence[]
  locations: Presence[]
  unused: string[]
}

/** Rows shown before the list is cut off, so a cast of forty does not fill the
 *  Dashboard with a name nobody is looking for. */
const TOP_N = 12

/**
 * Where things sit across the whole book.
 *
 * Novalist computed POV and mentions per scene and only ever showed them for
 * the scene in view, so "which character is this book actually about" and "have
 * I forgotten this location since chapter two" had no answer anywhere.
 */
export function BookAnalyticsCard(): React.JSX.Element | null {
  const { t } = useTranslation()
  const [data, setData] = useState<BookAnalytics | null>(null)

  useEffect(() => {
    // Reads every scene file, so it is asked for once when the Dashboard opens
    // rather than kept live.
    void rpc
      .request<BookAnalytics>('analytics/book')
      .then(setData)
      .catch(() => setData(null))
  }, [])

  if (!data || data.chapterTitles.length === 0) return null

  const heat = (count: number, busiest: number): string =>
    count === 0 ? '0' : String(Math.max(0.25, count / Math.max(1, busiest)))

  const presenceGrid = (rows: Presence[], title: string): React.JSX.Element | null => {
    if (rows.length === 0) return null
    const busiest = Math.max(...rows.flatMap((r) => r.scenesPerChapter), 1)
    return (
      <>
        <div className="dashboard-card-title">{title}</div>
        <div className="analytics-grid">
          {rows.slice(0, TOP_N).map((row) => (
            <div key={row.entityId} className="analytics-row">
              <span className="analytics-label" title={row.label}>
                {row.label}
              </span>
              <span className="analytics-cells">
                {row.scenesPerChapter.map((count, index) => (
                  <span
                    key={index}
                    className="analytics-cell"
                    style={{ opacity: heat(count, busiest) }}
                    title={`${data.chapterTitles[index]}: ${count}`}
                  />
                ))}
              </span>
              <span className="analytics-total">{row.totalScenes}</span>
            </div>
          ))}
        </div>
      </>
    )
  }

  const bars = (rows: Distribution[], title: string): React.JSX.Element | null => {
    if (rows.length === 0) return null
    return (
      <>
        <div className="dashboard-card-title">{title}</div>
        {rows.slice(0, TOP_N).map((row) => (
          <div key={row.key || 'unset'} className="dashboard-status-row">
            <span className="dashboard-status-name">{row.label || t('analytics.unset')}</span>
            <div className="dashboard-bar-track dashboard-status-track">
              <div className="dashboard-bar-fill" style={{ width: `${row.percent}%` }} />
            </div>
            <span className="dashboard-status-count">
              {row.percent}% - {row.sceneCount}
            </span>
          </div>
        ))}
      </>
    )
  }

  return (
    <div className="dashboard-card">
      {bars(data.pov, t('analytics.povTitle'))}
      {bars(data.acts, t('analytics.actTitle'))}
      {presenceGrid(data.characters, t('analytics.charactersTitle'))}
      {presenceGrid(data.locations, t('analytics.locationsTitle'))}

      {/* The most useful thing here: something planned and then quietly
          dropped is invisible until something counts. */}
      {data.unused.length > 0 && (
        <>
          <div className="dashboard-card-title">{t('analytics.unusedTitle')}</div>
          <div className="settings-hint">
            {t('analytics.unusedHint', { names: data.unused.join(', ') })}
          </div>
        </>
      )}
    </div>
  )
}
