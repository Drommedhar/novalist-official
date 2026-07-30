import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface ArcPointPlaced {
  id: string
  sceneId: string
  sceneTitle: string
  label: string
  readingIndex: number
  isTurn: boolean
}

interface CharacterArc {
  characterId: string
  name: string
  start: string
  end: string
  want: string
  need: string
  points: ArcPointPlaced[]
}

/**
 * Every character's arc laid against the book.
 *
 * The Codex holds one character's arc at a time, which is the wrong shape for
 * the question this answers: whether the turns are spread through the book or
 * bunched into one chapter.
 */
export function ArcsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [arcs, setArcs] = useState<CharacterArc[]>([])

  useEffect(() => {
    void rpc.request<CharacterArc[]>('arcs/all').then(setArcs).catch(() => setArcs([]))
  }, [])

  // Nothing to show until at least one character has an arc, which is the
  // state every project starts in.
  if (arcs.length === 0) return <></>

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('arc.dashboardTitle')}</div>
      <div className="dashboard-echo-desc">{t('arc.dashboardIntro')}</div>

      {arcs.map((arc) => (
        <div key={arc.characterId} className="arc-row">
          <div className="arc-name">{arc.name}</div>
          {(arc.start || arc.end) && (
            <div className="arc-ends">
              {arc.start} {arc.start && arc.end ? '→' : ''} {arc.end}
            </div>
          )}
          {(arc.want || arc.need) && (
            <div className="arc-ends arc-wantneed">
              {arc.want} {arc.want && arc.need ? '→' : ''} {arc.need}
            </div>
          )}
          <div className="arc-points">
            {arc.points.map((point) => (
              <span
                key={point.id}
                className={`arc-point${point.readingIndex < 0 ? ' unplaced' : ''}${
                  point.isTurn ? ' turn' : ''
                }`}
                title={point.isTurn ? t('arc.turnHint') : point.sceneTitle || t('arc.noScene')}
              >
                {point.label}
              </span>
            ))}
            {arc.points.length === 0 && (
              <span className="settings-hint">{t('arc.noPoints')}</span>
            )}
          </div>
        </div>
      ))}
    </div>
  )
}
