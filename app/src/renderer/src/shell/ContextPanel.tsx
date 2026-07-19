import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
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

function EntitySection({
  titleKey,
  type,
  cards
}: {
  titleKey: string
  type: string
  cards: EntityCard[]
}): React.JSX.Element | null {
  const { t } = useTranslation()
  if (cards.length === 0) return null
  const open = async (id: string): Promise<void> => {
    useShellStore.getState().setMainView('codex')
    await useCodexStore.getState().setType(type)
    await useCodexStore.getState().select(id)
  }
  return (
    <div className="ctx-section">
      <div className="inspector-label">{t(titleKey)}</div>
      {cards.map((card) => (
        <button key={card.id} className="ctx-card" onClick={() => void open(card.id)}>
          {card.imagePath ? (
            <img className="ctx-card-img" src={`novalist-project://nl/${encodeURI(card.imagePath)}`} alt="" />
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
    </div>
  )
}

/** Scene context/analysis panel: entities present in the scene, cross-chapter
 * mention matrix, and the auto-computed scene analysis (POV editable). */
export function ContextPanel({
  chapterGuid,
  sceneId
}: {
  chapterGuid: string
  sceneId: string
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [ctx, setCtx] = useState<SceneContext | null>(null)

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

  if (!ctx) return null
  const a = ctx.analysis
  const hasAny =
    ctx.characters.length ||
    ctx.locations.length ||
    ctx.items.length ||
    ctx.lore.length ||
    a.wordCount > 0

  if (!hasAny) return null

  return (
    <div className="ctx-panel">
      <EntitySection titleKey="context.characters" type="character" cards={ctx.characters} />

      {ctx.mentionRows.length > 0 && (
        <div className="ctx-section">
          <div className="inspector-label">{t('context.mentionFrequency')}</div>
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
                      title={t(cell.present ? 'context.mentionedTooltip' : 'context.absentTooltip').replace(
                        '{0}',
                        cell.chapterLabel
                      )}
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
        </div>
      )}

      <EntitySection titleKey="context.locations" type="location" cards={ctx.locations} />
      <EntitySection titleKey="context.items" type="item" cards={ctx.items} />
      <EntitySection titleKey="context.lore" type="lore" cards={ctx.lore} />

      <div className="ctx-section">
        <div className="inspector-label">{t('context.sceneAnalysis')}</div>
        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.pov')}</span>
          <select
            className="dialog-input ctx-pov"
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
        </div>
        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.emotion')}</span>
          <span className="ctx-badge">{loc(t, a.emotion, 'emotion')}</span>
        </div>
        <div className="ctx-analysis-row">
          <span className="ctx-analysis-key">{t('context.intensity')}</span>
          <span className="ctx-intensity">
            <span className="ctx-intensity-track">
              <span
                className={`ctx-intensity-fill${a.intensity < 0 ? ' negative' : ''}`}
                style={{ width: `${Math.min(100, Math.abs(a.intensity) * 10)}%` }}
              />
            </span>
            <span className="ctx-intensity-num">{a.intensity > 0 ? `+${a.intensity}` : a.intensity}</span>
          </span>
        </div>
        {a.conflict && (
          <div className="ctx-analysis-block">
            <span className="ctx-analysis-key">{t('context.conflict')}</span>
            <p className="ctx-conflict">{a.conflict}</p>
          </div>
        )}
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
      </div>
    </div>
  )
}
