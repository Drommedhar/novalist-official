import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Sliders } from 'lucide-react'
import { useNarrationStore } from '../../stores/narrationStore'
import { MotionPresence } from '../../shell/MotionPresence'

/**
 * A character's standing register - what is added to every line they speak.
 *
 * For somebody who is always more clipped, or warmer, or wearier than the prose
 * bothers to say each time. A note to the actor about the part, rather than a
 * direction on any one line, which is why it lives on the cast rail and not in
 * the panel.
 */
export function RegisterButton({
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

  const set = (dimension: string, value: number): void => setDraft({ ...draft, [dimension]: value })

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
      <MotionPresence>{open && (
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
      )}</MotionPresence>
    </>
  )
}
