import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FEATURE_DESIGN, FEATURE_EMOTION_INFERRED, NARRATOR, useNarrationStore, type VoiceBrief } from '../../stores/narrationStore'

/**
 * The brief, before anything is designed.
 *
 * Editable, and shown first, because it is assembled from Codex fields the
 * writer may never have thought of as describing a voice - and because what
 * they change here still goes through the same emotion filter on the way out.
 * The brief describes the instrument; how a line is felt is decided per line,
 * every time, against that one fixed identity.
 */
export function DesignDialog({ brief }: { brief: VoiceBrief }): React.JSX.Element {
  const { t } = useTranslation()
  const error = useNarrationStore((s) => s.designError)
  const closeBrief = useNarrationStore((s) => s.closeBrief)
  const openBrief = useNarrationStore((s) => s.openBrief)
  const design = useNarrationStore((s) => s.design)
  const designNarrator = useNarrationStore((s) => s.designNarrator)
  const candidate = useNarrationStore((s) => s.candidate)
  const engines = useNarrationStore((s) => s.engines)

  const [text, setText] = useState(brief.description)
  useEffect(() => setText(brief.description), [brief.description])

  // A blank seed requests a fresh voice even when the brief is unchanged.
  const [seed, setSeed] = useState('')
  const candidateSeed = useNarrationStore((s) => s.candidateSeed)

  // Every engine that can design, not the first one that happened to be ready.
  // That was the bug: a writer with a real speech engine and the example tone
  // generator installed got whichever had finished loading, and the example
  // loads instantly - so the voice they designed was a sine wave, and the
  // reading then correctly played it back as one.
  const designers = engines.filter((e) => e.isReady && (e.features & FEATURE_DESIGN) !== 0)
  const [engineId, setEngineId] = useState(designers[0]?.engineId ?? '')
  const chosen = designers.some((e) => e.engineId === engineId)
    ? engineId
    : (designers[0]?.engineId ?? '')

  const withheld = brief.refusal === 'WithheldFromAi'
  const isNarrator = brief.characterId === NARRATOR
  const submit = (): void => {
    const asked = seed.trim().length > 0 ? Number.parseInt(seed.trim(), 10) : null
    const pinned = asked !== null && Number.isFinite(asked) && asked >= 0 ? asked : null
    if (isNarrator) void designNarrator(chosen, text, pinned)
    else void design(chosen, brief.characterId, text, true, pinned)
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
          {/* Which engine makes it. Shown whenever there is a choice, because
              the choice is audible and permanent: the voice is stored as the
              audio that engine produced, and only that engine can speak in it
              afterwards. Left to whichever engine loaded first, a writer with
              a real speech engine and the example tone generator installed got
              the tone generator - it loads instantly - and heard their whole
              book as a sine wave. */}
          {designers.length > 1 && (
            <label className="narration-design-field">
              <span>{t('narration.designWith')}</span>
              <select value={chosen} onChange={(e) => setEngineId(e.target.value)}>
                {designers.map((engine) => (
                  <option key={engine.engineId} value={engine.engineId}>
                    {engine.engineName}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label className="narration-design-field">
            <span>{t('narration.briefLabel')}</span>
            <textarea value={text} rows={4} onChange={(e) => setText(e.target.value)} />
          </label>
          <p className="narration-design-note">{t('narration.briefIsTheInstrument')}</p>

          <label className="narration-design-field">
            <span>{t('narration.seed')}</span>
            <input
              type="text"
              inputMode="numeric"
              value={seed}
              placeholder={t('narration.seedRandom')}
              onChange={(e) => setSeed(e.target.value)}
            />
          </label>
          <p className="narration-design-note">{t('narration.seedNote')}</p>

          {brief.sampleLines.length > 0 && (
            <ul className="narration-design-samples">
              {brief.sampleLines.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          )}

          {error !== null && <p className="narration-design-error">{error}</p>}

          {/* What it was actually drawn with. A voice heard once and not kept is
              otherwise gone - design is not reproducible, and nothing else
              remembers the draw. */}
          {candidate !== null && candidateSeed !== null && (
            <p className="narration-design-note">
              {t('narration.seedWas', { seed: candidateSeed })}
              <button
                type="button"
                className="narration-clear"
                onClick={() => setSeed(String(candidateSeed))}
              >
                {t('narration.seedKeepIt')}
              </button>
            </p>
          )}

          <NarrationAudition brief={brief} />

          {/* Heard before it is anybody's. Designing the same description
              twice gives two different voices and one of them may not be the
              voice that was asked for, so the first result is offered rather
              than imposed. */}
          {candidate !== null && (
            <div className="narration-candidate">
              <p className="narration-design-note">{t('narration.listenBeforeKeeping')}</p>
              {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
              <audio controls autoPlay src={`novalist-audio://clip/${candidate}`} />
            </div>
          )}

          <NarrationDesignButtons submit={submit} />
        </>
      )}
    </div>
  )
}

function NarrationAudition({ brief }: { brief: VoiceBrief }): React.JSX.Element {
  const { t } = useTranslation()
  const busy = useNarrationStore((s) => s.busy)
  const audition = useNarrationStore((s) => s.audition)
  const designed = useNarrationStore((s) => s.designed)
  const members = useNarrationStore((s) => s.members)
  const narratorVoiceId = useNarrationStore((s) => s.narratorVoiceId)
  const auditionVoice = useNarrationStore((s) => s.auditionVoice)
  const engines = useNarrationStore((s) => s.engines)
  const isNarrator = brief.characterId === NARRATOR
  // The voice this character already has, if it is one that was designed rather
  // than one the operating system shipped. Auditioning is the question "does
  // this casting hold when they are actually feeling something", which cannot
  // be asked before there is a casting - and cannot be answered by a system
  // voice, which has no emotional range to hear.
  const castTo = isNarrator
    ? narratorVoiceId
    : (members.find((m) => m.characterId === brief.characterId)?.voiceId ?? null)
  const kept = designed.find((d) => d.voiceId === castTo) ?? null
  const keptFeatures = engines.find((engine) => engine.engineId === kept?.engineId)?.features ?? 0
  const auditionIsAutomatic = (keptFeatures & FEATURE_EMOTION_INFERRED) !== 0
  // Their own words where there are any. A voice auditioned on a line the
  // character actually speaks tells you something; one auditioned on a stock
  // sentence tells you about the sentence.
  const auditionText = brief.sampleLines[0] ?? brief.name
  return <>
          {kept !== null && (
            <div className="narration-audition-ask">
              <button
                type="button"
                className="narration-clear"
                disabled={busy}
                onClick={() => void auditionVoice(kept.voiceId, auditionText)}
              >
                {busy
                  ? t('narration.auditioning')
                  : t(auditionIsAutomatic ? 'narration.auditionAutomatic' : 'narration.audition')}
              </button>
              <p className="narration-design-note">
                {t(
                  auditionIsAutomatic ? 'narration.auditionAutomaticNote' : 'narration.auditionNote'
                )}
              </p>
            </div>
          )}

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

  </>
}

function NarrationDesignButtons({submit}: {submit(): void}): React.JSX.Element {
  const { t } = useTranslation()
  const busy = useNarrationStore((s) => s.busy)
  const candidate = useNarrationStore((s) => s.candidate)
  const keepVoice = useNarrationStore((s) => s.keepVoice)
  return (<div className="narration-design-actions">
            {candidate !== null && (
              <button
                type="button"
                className="narration-play"
                disabled={busy}
                onClick={() => void keepVoice()}
              >
                {t('narration.keepVoice')}
              </button>
            )}
            <button
              type="button"
              className={candidate === null ? 'narration-play' : 'narration-clear'}
              disabled={busy}
              onClick={submit}
            >
              {busy
                ? t('narration.designing')
                : candidate === null
                  ? t('narration.designVoice')
                  : t('narration.designAgain')}
            </button>
          </div>)
}
