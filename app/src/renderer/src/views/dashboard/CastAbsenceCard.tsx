import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

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
  /**
   * The book's shape, as one string: which chapters, in what order, holding how
   * many scenes. Asked again whenever it changes.
   *
   * The card used to ask once and keep the answer for the life of the screen,
   * and a report with nothing in it is drawn as no card at all - so an answer
   * given before the book had finished arriving was indistinguishable from a
   * book where nobody disappears, for the whole session. Opening a project lands
   * on the Dashboard, so the cards ask their questions at the same moment the
   * project is still settling; and with panes the screen can sit open beside the
   * chapter that is being written next to it. Both leave a report that is wrong
   * in the one way nobody can see.
   *
   * The signature rather than the chapter list itself: it is a value, so a state
   * that rebuilt the same book does not send the report round again, and a save
   * that only changed a word count does not either.
   */
  const structure = useProjectStore((s) =>
    s.chapters.map((c) => `${c.guid}.${c.scenes.length}`).join('|')
  )

  useEffect(() => {
    let live = true
    void rpc
      .request<AbsenceRow[]>('analytics/castAbsence')
      .then((next) => live && setRows(next))
      .catch(() => live && setRows([]))
    return () => {
      live = false
    }
  }, [structure])

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
