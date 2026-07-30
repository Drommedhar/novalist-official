import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { MapScale } from './mapModel'

/**
 * What one world unit on this map is worth on the ground.
 *
 * The map has always had world-space units and a zoom readout and no scale at
 * all, so "how many days' ride to the coast" could not be answered from a map
 * the app itself drew. One number is enough to answer it, to draw a scale bar,
 * and to make the ruler mean something.
 *
 * The unit is free text on purpose: a world does not have to measure in
 * kilometres, and a drop-down of Earth units would be an odd thing to put in
 * front of somebody drawing a second one.
 */
export function MapScaleDialog({
  initial,
  onSubmit,
  onCancel
}: {
  initial: MapScale | null
  onSubmit(scale: MapScale | null): void
  onCancel(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [unitsPer, setUnitsPer] = useState(String(initial?.unitsPer ?? ''))
  const [unit, setUnit] = useState(initial?.unit ?? 'km')
  const [gridSpacing, setGridSpacing] = useState(String(initial?.gridSpacing || ''))

  const submit = (): void => {
    const per = Number(unitsPer)
    // A scale of zero is not a scale. Clearing it puts the map back to having
    // no opinion, which is better than a wrong one.
    if (!(per > 0)) {
      onSubmit(null)
      return
    }
    onSubmit({
      unitsPer: per,
      unit: unit.trim(),
      gridSpacing: Math.max(0, Number(gridSpacing) || 0)
    })
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={t('maps.scale')}>
        <div className="dialog-title">{t('maps.scale')}</div>
        <div className="settings-hint">{t('maps.scaleIntro')}</div>

        <label className="inspector-label" htmlFor="map-scale-per">
          {t('maps.scaleGroundPerUnit')}
        </label>
        <input
          id="map-scale-per"
          className="dialog-input"
          type="number"
          min={0}
          step="any"
          value={unitsPer}
          onChange={(e) => setUnitsPer(e.target.value)}
          autoFocus
        />

        <label className="inspector-label" htmlFor="map-scale-unit">
          {t('maps.scaleUnit')}
        </label>
        <input
          id="map-scale-unit"
          className="dialog-input"
          placeholder={t('maps.scaleUnitPlaceholder')}
          value={unit}
          onChange={(e) => setUnit(e.target.value)}
        />

        <label className="inspector-label" htmlFor="map-scale-grid">
          {t('maps.scaleGrid')}
        </label>
        <input
          id="map-scale-grid"
          className="dialog-input"
          type="number"
          min={0}
          step="any"
          value={gridSpacing}
          onChange={(e) => setGridSpacing(e.target.value)}
        />
        <div className="settings-hint">{t('maps.scaleGridHint')}</div>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={submit}>
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
