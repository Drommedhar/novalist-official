import { useTranslation } from 'react-i18next'
import type { Plotline } from './PlotlineDetailDialog'

interface Column {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  plotlineIds: string[]
}

const LANE_HEIGHT = 34
const COLUMN_WIDTH = 26
const LABEL_WIDTH = 150
const TOP = 26

/**
 * Each thread as a track across the book, rather than a column of ticks.
 *
 * The grid answers "is this thread in this scene" and answers it one cell at a
 * time. What a revision asks is where two threads meet - the scene that carries
 * the romance and the mystery at once is the scene doing structural work, and a
 * matrix of ticks hides it among four hundred other cells.
 *
 * A track runs from a thread's first scene to its last, so a gap in the middle
 * reads as a gap rather than as the end of the thread. The scenes where more
 * than one track is present are marked down the whole height, which is the
 * shape you are actually looking for.
 */
export function PlotLanes({
  plotlines,
  columns,
  onOpenScene
}: {
  plotlines: Plotline[]
  columns: Column[]
  onOpenScene: (chapterGuid: string, sceneId: string) => void
}): React.JSX.Element {
  const { t } = useTranslation()

  const width = LABEL_WIDTH + columns.length * COLUMN_WIDTH
  const height = TOP + plotlines.length * LANE_HEIGHT + 8
  const x = (index: number): number => LABEL_WIDTH + index * COLUMN_WIDTH + COLUMN_WIDTH / 2

  // Scenes carrying more than one thread. These are the ones worth finding.
  const crossings = columns
    .map((column, index) => ({ index, count: column.plotlineIds.length }))
    .filter((c) => c.count > 1)

  return (
    <div className="plotgrid-scroll">
      <svg className="plot-lanes" width={width} height={height} role="img">
        {crossings.map((crossing) => (
          <rect
            key={crossing.index}
            className="plot-lane-crossing"
            x={x(crossing.index) - COLUMN_WIDTH / 2}
            y={TOP - 8}
            width={COLUMN_WIDTH}
            height={height - TOP}
          />
        ))}

        {plotlines.map((plotline, lane) => {
          const present = columns
            .map((column, index) => (column.plotlineIds.includes(plotline.id) ? index : -1))
            .filter((index) => index >= 0)
          const y = TOP + lane * LANE_HEIGHT

          return (
            <g key={plotline.id}>
              <text className="plot-lane-label" x={LABEL_WIDTH - 10} y={y + 4}>
                {plotline.name}
              </text>

              {/* First appearance to last. A thread that stops and returns
                  reads as one thread with a gap, not as two. */}
              {present.length > 1 && (
                <line
                  className="plot-lane-track"
                  x1={x(present[0])}
                  y1={y}
                  x2={x(present[present.length - 1])}
                  y2={y}
                  style={{ stroke: plotline.color }}
                />
              )}

              {present.map((index) => (
                <circle
                  key={index}
                  className="plot-lane-stop"
                  cx={x(index)}
                  cy={y}
                  r={5}
                  style={{ fill: plotline.color }}
                  onClick={() => onOpenScene(columns[index].chapterGuid, columns[index].sceneId)}
                >
                  <title>
                    {`${columns[index].chapterTitle} - ${columns[index].sceneTitle}`}
                  </title>
                </circle>
              ))}

              {present.length === 0 && (
                <text className="plot-lane-empty" x={LABEL_WIDTH + 8} y={y + 4}>
                  {t('plotGrid.laneEmpty')}
                </text>
              )}
            </g>
          )
        })}
      </svg>
    </div>
  )
}
