import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore } from '../../stores/manuscriptStore'
import { rpc as rpcClient } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { sceneColour } from './sceneColour'
import { LazyBlock } from '../../shell/LazyBlock'
import { EAGER_SECTIONS } from './ManuscriptFrame'

// Reserved height for a block that has not been built yet, so the scrollbar is
// the right length before anything has been measured.
const CORKBOARD_ROW_HEIGHT = 190

const CORKBOARD_CARDS_PER_ROW = 4

interface CardPlacement {
  sceneId: string
  x: number
  y: number
}

/**
 * The corkboard, either grouped by chapter in reading order or freeform.
 *
 * Reading order is the arrangement the binder already shows. Planning on index
 * cards is about the ones it cannot: three piles for three threads, a row of
 * scenes that happen the same night, an outlier pushed aside because it does
 * not fit yet.
 */
export function Corkboard(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const freeform = useManuscriptStore((s) => s.freeform)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const chapters = useProjectStore((s) => s.chapters)
  const colourBy = useManuscriptStore((s) => s.colourBy)

  /**
   * The colour of whatever label a scene carries, if any. Built once per
   * chapter change rather than walked per card - the flat scan this replaced
   * was quadratic, and a five-hundred-scene book paid for it on every render.
   */
  const labelColors = useMemo(() => {
    const map = new Map<string, string>()
    for (const chapter of chapters)
      for (const scene of chapter.scenes) if (scene.labelColor) map.set(scene.id, scene.labelColor)
    return map
  }, [chapters])
  /** Chapter status and act, so a card can be coloured by either. */
  const chapterOf = useMemo(() => {
    const map = new Map<string, { status: string; act: string }>()
    for (const chapter of chapters)
      for (const scene of chapter.scenes)
        map.set(scene.id, { status: chapter.status, act: chapter.act })
    return map
  }, [chapters])

  /** The band colour for a card, under whichever dimension is selected. */
  const bandColor = (sceneId: string, pov: string | null): string | undefined =>
    sceneColour(colourBy, {
      labelColor: labelColors.get(sceneId),
      pov,
      act: chapterOf.get(sceneId)?.act,
      status: chapterOf.get(sceneId)?.status
    })

  if (freeform) return <FreeformCorkboard bandColor={bandColor} />

  return (
    <div className="corkboard">
      {sections.map((section, index) => (
        <LazyBlock
          key={section.chapterGuid}
          eager={index < EAGER_SECTIONS}
          estimatedHeight={
            CORKBOARD_ROW_HEIGHT *
            Math.max(1, Math.ceil(section.scenes.length / CORKBOARD_CARDS_PER_ROW))
          }
        >
          <div className="corkboard-chapter">{section.chapterTitle}</div>
          <div className="corkboard-cards">
            {section.scenes.map((scene) => (
              <div
                key={scene.sceneId}
                className={`corkboard-card${selectedIds.includes(scene.sceneId) ? ' selected' : ''}`}
                // A label the writer named, drawn as the edge of the card. The
                // colour has been on the model for years with nothing reading it.
                style={
                  bandColor(scene.sceneId, scene.pov)
                    ? { borderLeft: `3px solid ${bandColor(scene.sceneId, scene.pov)}` }
                    : undefined
                }
              >
                {/* The title is the selection handle: the synopsis box under it
                    must stay a plain text field, modifier keys and all. */}
                <button
                  className="corkboard-card-title"
                  onClick={(e) => {
                    if (handleSceneClick(scene.sceneId, e)) return
                    void useProjectStore.getState().openScene(section.chapterGuid, scene.sceneId)
                  }}
                >
                  {scene.title}
                </button>
                <textarea
                  className="corkboard-synopsis"
                  placeholder={t('sceneNotes.synopsisPlaceholder')}
                  defaultValue={scene.synopsis ?? ''}
                  onBlur={(e) =>
                    void setSynopsis(section.chapterGuid, scene.sceneId, e.target.value)
                  }
                />
                <div className="corkboard-card-words">
                  {scene.wordCount.toLocaleString()} {t('shell.words')}
                </div>
              </div>
            ))}
          </div>
        </LazyBlock>
      ))}
    </div>
  )
}

/**
 * Cards the writer places, rather than cards the reading order places.
 *
 * Positions are per scene and persist, so the arrangement is still there next
 * week. A scene that has never been placed takes the slot it would have had in
 * reading order, which is why turning freeform on shows the book as it stands
 * instead of every card stacked in the corner.
 */
function FreeformCorkboard({
  bandColor
}: {
  bandColor: (sceneId: string, pov: string | null) => string | undefined
}): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const [places, setPlaces] = useState<Record<string, { x: number; y: number }>>({})
  const dragging = useRef<{ sceneId: string; dx: number; dy: number } | null>(null)

  const load = async (): Promise<void> => {
    const placements = await rpcClient.request<CardPlacement[]>('corkboard/placements')
    setPlaces(Object.fromEntries(placements.map((p) => [p.sceneId, { x: p.x, y: p.y }])))
  }

  useEffect(() => {
    void load()
  }, [sections])

  const scenes = sections.flatMap((section) =>
    section.scenes.map((scene) => ({ ...scene, chapterGuid: section.chapterGuid }))
  )

  // The board is as tall and wide as the furthest card, plus a card's worth of
  // room to drop the next one into.
  const extent = scenes.reduce(
    (acc, scene) => {
      const place = places[scene.sceneId]
      if (!place) return acc
      return { x: Math.max(acc.x, place.x + 260), y: Math.max(acc.y, place.y + 200) }
    },
    { x: 0, y: 0 }
  )

  const onPointerDown = (sceneId: string, e: React.PointerEvent<HTMLDivElement>): void => {
    // Only a drag on the card body moves it. The synopsis box and the title
    // button are things to use, not handles to pull.
    if ((e.target as HTMLElement).closest('textarea, button')) return
    const place = places[sceneId] ?? { x: 0, y: 0 }
    dragging.current = { sceneId, dx: e.clientX - place.x, dy: e.clientY - place.y }
    e.currentTarget.setPointerCapture(e.pointerId)
  }

  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>): void => {
    const drag = dragging.current
    if (!drag) return
    setPlaces((current) => ({
      ...current,
      [drag.sceneId]: {
        x: Math.max(0, Math.round(e.clientX - drag.dx)),
        y: Math.max(0, Math.round(e.clientY - drag.dy))
      }
    }))
  }

  const onPointerUp = (): void => {
    const drag = dragging.current
    dragging.current = null
    if (!drag) return
    const place = places[drag.sceneId]
    if (!place) return
    // Written on release rather than on every move: a drag across the board is
    // hundreds of positions and only the last one is an answer.
    void rpcClient.request('corkboard/setPosition', [drag.sceneId, place.x, place.y])
  }

  return (
    <div className="corkboard corkboard-freeform">
      <div className="corkboard-freeform-bar">
        <button
          className="dashboard-range"
          onClick={() => {
            void rpcClient
              .request<CardPlacement[]>('corkboard/reset')
              .then((placements) =>
                setPlaces(
                  Object.fromEntries(placements.map((p) => [p.sceneId, { x: p.x, y: p.y }]))
                )
              )
          }}
        >
          {t('corkboard.arrange')}
        </button>
        <span className="settings-hint">{t('corkboard.freeformHint')}</span>
      </div>
      <div
        className="corkboard-surface"
        style={{ width: extent.x || undefined, height: extent.y || undefined }}
      >
        {scenes.map((scene) => {
          const place = places[scene.sceneId]
          if (!place) return null
          return (
            <div
              key={scene.sceneId}
              className={`corkboard-card corkboard-card-placed${
                selectedIds.includes(scene.sceneId) ? ' selected' : ''
              }`}
              style={{
                left: place.x,
                top: place.y,
                ...(bandColor(scene.sceneId, scene.pov)
                  ? { borderLeft: `3px solid ${bandColor(scene.sceneId, scene.pov)}` }
                  : {})
              }}
              onPointerDown={(e) => onPointerDown(scene.sceneId, e)}
              onPointerMove={onPointerMove}
              onPointerUp={onPointerUp}
            >
              <button
                className="corkboard-card-title"
                onClick={(e) => {
                  if (handleSceneClick(scene.sceneId, e)) return
                  void useProjectStore.getState().openScene(scene.chapterGuid, scene.sceneId)
                }}
              >
                {scene.title}
              </button>
              <textarea
                className="corkboard-synopsis"
                placeholder={t('sceneNotes.synopsisPlaceholder')}
                defaultValue={scene.synopsis ?? ''}
                onBlur={(e) => void setSynopsis(scene.chapterGuid, scene.sceneId, e.target.value)}
              />
              <div className="corkboard-card-words">
                {scene.wordCount.toLocaleString()} {t('shell.words')}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
