import { useMemo, useRef, useState } from 'react'
import { useContentTransition } from '../../shell/useContentTransition'
import { MotionPresence } from '../../shell/MotionPresence'
import { useTranslation } from 'react-i18next'
import { FEATURE_EMOTION_INFERRED, useNarrationStore, type NarrationSegment, type ReadingStep, type SegmentRef } from '../../stores/narrationStore'
import { DirectionEditor } from './DirectionEditor'

/* Explicit maps rather than an interpolated key, so every string this view can
   show is a literal the locale checker can find. */
const CONFIDENCE_KEYS: Record<NarrationSegment['confidence'], string> = {
  Manual: 'dialogue.confidence.manual',
  High: 'dialogue.confidence.high',
  Inferred: 'dialogue.confidence.inferred',
  Medium: 'dialogue.confidence.medium',
  Low: 'dialogue.confidence.low',
  None: 'dialogue.confidence.none'
}

const SOURCE_KEYS: Record<NarrationSegment['directionSource'], string> = {
  Writer: 'narration.source.writer',
  Verb: 'narration.source.verb',
  Scene: 'narration.source.scene',
  None: 'narration.source.none'
}

/**
 * The line the writer picked: who reads it, how, and the two corrections.
 *
 * A panel rather than controls in the margin. Dropdowns beside every line would
 * turn the prose back into a form, which is the thing this view exists not to
 * be — you read until something is wrong, and then you fix that one.
 */
function useSegmentEditing(step: ReadingStep) {
  const setSpeaker = useNarrationStore((s) => s.setSpeaker)
  const setDirection = useNarrationStore((s) => s.setDirection)
  const select = useNarrationStore((s) => s.select)
  const emotions = useNarrationStore((s) => s.emotions)
  const members = useNarrationStore((s) => s.members)
  const reading = useNarrationStore((s) => s.reading)
  const engines = useNarrationStore((s) => s.engines)
  const designed = useNarrationStore((s) => s.designed)
  const [editing, setEditing] = useState(false)
  /**
   * How many lines after this one the direction applies to.
   *
   * A whole argument, a whole eulogy: one performance, set once. Counted
   * forward from this line within the same scene, because a run that crosses a
   * scene break is not what anybody means by "this argument".
   */
  const [run, setRun] = useState(1)

  const segment = step.segment
  const narration = segment.kind === 'Narration'
  const owner = designed.find((voice) => voice.voiceId === segment.voiceId)?.engineId
  const inferred =
    (engines.find((engine) => engine.engineId === owner)?.features ?? 0) & FEATURE_EMOTION_INFERRED
  const ref: SegmentRef = {
    chapterGuid: step.chapterGuid,
    sceneId: step.sceneId,
    key: segment.key,
    lineKey: segment.lineKey
  }

  const candidates = useMemo(
    () => segment.candidates.filter((c) => c.characterId !== segment.speakerId),
    [segment.candidates, segment.speakerId]
  )

  /** This line and the ones after it in the same scene, as far as the run goes. */
  const runRefs = useMemo(() => {
    const at = reading.findIndex(
      (s) =>
        s.chapterGuid === step.chapterGuid &&
        s.sceneId === step.sceneId &&
        s.segment.key === segment.key
    )
    if (at < 0) return [ref]
    return reading
      .slice(at, at + run)
      .filter((s) => s.chapterGuid === step.chapterGuid && s.sceneId === step.sceneId)
      .map((s) => ({
        chapterGuid: s.chapterGuid,
        sceneId: s.sceneId,
        key: s.segment.key,
        lineKey: s.segment.lineKey
      }))
    // The ref is stable for a given segment, and rebuilding the list on every
    // render of the prose frame would reset the editor mid-edit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reading, run, step.chapterGuid, step.sceneId, segment.key])

  /** How many lines are actually left in this scene, so the control cannot
   *  offer a run that runs off the end of it. */
  const runMax = useMemo(() => {
    const at = reading.findIndex(
      (s) =>
        s.chapterGuid === step.chapterGuid &&
        s.sceneId === step.sceneId &&
        s.segment.key === segment.key
    )
    if (at < 0) return 1
    let count = 0
    for (let i = at; i < reading.length; i++) {
      if (reading[i].chapterGuid !== step.chapterGuid || reading[i].sceneId !== step.sceneId) break
      count++
    }
    return count
  }, [reading, step.chapterGuid, step.sceneId, segment.key])

  return { setSpeaker, setDirection, select, emotions, members, editing, setEditing, run, setRun, segment, narration, inferred, ref, candidates, runRefs, runMax }
}

export function SegmentPanel({ step }: { step: ReadingStep }): React.JSX.Element {
  const { t } = useTranslation()
  const { setSpeaker, setDirection, select, emotions, members, editing, setEditing, run, setRun, segment, narration, inferred, ref, candidates, runRefs, runMax } = useSegmentEditing(step)
  const panelRef = useRef<HTMLDivElement>(null)
  useContentTransition(panelRef, `${step.chapterGuid}:${step.sceneId}:${segment.key}`)

  return (
    <div className="narration-panel" ref={panelRef}>
      <div className="narration-panel-head">
        <span className="narration-panel-where">
          {step.chapterTitle}
          {step.sceneTitle ? ` · ${step.sceneTitle}` : ''}
        </span>
        <button
          type="button"
          className="narration-panel-close"
          aria-label={t('dialog.close')}
          onClick={() => select(null)}
        >
          ×
        </button>
      </div>

      <p className="narration-panel-text">{segment.text}</p>

      <div className="narration-panel-controls">
        <label className="narration-panel-field">
          <span>{t('narration.readBy')}</span>
          {narration ? (
            <span className="narration-panel-static">{t('narration.narrator')}</span>
          ) : (
            <select
              value={segment.speakerId ?? ''}
              onChange={(e) => void setSpeaker(ref, e.target.value || null)}
            >
              <option value="">{t('narration.unknownSpeaker')}</option>
              {members.map((member) => (
                <option key={member.characterId} value={member.characterId}>
                  {member.name}
                </option>
              ))}
            </select>
          )}
          {!narration && (
            <span className={`narration-chip confidence ${segment.confidence.toLowerCase()}`}>
              {t(CONFIDENCE_KEYS[segment.confidence])}
            </span>
          )}
        </label>

        {inferred !== 0 ? (
          <label className="narration-panel-field">
            <span>{t('narration.directionFor')}</span>
            <span className="narration-panel-static">{t('narration.automaticDelivery')}</span>
          </label>
        ) : (
          <label className="narration-panel-field">
            <span>{t('narration.directionFor')}</span>
            <select
              value={segment.directionKey}
              onChange={(e) => void setDirection(ref, e.target.value)}
            >
              {emotions.map((key) => (
                <option key={key} value={key}>
                  {t(`emotion.${key}`, key)}
                </option>
              ))}
            </select>
            <span className={`narration-chip source ${segment.directionSource.toLowerCase()}`}>
              {segment.directionSource === 'Verb' && segment.directionEvidence
                ? t('narration.source.verbWith', { verb: segment.directionEvidence })
                : t(SOURCE_KEYS[segment.directionSource])}
            </span>
            {segment.directionSource === 'Writer' && (
              <button
                type="button"
                className="narration-clear"
                onClick={() => void setDirection(ref, null)}
              >
                {t('narration.clearDirection')}
              </button>
            )}
            {/* Behind the names, for the delivery none of them is. */}
            <button
              type="button"
              className="narration-clear"
              onClick={() => setEditing((was) => !was)}
            >
              {t('narration.byHand')}
            </button>
          </label>
        )}

        <MotionPresence>{inferred === 0 && editing && runMax > 1 && (
          <label className="narration-panel-field">
            <span>{t('narration.applyTo')}</span>
            <input
              type="number"
              min={1}
              max={runMax}
              value={run}
              onChange={(e) =>
                setRun(Math.max(1, Math.min(runMax, Number.parseInt(e.target.value, 10) || 1)))
              }
            />
            <span className="narration-panel-static">
              {t('narration.linesOfScene', { count: runMax })}
            </span>
          </label>
        )}</MotionPresence>

        <MotionPresence collapse>{inferred === 0 && editing && (
          <DirectionEditor
            refs={runRefs}
            vector={segment.directionVector}
            referenceClip={segment.directionClip}
            emotionKey={segment.directionKey}
            voiceId={segment.voiceId}
            onClose={() => setEditing(false)}
          />
        )}</MotionPresence>

        {!segment.voiceId && <span className="narration-chip warn">{t('narration.noVoice')}</span>}
      </div>

      {!narration && candidates.length > 0 && (
        <div className="narration-candidates">
          <span className="narration-candidates-label">{t('dialogue.mightBe')}</span>
          {candidates.map((candidate) => (
            <button
              key={candidate.characterId}
              type="button"
              className="narration-candidate"
              onClick={() => void setSpeaker(ref, candidate.characterId)}
            >
              {candidate.name} {candidate.percent}%
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
