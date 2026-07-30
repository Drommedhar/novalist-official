import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface AbsenceRow {
  entityId: string
  label: string
  totalScenes: number
  longestGap: number
  gapStart: string
  gapEnd: string
  firstChapter: string
  lastChapter: string
  chaptersSinceLastSeen: number
}

/**
 * Who dropped out of the book, and for how long.
 *
 * Novalist has counted appearances per chapter for a while and only ever drawn
 * them as a grid, plus "last seen N chapters ago" for one entry at a time in
 * the Inspector. Reading forty rows of a grid to find the character who
 * vanished in act two is exactly the work a report is for.
 */
export function CastAbsenceCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [rows, setRows] = useState<AbsenceRow[]>([])

  useEffect(() => {
    void rpc
      .request<AbsenceRow[]>('analytics/castAbsence')
      .then(setRows)
      .catch(() => setRows([]))
  }, [])

  // A book where nobody disappears has nothing to say here, and an empty card
  // reads as a report that failed rather than one that came back clean.
  if (rows.length === 0) return <></>

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('castAbsence.title')}</div>
      <div className="dashboard-echo-desc">{t('castAbsence.intro')}</div>
      <div className="cast-absence-rows">
        {rows.map((row) => (
          <div key={row.entityId} className="cast-absence-row">
            <span className="cast-absence-name">{row.label}</span>
            <span className="cast-absence-detail">
              {/* Both measures, because they answer different questions: a gap
                  is somebody who came back, and the other is somebody who has
                  not yet. */}
              {row.longestGap > 0 && (
                <span className="cast-absence-gap">
                  {t('castAbsence.gap', {
                    count: row.longestGap,
                    from: row.gapStart,
                    to: row.gapEnd
                  })}
                </span>
              )}
              {row.chaptersSinceLastSeen > 0 && (
                <span className="cast-absence-since">
                  {t('castAbsence.since', {
                    count: row.chaptersSinceLastSeen,
                    chapter: row.lastChapter
                  })}
                </span>
              )}
            </span>
            <span className="cast-absence-scenes">
              {t('castAbsence.scenes', { count: row.totalScenes })}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}
