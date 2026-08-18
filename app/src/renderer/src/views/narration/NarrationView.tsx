import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Crosshair, Play, Sliders, Sparkles, Square, Trash2 } from 'lucide-react'
import {
  FEATURE_DESIGN,
  NARRATOR,
  useNarrationStore,
  type DesignedVoice,
  type NarrationSegment,
  type ReadingStep,
  type SegmentRef,
  type SystemVoice
} from '../../stores/narrationStore'
import { DirectionEditor } from './DirectionEditor'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { autoColour } from '../manuscript/sceneColour'
import './narration.css'

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

/** The frame's side of the bridge. Same shape as the manuscript frame's. */
interface NarrationWindow extends Window {
  setBook(json: string): void
  setSpeaking(sceneId: string | null, key: string | null): void
  setSelected(sceneId: string | null, key: string | null, reveal: boolean): void
  revealScene(sceneId: string): void
  setTheme(
    bg: string,
    fg: string,
    accent: string,
    subtle: string,
    divider: string,
    scrollbarThumb?: string,
    scrollbarThumbHover?: string,
    scrollbarThumbActive?: string
  ): void
  setFont(family: string, size: number): void
  setReadingComfort(lineHeight: number, letterSpacing: number): void
  setLanguage(lang: string): void
}

/**
 * The book as it will be read aloud.
 *
 * The prose is the view. An earlier cut listed the segments as rows and it was
 * unreadable for the thing it is for: a reading is something you follow, and a
 * column of extracted fragments has no paragraphs, no emphasis and no place in
 * the book — so the writer could not tell where they were in their own scene.
 * The whole book is on one strip, marked up where it stands, and scrolling is
 * how you move: the earlier cut followed whatever the editor had open and the
 * only way to change it was the binder, which puts the editor back in the pane.
 */
export function NarrationView(): React.JSX.Element {
  const { t } = useTranslation()
  const openSceneId = useProjectStore((s) => s.openSceneId)

  const book = useNarrationStore((s) => s.book)
  const reading = useNarrationStore((s) => s.reading)
  const members = useNarrationStore((s) => s.members)
  const voices = useNarrationStore((s) => s.voices)
  const narratorVoiceId = useNarrationStore((s) => s.narratorVoiceId)
  const unassignedCount = useNarrationStore((s) => s.unassignedCount)
  const loading = useNarrationStore((s) => s.loading)
  const speaking = useNarrationStore((s) => s.speaking)
  const selected = useNarrationStore((s) => s.selected)
  const rate = useNarrationStore((s) => s.rate)
  const loadCast = useNarrationStore((s) => s.loadCast)
  const loadBook = useNarrationStore((s) => s.loadBook)
  const setVoice = useNarrationStore((s) => s.setVoice)
  const setRate = useNarrationStore((s) => s.setRate)
  const play = useNarrationStore((s) => s.play)
  const stop = useNarrationStore((s) => s.stop)
  const busy = useNarrationStore((s) => s.busy)
  const prepareEngine = useNarrationStore((s) => s.prepareEngine)
  const loadEngines = useNarrationStore((s) => s.loadEngines)
  const engines = useNarrationStore((s) => s.engines)
  const designed = useNarrationStore((s) => s.designed)
  const brief = useNarrationStore((s) => s.brief)

  const frameRef = useRef<HTMLIFrameElement>(null)
  const readyRef = useRef(false)

  useEffect(() => {
    void loadCast()
    void loadBook()
    void loadEngines()
  }, [loadCast, loadBook, loadEngines])

  // Stopping on the way out matters more than it looks: the platform engine
  // keeps speaking after the view is gone, and there would be no control left
  // on screen to stop it with.
  useEffect(() => () => stop(), [stop])

  /** A colour per speaker, so a page of prose says who is talking before you
   *  read a word of it. Hashed, like the corkboard's, so it needs no setup. */
  const colours = useMemo(() => {
    const map: Record<string, string> = {}
    for (const member of members) map[member.characterId] = autoColour(member.characterId)
    return map
  }, [members])

  const pushBook = (): void => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return

    const style = getComputedStyle(document.documentElement)
    const token = (name: string): string => style.getPropertyValue(name).trim()
    frame.setTheme(
      token('--nl-surface-editor'),
      token('--nl-text'),
      token('--nl-accent'),
      token('--nl-text-subtle'),
      token('--nl-border'),
      // Separate document: browser-painted scrollbars need the colours pushed.
      token('--nl-scrollbar-thumb'),
      token('--nl-scrollbar-thumb-hover'),
      token('--nl-scrollbar-thumb-active')
    )
    const effective = useSettingsStore.getState().view?.effective
    if (effective) {
      frame.setFont(effective.editorFontFamily, effective.editorFontSize)
      frame.setReadingComfort(effective.editorLineHeight, effective.editorLetterSpacing)
    }

    frame.setBook(
      JSON.stringify({
        chapters:
          book?.chapters.map((chapter) => ({
            guid: chapter.guid,
            title: chapter.title,
            act: chapter.act,
            scenes: chapter.scenes.map((scene) => ({
              chapterGuid: scene.chapterGuid,
              sceneId: scene.sceneId,
              sceneTitle: scene.sceneTitle,
              html: scene.html,
              segments: scene.segments.map((segment) => ({
                key: segment.key,
                kind: segment.kind,
                speakerId: segment.speakerId,
                // Hovering a line says who reads it and how, without the writer
                // having to select it to find out.
                label:
                  (segment.speakerName ?? t('narration.narrator')) +
                  ' · ' +
                  t(`emotion.${segment.directionKey}`, segment.directionKey)
              }))
            }))
          })) ?? [],
        colours,
        narratorColour: getComputedStyle(document.documentElement)
          .getPropertyValue('--nl-text-subtle')
          .trim(),
        emptyLabel: t('narration.emptyBook')
      })
    )
  }

  // Not gated on the frame being mounted yet. It was, and the listener was
  // therefore never attached: the frame was only in the tree once the book had
  // loaded, so on the first render there was nothing to read a ready message
  // from and the prose never arrived.
  useEffect(() => {
    const onMessage = (event: MessageEvent): void => {
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return
      const raw = (event.data as { novalistNarration?: string })?.novalistNarration
      if (typeof raw !== 'string') return
      let message: { type: string; [key: string]: unknown }
      try {
        message = JSON.parse(raw)
      } catch {
        return
      }
      if (message.type === 'ready') {
        readyRef.current = true
        pushBook()
      } else if (message.type === 'segmentClicked') {
        useNarrationStore.getState().select({
          chapterGuid: String(message.chapterGuid),
          sceneId: String(message.sceneId),
          key: String(message.key)
        })
      }
    }

    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(pushBook, [book, colours])

  useEffect(() => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return
    frame.setSpeaking(speaking?.sceneId ?? null, speaking?.key ?? null)
  }, [speaking])

  useEffect(() => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return
    frame.setSelected(selected?.sceneId ?? null, selected?.key ?? null, false)
  }, [selected])

  const cast = members.filter((m) => m.voiceId !== null).length
  const canPlay = reading.some((step) => step.segment.voiceId)
  const noVoices = voices.length === 0 && designed.length === 0
  // The first engine that is installed, ready, and able to design. Everything
  // about designing stays hidden until there is one, rather than being offered
  // and then failing when pressed.
  const designer = engines.find((e) => e.isReady && (e.features & FEATURE_DESIGN) !== 0) ?? null
  const selectedStep = useMemo(
    () =>
      selected
        ? (reading.find(
            (step) => step.sceneId === selected.sceneId && step.segment.key === selected.key
          ) ?? null)
        : null,
    [reading, selected]
  )

  const playFrom = (): void => {
    if (!selected) {
      void play(0)
      return
    }
    const at = reading.findIndex(
      (step) => step.sceneId === selected.sceneId && step.segment.key === selected.key
    )
    void play(at < 0 ? 0 : at)
  }

  return (
    <div className="narration-view">
      <aside className="narration-cast" aria-label={t('narration.cast')}>
        <div className="narration-cast-title">{t('shell.view.narration')}</div>
        <div className="narration-cast-head">
          {t('narration.castCount', { cast, total: members.length })}
        </div>

        <ul className="narration-cast-list">
          <li className="narration-cast-row narrator">
            <span className="narration-cast-swatch narration-cast-swatch-narrator" />
            <span className="narration-cast-name">{t('narration.narrator')}</span>
            <VoicePicker
              voices={voices}
              designed={designed}
              value={narratorVoiceId}
              label={t('narration.voiceFor', { name: t('narration.narrator') })}
              onChange={(id) => void setVoice(null, id)}
            />
            {designer !== null && <NarratorDesignActions voiceId={narratorVoiceId} />}
            <RegisterButton characterId={NARRATOR} name={t('narration.narrator')} />
          </li>
          {members.map((member) => (
            <li key={member.characterId} className="narration-cast-row">
              <span
                className="narration-cast-swatch"
                style={{ background: colours[member.characterId] }}
              />
              <span className="narration-cast-name" title={member.name}>
                {member.name}
              </span>
              <span className="narration-cast-count">{member.lineCount}</span>
              <VoicePicker
                voices={voices}
                designed={designed}
                value={member.voiceId}
                label={t('narration.voiceFor', { name: member.name })}
                onChange={(id) => void setVoice(member.characterId, id)}
              />
              {designer !== null && (
                <DesignActions characterId={member.characterId} voiceId={member.voiceId} />
              )}
              <RegisterButton characterId={member.characterId} name={member.name} />
            </li>
          ))}
        </ul>

        {unassignedCount > 0 && (
          <div className="narration-cast-note">
            {t('narration.unassigned', { count: unassignedCount })}
          </div>
        )}
        {noVoices && <div className="narration-cast-note warn">{t('narration.noVoices')}</div>}

        {/* Every engine, not the first one. Showing only one meant a writer
            who had installed a second could not see it, let alone prepare it -
            and the one they were shown was whichever happened to load first. */}
        {engines.map((engine) => (
          <div key={engine.engineId} className="narration-engine">
            <span className="narration-engine-name">{engine.engineName}</span>
            <span className="narration-engine-state">
              {/* An engine that reports an empty reason has not given one:
                  treating "" as a message printed nothing where the state
                  should have been. */}
              {engine.isReady
                ? engine.detail.length > 0
                  ? engine.detail
                  : t('narration.engineReady')
                : engine.error !== null && engine.error.trim().length > 0
                  ? engine.error
                  : t('narration.engineNotReady')}
            </span>
            {!engine.isReady && (
              <button
                type="button"
                className="narration-prepare"
                disabled={busy}
                onClick={() => void prepareEngine(engine.engineId)}
              >
                {engine.downloadBytes !== null && engine.downloadBytes > 0
                  ? t('narration.prepareWithSize', {
                      size: Math.round(engine.downloadBytes / (1024 * 1024 * 1024))
                    })
                  : t('narration.prepare')}
              </button>
            )}
          </div>
        ))}
      </aside>

      <section className="narration-stage" aria-label={t('narration.script')}>
        <iframe
          ref={frameRef}
          className="narration-frame"
          src="./editor/narration-editor.html"
          title={t('narration.script')}
          sandbox="allow-scripts allow-same-origin"
        />
        {loading && !book && <div className="narration-status">{t('narration.loading')}</div>}

        {selectedStep && <SegmentPanel step={selectedStep} />}

        {brief !== null && designer !== null && <DesignDialog engineId={designer.engineId} />}

        <footer className="narration-transport">
          {/* One button, and it says what it does. The platform engine speaks a
              passage whole, so there is nothing to resume from: stopping ends
              the reading, and starting again starts where you are. */}
          <button
            type="button"
            className="narration-play"
            disabled={!canPlay}
            onClick={() => (speaking === null ? playFrom() : stop())}
          >
            {speaking === null ? (
              <Play size={13} strokeWidth={1.75} aria-hidden="true" />
            ) : (
              <Square size={13} strokeWidth={1.75} aria-hidden="true" />
            )}
            {speaking === null
              ? selected
                ? t('narration.playFromHere')
                : t('narration.play')
              : t('narration.stop')}
          </button>

          <button
            type="button"
            className="narration-locate"
            disabled={!openSceneId}
            onClick={() => {
              const frame = frameRef.current?.contentWindow as NarrationWindow | null
              if (frame && openSceneId) frame.revealScene(openSceneId)
            }}
          >
            <Crosshair size={13} strokeWidth={1.75} aria-hidden="true" />
            {t('narration.goToOpenScene')}
          </button>

          <label className="narration-rate">
            {t('narration.speed')}
            <input
              type="range"
              min={0.5}
              max={2}
              step={0.1}
              value={rate}
              onChange={(e) => setRate(Number(e.target.value))}
            />
            <span className="narration-rate-value">{rate.toFixed(1)}&times;</span>
          </label>

          {!canPlay && reading.length > 0 && (
            <span className="narration-transport-note">{t('narration.nothingCast')}</span>
          )}
          {reading.length === 0 && !loading && (
            <span className="narration-transport-note">{t('narration.emptyBook')}</span>
          )}
        </footer>
      </section>
    </div>
  )
}

/**
 * The line the writer picked: who reads it, how, and the two corrections.
 *
 * A panel rather than controls in the margin. Dropdowns beside every line would
 * turn the prose back into a form, which is the thing this view exists not to
 * be — you read until something is wrong, and then you fix that one.
 */
function SegmentPanel({ step }: { step: ReadingStep }): React.JSX.Element {
  const { t } = useTranslation()
  const setSpeaker = useNarrationStore((s) => s.setSpeaker)
  const setDirection = useNarrationStore((s) => s.setDirection)
  const select = useNarrationStore((s) => s.select)
  const emotions = useNarrationStore((s) => s.emotions)
  const members = useNarrationStore((s) => s.members)
  const reading = useNarrationStore((s) => s.reading)
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
  const ref: SegmentRef = {
    chapterGuid: step.chapterGuid,
    sceneId: step.sceneId,
    key: segment.key
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
      .map((s) => ({ chapterGuid: s.chapterGuid, sceneId: s.sceneId, key: s.segment.key }))
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

  return (
    <div className="narration-panel">
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

        {editing && runMax > 1 && (
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
        )}

        {editing && (
          <DirectionEditor
            refs={runRefs}
            vector={segment.directionVector}
            referenceClip={segment.directionClip}
            emotionKey={segment.directionKey}
            onClose={() => setEditing(false)}
          />
        )}

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

/** The voice one character is read in. Empty means uncast, which sends their
 *  lines to the narrator rather than silencing them. */
function VoicePicker({
  voices,
  designed,
  value,
  label,
  onChange
}: {
  voices: SystemVoice[]
  designed: DesignedVoice[]
  value: string | null
  label: string
  onChange: (voiceId: string | null) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  // A voice this book names but this machine does not have still has to be
  // pickable, or opening somebody else's project would silently un-cast the
  // whole book the first time a picker rendered.
  const missing =
    value !== null &&
    value.length > 0 &&
    !designed.some((d) => d.voiceId === value) &&
    !voices.some((v) => v.id === value)

  return (
    <select
      className="narration-voice"
      aria-label={label}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value || null)}
    >
      <option value="">{t('narration.uncast')}</option>
      {missing && <option value={value}>{t('narration.voiceElsewhere')}</option>}
      {designed.length > 0 && (
        <optgroup label={t('narration.designedVoices')}>
          {designed.map((voice) => (
            <option key={voice.voiceId} value={voice.voiceId}>
              {voice.displayName}
            </option>
          ))}
        </optgroup>
      )}
      {voices.length > 0 && (
        <optgroup label={t('narration.systemVoices')}>
          {voices.map((voice) => (
            <option key={voice.id} value={voice.id}>
              {voice.name}
            </option>
          ))}
        </optgroup>
      )}
    </select>
  )
}

/** The narrator's own design button. Separate from a character's because the
 *  narrator has no Codex entry: their brief comes from the book. */
function NarratorDesignActions({ voiceId }: { voiceId: string | null }): React.JSX.Element {
  const { t } = useTranslation()
  const designed = useNarrationStore((s) => s.designed)
  const busy = useNarrationStore((s) => s.busy)
  const openNarratorBrief = useNarrationStore((s) => s.openNarratorBrief)
  const forgetVoice = useNarrationStore((s) => s.forgetVoice)

  const own = designed.find((d) => d.voiceId === voiceId) ?? null

  return (
    <span className="narration-design-actions">
      <button
        type="button"
        className="narration-design"
        disabled={busy}
        onClick={() => void openNarratorBrief()}
      >
        <Sparkles size={12} strokeWidth={1.75} aria-hidden="true" />
        {own === null ? t('narration.designVoice') : t('narration.redesignVoice')}
      </button>
      {own !== null && (
        <button
          type="button"
          className="narration-forget"
          aria-label={t('narration.forgetVoice')}
          title={t('narration.forgetVoice')}
          disabled={busy}
          onClick={() => void forgetVoice(own.voiceId)}
        >
          <Trash2 size={12} strokeWidth={1.75} aria-hidden="true" />
        </button>
      )}
    </span>
  )
}

/** Design, re-design and forget, on the character's own row. */
function DesignActions({
  characterId,
  voiceId
}: {
  characterId: string
  voiceId: string | null
}): React.JSX.Element {
  const { t } = useTranslation()
  const designed = useNarrationStore((s) => s.designed)
  const busy = useNarrationStore((s) => s.busy)
  const openBrief = useNarrationStore((s) => s.openBrief)
  const forgetVoice = useNarrationStore((s) => s.forgetVoice)

  const own = designed.find((d) => d.voiceId === voiceId) ?? null

  return (
    <span className="narration-design-actions">
      <button
        type="button"
        className="narration-design"
        disabled={busy}
        onClick={() => void openBrief(characterId)}
      >
        <Sparkles size={12} strokeWidth={1.75} aria-hidden="true" />
        {own === null ? t('narration.designVoice') : t('narration.redesignVoice')}
      </button>
      {own !== null && (
        <button
          type="button"
          className="narration-forget"
          aria-label={t('narration.forgetVoice')}
          title={t('narration.forgetVoice')}
          disabled={busy}
          onClick={() => void forgetVoice(own.voiceId)}
        >
          <Trash2 size={12} strokeWidth={1.75} aria-hidden="true" />
        </button>
      )}
    </span>
  )
}

/**
 * The brief, before anything is designed.
 *
 * Editable, and shown first, because it is assembled from Codex fields the
 * writer may never have thought of as describing a voice - and because what
 * they change here still goes through the same emotion filter on the way out.
 * The brief describes the instrument; how a line is felt is decided per line,
 * every time, against that one fixed identity.
 */
function DesignDialog({ engineId }: { engineId: string }): React.JSX.Element {
  const { t } = useTranslation()
  const brief = useNarrationStore((s) => s.brief)
  const busy = useNarrationStore((s) => s.busy)
  const error = useNarrationStore((s) => s.designError)
  const audition = useNarrationStore((s) => s.audition)
  const closeBrief = useNarrationStore((s) => s.closeBrief)
  const openBrief = useNarrationStore((s) => s.openBrief)
  const design = useNarrationStore((s) => s.design)
  const designNarrator = useNarrationStore((s) => s.designNarrator)

  const [text, setText] = useState(brief?.description ?? '')
  useEffect(() => setText(brief?.description ?? ''), [brief?.description])

  if (brief === null) return <></>
  const withheld = brief.refusal === 'WithheldFromAi'
  const isNarrator = brief.characterId === NARRATOR
  const submit = (): void => {
    if (isNarrator) void designNarrator(engineId, text)
    else void design(engineId, brief.characterId, text, true)
  }

  return (
    <div className="narration-design-dialog" role="dialog" aria-label={t('narration.designVoice')}>
      <div className="narration-panel-head">
        <span className="narration-panel-where">
          {isNarrator
            ? t('narration.designForNarrator')
            : t('narration.designFor', { name: brief.name })}
        </span>
        <button
          type="button"
          className="narration-panel-close"
          aria-label={t('dialog.close')}
          onClick={closeBrief}
        >
          &times;
        </button>
      </div>

      {withheld ? (
        <>
          <p className="narration-design-note">{t('narration.withheldFromAi')}</p>
          <button
            type="button"
            className="narration-design"
            onClick={() => void openBrief(brief.characterId, true)}
          >
            {t('narration.designAnyway')}
          </button>
        </>
      ) : (
        <>
          <label className="narration-design-field">
            <span>{t('narration.briefLabel')}</span>
            <textarea value={text} rows={4} onChange={(e) => setText(e.target.value)} />
          </label>
          <p className="narration-design-note">{t('narration.briefIsTheInstrument')}</p>

          {brief.sampleLines.length > 0 && (
            <ul className="narration-design-samples">
              {brief.sampleLines.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          )}

          {error !== null && <p className="narration-design-error">{error}</p>}

          {audition.length > 0 && (
            <div className="narration-audition">
              {audition.map((clip) => (
                <audio
                  key={clip.key}
                  controls
                  aria-label={t('emotion.' + clip.key, clip.key)}
                  src={'data:audio/' + clip.audioFormat + ';base64,' + clip.audio}
                />
              ))}
            </div>
          )}

          <button
            type="button"
            className="narration-play"
            disabled={busy}
            onClick={submit}
          >
            {busy ? t('narration.designing') : t('narration.designVoice')}
          </button>
        </>
      )}
    </div>
  )
}


/**
 * A character's standing register - what is added to every line they speak.
 *
 * For somebody who is always more clipped, or warmer, or wearier than the prose
 * bothers to say each time. A note to the actor about the part, rather than a
 * direction on any one line, which is why it lives on the cast rail and not in
 * the panel.
 */
function RegisterButton({
  characterId,
  name
}: {
  characterId: string
  name: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const dimensions = useNarrationStore((s) => s.dimensions)
  const registers = useNarrationStore((s) => s.registers)
  const setRegister = useNarrationStore((s) => s.setRegister)
  const [open, setOpen] = useState(false)
  const standing = registers[characterId] ?? {}
  const [draft, setDraft] = useState<Record<string, number>>(standing)

  const set = (dimension: string, value: number): void =>
    setDraft({ ...draft, [dimension]: value })

  return (
    <>
      <button
        type="button"
        className={`narration-register${Object.keys(standing).length > 0 ? ' set' : ''}`}
        title={t('narration.registerFor', { name })}
        aria-label={t('narration.registerFor', { name })}
        onClick={() => {
          setDraft(registers[characterId] ?? {})
          setOpen((was) => !was)
        }}
      >
        <Sliders size={13} aria-hidden="true" />
      </button>
      {open && (
        <div className="narration-register-editor">
          <p className="narration-register-blurb">{t('narration.registerBlurb')}</p>
          {dimensions.map((dimension) => (
            <label key={dimension} className="narration-slider">
              <span className="narration-slider-name">
                {t(`narration.dimension.${dimension}`, dimension)}
              </span>
              {/* Runs below zero as well as above: a character who is flatter
                  than the prose says needs the emotion taken away, not added. */}
              <input
                type="range"
                min={-0.5}
                max={0.5}
                step={0.05}
                value={draft[dimension] ?? 0}
                onChange={(e) => set(dimension, Number.parseFloat(e.target.value))}
              />
              <span className="narration-slider-value">{(draft[dimension] ?? 0).toFixed(2)}</span>
            </label>
          ))}
          <div className="narration-direction-actions">
            <button
              type="button"
              onClick={() => {
                void setRegister(characterId, draft)
                setOpen(false)
              }}
            >
              {t('narration.applyDirection')}
            </button>
            <button
              type="button"
              className="narration-clear"
              onClick={() => {
                void setRegister(characterId, null)
                setOpen(false)
              }}
            >
              {t('narration.clearRegister')}
            </button>
          </div>
        </div>
      )}
    </>
  )
}
