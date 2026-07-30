import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useStageStore } from '../stores/stageStore'
import {
  activeCount,
  isEmptyFilter,
  useFilterStore,
  type ProjectFilter
} from '../stores/filterStore'

interface Named {
  id: string
  name: string
}

/**
 * The one filter every narrowing view shares, and the presets for it.
 *
 * The Manuscript status filter, the Timeline's character and location filters
 * and the Plot Grid's own were three unrelated pieces of local state: narrowing
 * to one character meant setting it again in each view, and it was gone the
 * moment you navigated away.
 */
export function FilterBar(): React.JSX.Element {
  const { t } = useTranslation()
  const filter = useFilterStore((s) => s.filter)
  const presets = useFilterStore((s) => s.presets)
  const projectPath = useProjectStore((s) => s.projectPath)
  const stages = useStageStore((s) => s.stages)
  const [characters, setCharacters] = useState<Named[]>([])
  const [locations, setLocations] = useState<Named[]>([])
  const [plotlines, setPlotlines] = useState<Named[]>([])
  const [presetName, setPresetName] = useState('')

  useEffect(() => {
    useFilterStore.getState().loadPresets(projectPath)
    if (!projectPath) return
    void useStageStore.getState().load()
    void rpc
      .request<Named[]>('entities/list', ['character'])
      .then(setCharacters)
      .catch(() => setCharacters([]))
    void rpc
      .request<Named[]>('entities/list', ['location'])
      .then(setLocations)
      .catch(() => setLocations([]))
    void rpc
      .request<Named[]>('binder/plotlines')
      .then(setPlotlines)
      .catch(() => setPlotlines([]))
  }, [projectPath])

  const set = (patch: Partial<ProjectFilter>): void => useFilterStore.getState().set(patch)

  const chip = (
    key: keyof ProjectFilter,
    labelKey: string,
    options: { id: string; name: string }[]
  ): React.JSX.Element => (
    <label className="filter-chip">
      <span className="filter-chip-label">{t(labelKey)}</span>
      <select
        className="inspector-input"
        aria-label={t(labelKey)}
        value={filter[key]}
        onChange={(e) => set({ [key]: e.target.value } as Partial<ProjectFilter>)}
      >
        <option value="">{t('filters.any')}</option>
        {options.map((o) => (
          <option key={o.id} value={o.id}>
            {o.name}
          </option>
        ))}
      </select>
    </label>
  )

  return (
    <div className="filter-bar">
      {chip(
        'status',
        'filters.status',
        ['Outline', 'FirstDraft', 'Revised', 'Edited', 'Final'].map((id) => ({
          id,
          name: t(`dashboard.status${id}`)
        }))
      )}
      {characters.length > 0 && chip('character', 'filters.character', characters)}
      {locations.length > 0 && chip('location', 'filters.location', locations)}
      {plotlines.length > 0 && chip('plotline', 'filters.plotline', plotlines)}
      {stages.length > 0 &&
        chip(
          'stage',
          'filters.stage',
          stages.map((s) => ({ id: s.key, name: s.label }))
        )}

      {/* Only once something is narrowed: a Clear that clears nothing is a
          control that teaches the writer it does nothing. */}
      {!isEmptyFilter(filter) && (
        <button
          className="btn-secondary"
          onClick={() => useFilterStore.getState().clear()}
          title={t('filters.clear')}
        >
          <X size={14} strokeWidth={2} />
          {t('filters.clear', { count: activeCount(filter) })}
        </button>
      )}

      <div className="filter-presets">
        <input
          className="inspector-input"
          placeholder={t('filters.namePlaceholder')}
          value={presetName}
          onChange={(e) => setPresetName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key !== 'Enter') return
            useFilterStore.getState().save(presetName)
            setPresetName('')
          }}
        />
        {presets.map((preset) => (
          <span key={preset.name} className="filter-preset">
            <button
              className="filter-preset-apply"
              onClick={() => useFilterStore.getState().apply(preset.name)}
            >
              {preset.name}
            </button>
            <button
              className="binder-expand"
              aria-label={t('filters.removePreset')}
              title={t('filters.removePreset')}
              onClick={() => useFilterStore.getState().remove(preset.name)}
            >
              &times;
            </button>
          </span>
        ))}
      </div>
    </div>
  )
}
