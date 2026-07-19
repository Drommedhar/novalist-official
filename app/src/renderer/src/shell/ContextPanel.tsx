import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight, RotateCcw } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useShellStore } from '../stores/shellStore'
import { useCodexStore } from '../stores/codexStore'

interface EntityCard {
  id: string
  name: string
  detail: string
  secondary: string | null
  imagePath: string | null
}

interface MentionCell {
  chapterLabel: string
  present: boolean
  current: boolean
}

interface MentionRow {
  name: string
  cells: MentionCell[]
  lastSeenChaptersAgo: number
}

interface SceneAnalysis {
  pov: string
  povOptions: string[]
  emotion: string
  emotionKeys: string[]
  intensity: number
  conflict: string
  tags: string[]
  dialoguePercent: number
  avgSentenceLength: number
  wordCount: number
}

interface SceneContext {
  characters: EntityCard[]
  locations: EntityCard[]
  items: EntityCard[]
  lore: EntityCard[]
  mentionRows: MentionRow[]
  analysis: SceneAnalysis
}

/** A single set of section-collapse preferences (not scene-specific), persisted
 * to localStorage. Mirrors the desktop ProjectSettings.ViewState.Context* flags,
 * but without adding a settings facade. */
const SECTION_STORAGE_KEY = 'nl.context.collapsed'

function readCollapsed(): Record<string, boolean> {
  try {
    return JSON.parse(localStorage.getItem(SECTION_STORAGE_KEY) || '{}') as Record<string, boolean>
  } catch {
    return {}
  }
}

/** Localizes a backend key ("tense", "sceneTag.dialogue", "pov.firstPerson"),
 * falling back to the raw value when it is a plain name. */
function loc(t: (k: string) => string, value: string, prefix?: string): string {
  if (!value) return value
  if (value.includes('.')) {
    const translated = t(value)
    return translated === value ? value.split('.').pop()! : translated
  }
  if (prefix) {
    const translated = t(`${prefix}.${value}`)
    if (translated !== `${prefix}.${value}`) return translated
  }
  return value
}

function CollapsibleSection({
  titleKey,
  sectionKey,
  collapsed,
  onToggle,
  children
}: {
  titleKey: string
  sectionKey: string
  collapsed: boolean
  onToggle(key: string): void
  children: React.ReactNode
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="ctx-section">
      <button className="ctx-section-head" onClick={() => onToggle(sectionKey)}>
        {collapsed ? (
          <ChevronRight className="ctx-section-chevron" size={12} strokeWidth={2} />
        ) : (
          <ChevronDown className="ctx-section-chevron" size={12} strokeWidth={2} />
        )}
        <span className="ctx-section-title">{t(titleKey)}</span>
      </button>
      {!collapsed && children}
    </div>
  )
}

function EntitySection({
  titleKey,
  sectionKey,
  type,
  cards,
  collapsed,
  onToggle
}: {
  titleKey: string
  sectionKey: string
  type: string
  cards: EntityCard[]
  collapsed: boolean
  onToggle(key: string): void
}): React.JSX.Element | null {
  if (cards.length === 0) return null
  const open = async (id: string): Promise<void> => {
    useShellStore.getState().setMainView('codex')
    await useCodexStore.getState().setType(type)
    await useCodexStore.getState().select(id)
  }
  return (
    <CollapsibleSection
      titleKey={titleKey}
      sectionKey={sectionKey}
      collapsed={collapsed}
      onToggle={onToggle}
    >
      {cards.map((card) => (
        <button key={card.id} className="ctx-card" onClick={() => void open(card.id)}>
          {card.imagePath ? (
            <img
              className="ctx-card-img"
              src={`novalist-project://nl/${encodeURI(card.imagePath)}`}
              alt=""
            />
          ) : (
            <span className="ctx-card-img ctx-card-img-empty" aria-hidden="true">
              {card.name.slice(0, 1).toUpperCase()}
            </span>
          )}
          <span className="ctx-card-text">
            <span className="ctx-card-name">{card.name}</span>
            {card.detail && <span className="ctx-card-detail">{card.detail}</span>}
            {card.secondary && <span className="ctx-card-detail">{card.secondary}</span>}
          </span>
        </button>
      ))}
    </CollapsibleSection>
  )
}

/** Scene context/analysis panel: entities present in the scene, cross-chapter
 * mention matrix, and the auto-computed scene analysis. POV, emotion, intensity,
 * conflict and tags are editable and each carries a reset-to-auto affordance. */
export function ContextPanel({
  chapterGuid,
  sceneId
}: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [ctx, setCtx] = useState<SceneContext | null>(null)
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>(readCollapsed)
  const [conflictDraft, setConflictDraft] = useState('')
  const [tagsDraft, setTagsDraft] = useState('')
  const [intensityDraft, setIntensityDraft] = useState('')

  const analyze = (): void => {
    void rpc
      .request<SceneContext>('context/analyze', [chapterGuid, sceneId])
      .then(setCtx)
      .catch(() => setCtx(null))
  }

  useEffect(() => {
    setCtx(null)
    analyze()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chapterGuid, sceneId])

  // Re-sync the editable drafts whenever the analysis (or language) changes so a
  // reset/override or a scene switch is reflected in the inputs.
  useEffect(() => {
    if (!ctx) return
    const a = ctx.analysis
    setConflictDraft(a.conflict)
    setIntensityDraft(String(a.intensity))
    setTagsDraft(a.tags.map((tag) => loc(t, tag)).join(', '))
  }, [ctx, t])

  const toggleSection = (key: string): void => {
    setCollapsed((prev) => {
      const next = { ...prev, [key]: !prev[key] }
      try {
        localStorage.setItem(SECTION_STORAGE_KEY, JSON.stringify(next))
      } catch {
        /* storage unavailable — collapse still works for the session */
      }
      return next
    })
  }

  const setOverride = (patch: {
    pov?: string
    emotion?: string
    intensity?: number
    conflict?: string
    tags?: string[]
  }): void => {
    void rpc.request('scenes/setAnalysisOverride', [chapterGuid, sceneId, patch]).then(analyze)
  }

  const resetOverride = (field: string): void => {
    void rpc.request('scenes/resetAnalysisOverride', [chapterGuid, sceneId, field]).then(analyze)
  }

  if (!ctx) return null
  const a = ctx.analysis
  const hasAny =
    ctx.characters.length ||
    ctx.locations.length ||
    ctx.items.length ||
    ctx.lore.length ||
    a.wordCount > 0

  if (!hasAny) return null

  const resetButton = (field: string): React.JSX.Element => (
    <button
      className="ctx-reset"
      title={t('context.resetOverride')}
      aria-label={t('context.resetOverride')}
      onClick={() => resetOverride(field)}
    >
      <RotateCcw size={12} strokeWidth={2} />
    </button>
  )

  return (
    <div className="ctx-panel">
      <EntitySection
        titleKey="context.characters"
        sectionKey="characters"
        type="character"
        cards={ctx.characters}
        collapsed={!!collapsed.characters}
        onToggle={toggleSection}
      />

      {ctx.mentionRows.length > 0 && (
        <CollapsibleSection
          titleKey="context.mentionFrequency"
          sectionKey="mentions"
          collapsed={!!collapsed.mentions}
          onToggle={toggleSection}
        >
          <div className="ctx-matrix">
            {ctx.mentionRows.map((row) => (
              <div key={row.name} className="ctx-matrix-row">
                <span className="ctx-matrix-name" title={row.name}>
                  {row.name}
                </span>
                <span className="ctx-matrix-cells">
                  {row.cells.map((cell, i) => (
                    <span
                      key={i}
                      className={`ctx-cell${cell.present ? ' present' : ''}${cell.current ? ' current' : ''}`}
                      title={t(
                        cell.present ? 'context.mentionedTooltip' : 'context.absentTooltip'
                      ).replace('{0}', cell.chapterLabel)}
                    />
                  ))}
                </span>
                {row.lastSeenChaptersAgo >= 3 && (
                  <span className="ctx-lastseen">
                    {t('context.lastSeenGap').replace('{0}', String(row.lastSeenChaptersAgo))}
                  </span>
                )}
              </div>
            ))}
          </div>
        </CollapsibleSection>
      )}

      <EntitySection
        titleKey="context.locations"
        sectionKey="locations"
        type="location"
        cards={ctx.locations}
        collapsed={!!collapsed.locations}
        onToggle={toggleSection}
      />
      <EntitySection
        titleKey="context.items"
        sectionKey="items"
        type="item"
        cards={ctx.items}
        collapsed={!!collapsed.items}
        onToggle={toggleSection}
      />
      <EntitySection
        titleKey="context.lore"
        sectionKey="lore"
        type="lore"
        cards={ctx.lore}
        collapsed={!!collapsed.lore}
        onToggle={toggleSection}
      />

      <CollapsibleSection
        titleKey="context.sceneAnalysis"
        sectionKey="analysis"
        collapsed={!!collapsed.analysis}
        onToggle={toggleSection}
      >
        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.pov')}</span>
          <div className="ctx-analysis-edit">
            <select
              className="dialog-input ctx-analysis-input ctx-pov"
              value={a.pov}
              onChange={(e) => {
                const pov = e.target.value
                void rpc.request('scenes/setPov', [chapterGuid, sceneId, pov]).then(analyze)
              }}
            >
              <option value="">{t('context.none')}</option>
              {a.pov && !a.povOptions.includes(a.pov) && (
                <option value={a.pov}>{loc(t, a.pov)}</option>
              )}
              {a.povOptions.map((p) => (
                <option key={p} value={p}>
                  {loc(t, p)}
                </option>
              ))}
            </select>
            {resetButton('pov')}
          </div>
        </div>

        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.emotion')}</span>
          <div className="ctx-analysis-edit">
            <select
              className="dialog-input ctx-analysis-input"
              aria-label={t('context.selectEmotion')}
              value={a.emotion}
              onChange={(e) => setOverride({ emotion: e.target.value })}
            >
              {a.emotion && !a.emotionKeys.includes(a.emotion) && (
                <option value={a.emotion}>{loc(t, a.emotion, 'emotion')}</option>
              )}
              {a.emotionKeys.map((key) => (
                <option key={key} value={key}>
                  {loc(t, key, 'emotion')}
                </option>
              ))}
            </select>
            {resetButton('emotion')}
          </div>
        </div>

        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.intensity')}</span>
          <span className="ctx-intensity-track">
            <span
              className={`ctx-intensity-fill${a.intensity < 0 ? ' negative' : ''}`}
              style={{ width: `${Math.min(100, Math.abs(a.intensity) * 10)}%` }}
            />
          </span>
          <input
            type="number"
            min={-10}
            max={10}
            className="dialog-input ctx-intensity-input"
            title={t('context.intensityRange')}
            value={intensityDraft}
            onChange={(e) => setIntensityDraft(e.target.value)}
            onBlur={() => {
              const v = Number(intensityDraft)
              if (intensityDraft.trim() === '' || Number.isNaN(v)) {
                setIntensityDraft(String(a.intensity))
                return
              }
              const clamped = Math.max(-10, Math.min(10, Math.round(v)))
              if (clamped !== a.intensity) setOverride({ intensity: clamped })
              else setIntensityDraft(String(clamped))
            }}
          />
          {resetButton('intensity')}
        </div>

        <div className="ctx-analysis-block">
          <span className="ctx-analysis-key">{t('context.conflict')}</span>
          <div className="ctx-analysis-edit">
            <input
              className="dialog-input ctx-analysis-input"
              placeholder={t('context.conflict')}
              value={conflictDraft}
              onChange={(e) => setConflictDraft(e.target.value)}
              onBlur={() => {
                if (conflictDraft !== a.conflict) setOverride({ conflict: conflictDraft })
              }}
            />
            {resetButton('conflict')}
          </div>
        </div>

        <div className="ctx-analysis-block">
          <span className="ctx-analysis-key">{t('context.tags')}</span>
          <div className="ctx-analysis-edit">
            <input
              className="dialog-input ctx-analysis-input"
              placeholder={t('context.commaSeparatedTags')}
              value={tagsDraft}
              onChange={(e) => setTagsDraft(e.target.value)}
              onBlur={() => {
                const parsed = tagsDraft
                  .split(',')
                  .map((s) => s.trim())
                  .filter(Boolean)
                const base = a.tags.map((tag) => loc(t, tag))
                if (parsed.join('|') !== base.join('|')) setOverride({ tags: parsed })
              }}
            />
            {resetButton('tags')}
          </div>
        </div>

        {a.tags.length > 0 && (
          <div className="ctx-tags">
            {a.tags.map((tag) => (
              <span key={tag} className="entity-chip">
                {loc(t, tag)}
              </span>
            ))}
          </div>
        )}

        <div className="ctx-stats">
          {t('context.wordsDisplay').replace('{0:N0}', a.wordCount.toLocaleString())} ·{' '}
          {t('context.dialogueDisplay').replace('{0}', String(a.dialoguePercent))} ·{' '}
          {t('context.avgSentenceDisplay').replace('{0}', String(Math.round(a.avgSentenceLength)))}
        </div>
      </CollapsibleSection>
    </div>
  )
}
