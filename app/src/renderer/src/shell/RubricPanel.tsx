import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'

interface Element {
  key: string
  group: string
  name: string
  question: string
  advice: string
}

interface SceneScores {
  scores: { elementKey: string; score: number }[]
  answered: number
  weak: number
  average: number
}

const SCALE = [1, 2, 3, 4, 5]

/** At or below this, the advice is worth reading. Matches the backend. */
const WEAK_AT_OR_BELOW = 2

/**
 * A named rubric for reading the open scene, with advice bound to each element.
 *
 * The per-scene analysis Novalist already had is descriptive - point of view,
 * emotion, intensity - which says what a scene is and never whether it works. A
 * writer looking at a weak scene got numbers and no next move, and a judgement
 * with no next move is marking rather than teaching.
 *
 * So the advice only appears where it is needed: under an element the writer
 * has just scored low. Showing all twelve pieces of advice at once would be a
 * craft book, and nobody reads a craft book in an inspector.
 */
export function RubricPanel({
  chapterGuid,
  sceneId
}: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [elements, setElements] = useState<Element[]>([])
  const [scene, setScene] = useState<SceneScores | null>(null)

  useEffect(() => {
    void rpc
      .request<Element[]>('rubric/elements')
      .then(setElements)
      .catch(() => setElements([]))
  }, [])

  useEffect(() => {
    void rpc
      .request<SceneScores>('rubric/scene', [chapterGuid, sceneId])
      .then(setScene)
      .catch(() => setScene(null))
  }, [chapterGuid, sceneId])

  const scoreOf = (key: string): number =>
    scene?.scores.find((s) => s.elementKey === key)?.score ?? 0

  const set = async (key: string, score: number): Promise<void> => {
    setScene(
      await rpc.request<SceneScores>('rubric/setScore', [chapterGuid, sceneId, key, score])
    )
  }

  if (elements.length === 0) return <></>

  return (
    <div className="rubric">
      {/* The summary above already names the panel; repeating it here read as
          a heading printed twice. */}
      <p className="inspector-meta">{t('rubric.intro')}</p>

      {elements.map((element) => {
        const score = scoreOf(element.key)
        return (
          <div key={element.key} className="rubric-element">
            <span
              className="rubric-name"
              title={t(`rubric.element.${element.key}.question`, {
                defaultValue: element.question
              })}
            >
              {t(`rubric.element.${element.key}.name`, { defaultValue: element.name })}
            </span>
            <div className="rubric-scale">
              {SCALE.map((value) => (
                <button
                  key={value}
                  className={`rubric-dot${score === value ? ' picked' : ''}`}
                  aria-label={`${t(`rubric.element.${element.key}.name`, {
                    defaultValue: element.name
                  })}: ${value}`}
                  // Clicking the score it already has clears it back to "not
                  // asked here", so a wrong tap is one tap to undo.
                  onClick={() => void set(element.key, score === value ? 0 : value)}
                >
                  {value}
                </button>
              ))}
            </div>
            {/* A chase scene is not failing at interiority, it is not trying. */}
            {score === 0 && <span className="rubric-unasked">{t('rubric.notAsked')}</span>}
            {score > 0 && score <= WEAK_AT_OR_BELOW && (
              <p className="rubric-advice">
                {t(`rubric.element.${element.key}.advice`, { defaultValue: element.advice })}
              </p>
            )}
          </div>
        )
      })}

      {scene && scene.answered > 0 && (
        <p className="inspector-meta">
          {t('rubric.summary', {
            answered: scene.answered,
            total: elements.length,
            weak: scene.weak
          })}
        </p>
      )}
    </div>
  )
}
