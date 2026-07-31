import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Dices } from 'lucide-react'
import { rpc } from '../../rpc/client'

/**
 * Names to pick from, for the moment naming stops the work.
 *
 * Naming is the highest-frequency thing that interrupts a draft, and every
 * worldbuilding tool in the field gives a generator away. This one is offline
 * and deterministic: the same set, slider and seed give the same list back, so
 * a name somebody liked and did not write down is not gone.
 */
export function NameSuggestions({ onPick }: { onPick: (name: string) => void }): React.JSX.Element {
  const { t } = useTranslation()
  // Asked for rather than hardcoded: a list copied here would drift from the
  // sets that actually ship, and the picker would offer one that is not there.
  const [sets, setSets] = useState<string[]>([])
  const [set, setSet] = useState('')
  const [obscurity, setObscurity] = useState(50)
  const [names, setNames] = useState<string[]>([])
  const [seed, setSeed] = useState(1)

  useEffect(() => {
    void rpc
      .request<string[]>('names/sets')
      .then((available) => {
        setSets(available)
        setSet((current) => current || available[0] || '')
      })
      .catch(() => setSets([]))
  }, [])

  const roll = (nextSeed: number): void => {
    setSeed(nextSeed)
    void rpc
      .request<string[]>('names/generate', [set, 12, obscurity, nextSeed])
      .then(setNames)
      .catch(() => setNames([]))
  }

  return (
    <details className="name-suggestions">
      <summary>{t('names.title')}</summary>

      <div className="name-suggestions-controls">
        <select
          className="dialog-input"
          aria-label={t('names.set')}
          value={set}
          onChange={(e) => setSet(e.target.value)}
        >
          {sets.map((key) => (
            <option key={key} value={key}>
              {t(`names.set_${key}`)}
            </option>
          ))}
        </select>

        {/* A slider rather than a switch: "unusual but still pronounceable" is
            the setting people actually want. */}
        <label className="name-suggestions-slider">
          <span className="export-field-label">{t('names.obscurity')}</span>
          <input
            type="range"
            min={0}
            max={100}
            value={obscurity}
            onChange={(e) => setObscurity(Number(e.target.value))}
          />
        </label>

        <button
          className="dialog-button"
          disabled={set.length === 0}
          onClick={() => roll(seed + 1)}
        >
          <Dices size={13} strokeWidth={2} />
          {names.length === 0 ? t('names.generate') : t('names.again')}
        </button>
      </div>

      {names.length > 0 && (
        <div className="name-suggestions-list">
          {names.map((name) => (
            <button key={name} className="name-suggestion" onClick={() => onPick(name)}>
              {name}
            </button>
          ))}
        </div>
      )}
    </details>
  )
}
