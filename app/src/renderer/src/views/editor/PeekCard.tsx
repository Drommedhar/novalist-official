import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { EyeOff } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useShellStore } from '../../stores/shellStore'
import { rpc } from '../../rpc/client'
import './editor.css'
import { type PeekPill, type EntityPeek, type PeekScope } from './peekTypes'

/**
 * Marker per finding kind, mirroring the desktop card.
 *
 * Punctuation, not pictographs: the warning sign that used to stand for an
 * inconsistency is an emoji, whatever the old comment here claimed.
 */
const FINDING_MARKER: Record<string, string> = {
  reference: '→',
  inconsistency: '!',
  suggestion: '•'
}

const BUILTIN_TYPES = new Set(['character', 'location', 'item', 'lore'])

/** Localizes a pill label — literal text, or a "{0}" template + arg. */
function pillText(pill: PeekPill, t: TFunction): string {
  if (pill.text != null) return pill.text
  if (pill.labelKey) return t(pill.labelKey).replace('{0}', pill.arg ?? '')
  return ''
}

/**
 * The full focus-peek card, a faithful port of the desktop FocusPeekCardView:
 * header (title, type badge, open/pin/close), framed image with a switcher,
 * ordered attribute pills, relationships (with in-place peek-navigate), character
 * appearance, custom properties, description, map-pin deep links, a section
 * dropdown, and the AI-focus stub. It owns its own navigation target so clicking
 * a relationship re-renders the card in place for that entity.
 */
export function PeekCard({
  target,
  scope,
  onOpen,
  onClose,
  pinned,
  onTogglePin
}: {
  target: { entityType: string; entityId: string }
  scope: PeekScope
  onOpen: (type: string, id: string) => void
  onClose: () => void
  pinned: boolean
  onTogglePin: () => void
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [nav, setNav] = useState(target)
  const [data, setData] = useState<EntityPeek | null>(null)
  const [imageIndex, setImageIndex] = useState(0)
  const [sectionIndex, setSectionIndex] = useState(0)
  // What the entry is like at this point in the story, for the types that are
  // not characters - characters resolve their own richer overrides server-side.
  const [state, setState] = useState<{
    description: string | null
    note: string | null
    scopeLabel: string
    isOverridden: boolean
  } | null>(null)

  // A fresh hover resets in-place navigation, so a card opened on one entity
  // does not come back showing whichever relationship was last followed out of
  // it.
  //
  // Keyed on which entity it is, never on the object saying so. Both callers
  // build that object inline, so it is a new one on every render of the pane
  // the card hangs off - and treating that as a new entity threw the loaded
  // card away and asked for it again. The card blinked empty in between, and an
  // empty card measures nothing, which put the next placement a card's width
  // from the right one. That is the flicker.
  const targetType = target.entityType
  const targetId = target.entityId
  useEffect(() => {
    setNav((current) =>
      current.entityType === targetType && current.entityId === targetId
        ? current
        : { entityType: targetType, entityId: targetId }
    )
  }, [targetType, targetId])

  useEffect(() => {
    let alive = true
    setData(null)
    setImageIndex(0)
    setSectionIndex(0)
    void rpc
      .request<EntityPeek>('entities/peek', [
        nav.entityType,
        nav.entityId,
        scope.chapterGuid,
        scope.chapterTitle,
        scope.sceneTitle
      ])
      .then((peek) => {
        if (alive) setData(peek)
      })
      .catch(() => {
        // Peek fetch is best-effort; a failed load simply shows nothing.
      })
    void rpc
      .request<{
        description: string | null
        note: string | null
        scopeLabel: string
        isOverridden: boolean
      }>('entities/resolveState', [
        nav.entityType,
        nav.entityId,
        null,
        scope.chapterGuid,
        scope.chapterTitle,
        scope.sceneTitle
      ])
      .then((resolved) => {
        if (alive) setState(resolved.isOverridden ? resolved : null)
      })
      .catch(() => {
        // Best-effort: an entry with no restatements simply reads as itself.
      })

    return () => {
      alive = false
    }
    // Depend on the scope primitives (not the object identity, which changes each
    // render) so the peek refetches only when the entity or the open scope changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nav, scope.chapterGuid, scope.chapterTitle, scope.sceneTitle])

  if (!data) return null

  const typeLabel = BUILTIN_TYPES.has(data.typeKey)
    ? t(`focusPeek.type${data.typeKey.charAt(0).toUpperCase()}${data.typeKey.slice(1)}`)
    : data.customTypeLabel ?? data.typeKey
  const image = data.images[imageIndex] ?? data.images[0]
  const section = data.sections[sectionIndex] ?? data.sections[0]

  const navigateTo = (targetType: string, targetId: string): void => {
    setNav({ entityType: targetType, entityId: targetId })
  }

  /** Stops detecting this entry in the open scene. Reversible from the entry's
   *  Codex panel, which is where the whole ignore list is shown. */
  const ignoreHere = async (): Promise<void> => {
    const sceneId = scope.sceneId
    if (!sceneId) return
    const current = await rpc.request<{
      caseSensitive: boolean
      matchPlurals: boolean
      exclusions: string[]
      ignoredSceneIds: string[]
    }>('entities/getMatchSettings', [data.typeKey, data.id])
    if (current.ignoredSceneIds.includes(sceneId)) return
    await rpc.request('entities/setMatchSettings', [
      data.typeKey,
      data.id,
      current.caseSensitive,
      current.matchPlurals,
      current.exclusions,
      [...current.ignoredSceneIds, sceneId]
    ])
    onClose()
  }

  return (
    <div className="peek-card" onClick={(e) => e.stopPropagation()}>
      <div className="peek-header">
        <span className="peek-title" title={data.title}>
          {data.title}
        </span>
        <span className="peek-badge" style={{ background: data.badgeColor }}>
          {typeLabel}
        </span>
        <div className="peek-actions">
          <button
            className="peek-action"
            title={t('focusPeek.openEntity')}
            onClick={() => onOpen(data.typeKey, data.id)}
          >
            ↗
          </button>
          <button
            className="peek-action"
            title={pinned ? t('focusPeek.unpin') : t('focusPeek.pin')}
            onClick={onTogglePin}
          >
            {pinned ? '●' : '○'}
          </button>
          {scope.sceneId && (
            <button
              className="peek-action"
              title={t('match.ignoreHere')}
              onClick={() => void ignoreHere()}
            >
              <EyeOff size={13} />
            </button>
          )}
          <button className="peek-action" title={t('focusPeek.close')} onClick={onClose}>
            ✕
          </button>
        </div>
      </div>

      {data.scopeLabel && (
        <div className="peek-scope" title={t('focusPeek.overrideScopeHint')}>
          {t('focusPeek.overrideScope').replace('{0}', data.scopeLabel)}
        </div>
      )}

      {/* Read as it is here rather than as it is in general - a city razed in
          act two should not describe itself as standing. */}
      {state && (
        <div className="peek-scope" title={t('stateOverride.title')}>
          {t('focusPeek.overrideScope').replace('{0}', state.scopeLabel)}
          {state.description && <div className="peek-state-text">{state.description}</div>}
          {state.note && <div className="settings-hint">{state.note}</div>}
        </div>
      )}

      <div className="peek-scroll">
        <div className="peek-body">
          {image && (
            <div className="peek-image-area">
              {data.images.length > 1 && (
                <select
                  className="peek-select"
                  value={imageIndex}
                  onChange={(e) => setImageIndex(Number(e.target.value))}
                >
                  {data.images.map((img, i) => (
                    <option key={i} value={i}>
                      {img.name || `#${i + 1}`}
                    </option>
                  ))}
                </select>
              )}
              <div className="peek-image-frame">
                <img src={`novalist-project://nl/${encodeURI(image.url)}`} alt="" />
              </div>
            </div>
          )}

          <div className="peek-details">
            {data.pills.length > 0 && (
              <div className="peek-pills">
                {data.pills.map((pill, i) => (
                  <span
                    key={i}
                    className="peek-pill"
                    style={{ background: pill.color, opacity: pill.dim ? 0.75 : 1 }}
                  >
                    {pill.icon && (
                      <svg className="peek-pill-icon" viewBox="0 0 24 24" aria-hidden="true">
                        <path d={pill.icon} fill="currentColor" />
                      </svg>
                    )}
                    {pillText(pill, t)}
                  </span>
                ))}
              </div>
            )}

            {data.relationships.length > 0 && (
              <div className="peek-section">
                <div className="peek-caption">{t('focusPeek.relationships')}</div>
                {data.relationships.map((rel, i) => (
                  <div key={i} className="peek-rel-row">
                    <span className="peek-rel-role">{rel.role}</span>
                    <span className="peek-rel-targets">
                      {rel.targets.map((tgt, j) => (
                        <span key={j}>
                          {j > 0 && <span className="peek-rel-sep">, </span>}
                          {tgt.entityId && tgt.typeKey ? (
                            <button
                              className="peek-link"
                              onClick={() => navigateTo(tgt.typeKey!, tgt.entityId!)}
                            >
                              {tgt.name}
                            </button>
                          ) : (
                            <span className="peek-rel-plain">{tgt.name}</span>
                          )}
                        </span>
                      ))}
                    </span>
                  </div>
                ))}
              </div>
            )}

            {data.appearanceProps.length > 0 && (
              <div className="peek-section">
                <div className="peek-caption">{t('focusPeek.appearance')}</div>
                <div className="peek-inline-props">
                  {data.appearanceProps.map((prop, i) => (
                    <span key={i} className="peek-inline-prop">
                      <span className="peek-prop-key">{t(prop.key)}: </span>
                      <span className="peek-prop-val">{prop.value}</span>
                    </span>
                  ))}
                </div>
              </div>
            )}

            {data.customProps.length > 0 && (
              <div className="peek-section">
                {data.customProps.map((prop, i) => (
                  <div key={i} className="peek-prop-row">
                    <span className="peek-prop-key">{prop.key}: </span>
                    <span className="peek-prop-val">{prop.value}</span>
                  </div>
                ))}
              </div>
            )}

            {data.description && (
              <div className="peek-section">
                <div className="peek-description">{data.description}</div>
              </div>
            )}

            {data.mapPins.length > 0 && (
              <div className="peek-section">
                <div className="peek-caption">{t('focusPeek.mapPins')}</div>
                <div className="peek-pins">
                  {data.mapPins.map((pin, i) => {
                    const label = pin.pinLabel && pin.mapName
                      ? `${pin.pinLabel} · ${pin.mapName}`
                      : pin.pinLabel || pin.mapName
                    return (
                      <button
                        key={i}
                        className="peek-link"
                        onClick={() =>
                          useShellStore.getState().navigateToMapPin(pin.mapId, pin.pinId)
                        }
                      >
                        {label}
                      </button>
                    )
                  })}
                </div>
              </div>
            )}

            {/* Findings a previous chapter analysis recorded about this entity.
                Read-only: the host surfaces them, an extension produces them. */}
            {data.aiFindings && data.aiFindings.length > 0 && (
              <div className="peek-section">
                <div className="peek-caption">{t('focusPeek.aiFocus')}</div>
                <ul className="peek-findings">
                  {data.aiFindings.map((finding, i) => (
                    <li key={i}>
                      <span className="peek-finding-title">
                        <span className="peek-finding-marker" aria-hidden="true">
                          {FINDING_MARKER[finding.type] ?? FINDING_MARKER.suggestion}
                        </span>
                        {finding.title}
                      </span>
                      {finding.description && (
                        <span className="peek-finding-desc">{finding.description}</span>
                      )}
                      {finding.excerpt && (
                        <span className="peek-finding-excerpt">{finding.excerpt}</span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </div>

        {data.sections.length > 0 && (
          <div className="peek-section">
            <div className="peek-sections-head">
              <span className="peek-caption">{t('focusPeek.sections')}</span>
              <select
                className="peek-select"
                value={sectionIndex}
                onChange={(e) => setSectionIndex(Number(e.target.value))}
              >
                {data.sections.map((sec, i) => (
                  <option key={i} value={i}>
                    {sec.title}
                  </option>
                ))}
              </select>
            </div>
            {section?.content && (
              /* Section bodies are authored Markdown. The Wiki has always
                 rendered them; here they used to be dumped as raw source, so a
                 peeked entity showed "# Strengths" and "* Brave" verbatim. */
              <div className="peek-section-body">
                <Markdown remarkPlugins={[remarkGfm]}>{section.content}</Markdown>
              </div>
            )}
          </div>
        )}

      </div>
    </div>
  )
}
export { useEntityPeek } from './useEntityPeek'
export type { PeekScope, EntityPeekController } from './peekTypes'
