import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useNarrationStore, type DesignedVoice, type SystemVoice } from '../../stores/narrationStore'

/** The voice one character is read in. Empty means uncast, which sends their
 *  lines to the narrator rather than silencing them. */
export function VoicePicker({
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
  const engines = useNarrationStore((s) => s.engines)
  const engineNames = useMemo(
    () => new Map(engines.map((engine) => [engine.engineId, engine.engineName])),
    [engines]
  )
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
              {/* Named with its engine where there is more than one. A voice
                  belongs to the engine that made it and can only ever be spoken
                  by that engine, so which one made it is the difference between
                  a performance and a tone - and until now nothing on screen
                  said which you had. */}
              {engineNames.size > 1
                ? `${voice.displayName} (${engineNames.get(voice.engineId) ?? voice.engineId})`
                : voice.displayName}
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
