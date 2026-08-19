import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Sliders } from 'lucide-react'
import { useNarrationStore } from '../../stores/narrationStore'
import type { SegmentRef } from '../../stores/narrationStore'

/**
 * The eight sliders, for the moment the sixteen names do not cover.
 *
 * The names are the fast path and stay the default — most lines are angry or
 * sorrowful or nothing at all, and picking a word is quicker than pushing eight
 * numbers. This is what is behind them: a delivery with no name, set exactly,
 * and applied either to this line or to the whole run the writer has in mind.
 *
 * Values are what will actually be performed, standing register included, so
 * what the screen says and what the ear will hear are the same thing.
 */
export function DirectionEditor({
  refs,
  vector,
  referenceClip,
  emotionKey,
  voiceId,
  onClose
}: {
  /** The line, or the run of lines, this applies to. */
  refs: SegmentRef[]
  vector: Record<string, number>
  referenceClip: string | null
  emotionKey: string
  /** Whose voice reads this line. What can be pointed at is bounded by it. */
  voiceId: string | null
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const dimensions = useNarrationStore((s) => s.dimensions)
  const setDirections = useNarrationStore((s) => s.setDirections)
  const reading = useNarrationStore((s) => s.reading)
  const clips = useNarrationStore((s) => s.heard)
  const [draft, setDraft] = useState<Record<string, number>>(vector)
  const [clip, setClip] = useState<string | null>(referenceClip)
  const [saving, setSaving] = useState(false)

  // Re-open on whatever the writer has picked now, rather than on whatever was
  // picked when this first mounted.
  useEffect(() => {
    setDraft(vector)
    setClip(referenceClip)
  }, [vector, referenceClip])

  /** The engine's ceiling: everything at once is a request for nothing. */
  const total = useMemo(
    () => Object.values(draft).reduce((sum, value) => sum + Math.max(0, value), 0),
    [draft]
  )
  const over = total > 1.5

  /**
   * Lines already rendered this session, in this voice, as things to point at.
   *
   * "Like that" only means anything about a delivery the writer has actually
   * heard, so the list is what has been performed rather than every line in the
   * book.
   *
   * And only this character's own lines. An engine that can take a clip as a
   * direction takes its whole delivery from it — the timbre with the prosody —
   * so pointing one character at another's line would not read it their way, it
   * would read it in their voice. The narrower list is also the honest one:
   * "say it like he said it" is not a thing a director can ask for.
   */
  const heard = useMemo(
    () =>
      reading
        .map((step) => ({ step, clip: clips[step.segment.key] }))
        .filter((row) => row.clip !== undefined && row.step.segment.voiceId === voiceId),
    [reading, clips, voiceId]
  )

  const apply = async (): Promise<void> => {
    setSaving(true)
    try {
      await setDirections(refs, emotionKey, draft, clip)
      onClose()
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="narration-direction-editor">
      <div className="narration-direction-head">
        <Sliders size={14} aria-hidden="true" />
        <span>
          {refs.length > 1
            ? t('narration.directLines', { count: refs.length })
            : t('narration.directLine')}
        </span>
      </div>

      <div className="narration-sliders">
        {dimensions.map((dimension) => (
          <label key={dimension} className="narration-slider">
            <span className="narration-slider-name">
              {t(`narration.dimension.${dimension}`, dimension)}
            </span>
            <input
              type="range"
              min={0}
              max={1}
              step={0.05}
              value={draft[dimension] ?? 0}
              onChange={(e) =>
                setDraft({ ...draft, [dimension]: Number.parseFloat(e.target.value) })
              }
            />
            <span className="narration-slider-value">
              {(draft[dimension] ?? 0).toFixed(2)}
            </span>
          </label>
        ))}
      </div>

      {/* Said rather than silently rescaled: the writer set these, and moving
          them without saying so would be the screen disagreeing with the ear. */}
      {over && <p className="narration-chip warn">{t('narration.directionOver')}</p>}

      {heard.length > 0 && (
        <label className="narration-panel-field">
          <span>{t('narration.likeThat')}</span>
          <select value={clip ?? ''} onChange={(e) => setClip(e.target.value || null)}>
            <option value="">{t('narration.likeThatNone')}</option>
            {heard.map((row) => (
              <option key={row.step.segment.key} value={row.clip}>
                {row.step.segment.text.slice(0, 60)}
              </option>
            ))}
          </select>
        </label>
      )}

      <div className="narration-direction-actions">
        <button type="button" disabled={saving} onClick={() => void apply()}>
          {t('narration.applyDirection')}
        </button>
        <button type="button" className="narration-clear" onClick={onClose}>
          {t('dialog.cancel')}
        </button>
      </div>
    </div>
  )
}
