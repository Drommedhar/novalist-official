import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight, X } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useBookScope, useProjectStore } from '../stores/projectStore'
import { useStageStore } from '../stores/stageStore'
import { useIsPhone } from './useIsPhone'
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
  const bookScope = useBookScope()
  const stages = useStageStore((s) => s.stages)
  const isPhone = useIsPhone()
  /** Phone only: whether the bar is unfolded. */
  const [open, setOpen] = useState(false)

  // Something narrowing the view is not allowed to sit behind a closed row: if
  // a filter is set (restored from a preset, say), the bar shows itself.
  const narrowed = !isEmptyFilter(filter)
  useEffect(() => {
    if (narrowed) setOpen(true)
  }, [narrowed])
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
    // Characters, places, stages and plotlines are all the active book's, so
    // this follows the book rather than only the project.
  }, [bookScope])

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

  // On a phone five pickers and a preset field are most of the screen, on top of
  // whatever toolbar the view has, so the bar folds to a single row. It says how
  // many filters are on, because a folded bar that is quietly narrowing the view
  // is worse than no bar - and it opens itself when something is active, so a
  // filter can never be hiding behind a closed row.
  if (isPhone && !open) {
    return (
      <button
        type="button"
        className="filter-bar-toggle"
        aria-expanded={false}
        onClick={() => setOpen(true)}
      >
        <ChevronRight size={15} strokeWidth={2} />
        {isEmptyFilter(filter)
          ? t('filters.title')
          : t('filters.clear', { count: activeCount(filter) })}
      </button>
    )
  }

  return (
    <div className="filter-bar">
      {isPhone && (
        <button
          type="button"
          className="filter-bar-toggle"
          aria-expanded
          onClick={() => setOpen(false)}
        >
          <ChevronDown size={15} strokeWidth={2} />
          {t('filters.title')}
        </button>
      )}
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
