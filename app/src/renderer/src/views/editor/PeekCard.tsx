import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { EyeOff } from 'lucide-react'
import type { TFunction } from 'i18next'
import { useShellStore } from '../../stores/shellStore'
import { rpc } from '../../rpc/client'
import { placePeekCard, type PeekAnchor } from './peekPlacement'
import './editor.css'

// Shapes returned by the backend `entities/peek` method (camelCase over RPC).
interface PeekImage {
  name: string
  url: string
}
interface PeekPill {
  text: string | null
  labelKey: string | null
  arg: string | null
  dim: boolean
  color: string
  icon: string | null
}
interface PeekProp {
  key: string
  value: string
}
interface PeekRelTarget {
  name: string
  entityId: string | null
  typeKey: string | null
}
interface PeekRel {
  role: string
  targets: PeekRelTarget[]
}
interface PeekSection {
  title: string
  content: string
}
interface PeekMapPin {
  mapId: string
  mapName: string
  pinId: string
  pinLabel: string
}
interface EntityPeek {
  id: string
  typeKey: string
  title: string
  customTypeLabel: string | null
  badgeColor: string
  description: string
  images: PeekImage[]
  pills: PeekPill[]
  appearanceProps: PeekProp[]
  customProps: PeekProp[]
  relationships: PeekRel[]
  sections: PeekSection[]
  mapPins: PeekMapPin[]
  scopeLabel: string | null
  /** Cached findings about this entity from a previous chapter analysis. Absent
   *  when none has been run for the open chapter. */
  aiFindings: PeekFinding[] | null
}

interface PeekFinding {
  type: string
  title: string
  description: string
  excerpt: string
}

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

/** The open editor's chapter/scene, threaded into `entities/peek` so a character
 * peek resolves its per-chapter/scene overrides for the scope currently in view. */
export interface PeekScope {
  chapterGuid: string | null
  chapterTitle: string | null
  sceneTitle: string | null
  /** The open scene, so the card can silence this entry's detection here. */
  sceneId: string | null
}

/** Where the peek card sits, plus which entity it currently shows. */
interface HoverCard {
  /** What the card belongs to, in viewport coordinates: the whole word, not the
   *  pixel under the pointer. Keeping the anchor stable while the pointer moves
   *  inside the word is what stops the card jumping about. */
  anchor: PeekAnchor
  prefer: 'below' | 'beside'
  entityType: string
  entityId: string
}

/** Rounded to the pixel: sub-pixel jitter from a reflow is not a new anchor. */
function sameAnchor(a: PeekAnchor, b: PeekAnchor): boolean {
  return (
    Math.round(a.left) === Math.round(b.left) &&
    Math.round(a.top) === Math.round(b.top) &&
    Math.round(a.right) === Math.round(b.right) &&
    Math.round(a.bottom) === Math.round(b.bottom)
  )
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

  // A fresh hover (new prop target) resets in-place navigation.
  useEffect(() => {
    setNav(target)
  }, [target])

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

/** Imperative controls plus the rendered overlay for a shared entity peek. Both
 * the editor (driven by iframe hover messages) and the context sidebar (driven by
 * card mouse-enter/leave) use one of these so the peek behaves identically. */
export interface EntityPeekController {
  /** Show the peek for an entity, anchored to a rectangle in viewport
   * coordinates - the hovered word, or the sidebar row it belongs to. Showing
   * the same entity against the same rectangle again is a no-op, so a pointer
   * moving inside the word neither moves the card nor refetches it. A pinned
   * card ignores this and stays put, matching the desktop app. */
  showAt(
    target: { entityType: string; entityId: string },
    anchor: PeekAnchor,
    prefer?: 'below' | 'beside'
  ): void
  /** Debounced hide — cancelled if the pointer reaches the card (or it is pinned),
   * so moving onto the card never dismisses it. */
  scheduleHide(): void
  /** Cancel a pending hide (pointer re-entered the trigger or the card). */
  clearHide(): void
  /** Hide immediately unless the card is pinned (e.g. a click in the editor). */
  hide(): void
  /** True while the pointer is over the card itself — lets callers guard against a
   * late async exit event closing the card the moment the pointer reaches it. */
  isPointerOverCard(): boolean
  /** The anchored PeekCard overlay to render (or null when nothing is shown). */
  overlay: React.JSX.Element | null
}

/**
 * Owns the show/hide debounce, the pointer-over-card guard, pin state, and
 * viewport-clamped positioning for a single shared focus-peek overlay. Returns
 * imperative controls plus the overlay element the caller renders in its tree.
 */
export function useEntityPeek(opts: {
  scope: PeekScope
  onOpen: (type: string, id: string) => void
}): EntityPeekController {
  const [hoverCard, setHoverCard] = useState<HoverCard | null>(null)
  // A pinned card ignores hover changes and stays until explicitly closed.
  const [pinned, setPinned] = useState(false)
  const anchorRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ left: number; top: number } | null>(null)
  const pinnedRef = useRef(false)
  pinnedRef.current = pinned
  const hoverHideRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // True while the pointer is over the card itself. Hover-source events (the
  // editor's entityExit message arrives asynchronously via postMessage; a
  // sidebar card's mouseleave fires just before the card's mouseenter) can land
  // *after* the pointer has already moved onto the card, so without this guard
  // the late exit would re-schedule a hide and close the card as you reach it.
  const pointerOverCardRef = useRef(false)
  // Last known pointer position, tracked on the window rather than inferred from
  // the anchor's mouseenter. An element that appears *underneath* a stationary
  // cursor does not reliably receive mouseenter, so the flag above can stay
  // false while the pointer is in fact over the card - which used to let the
  // debounced hide fire and start a show/hide loop.
  const pointerRef = useRef<{ x: number; y: number } | null>(null)
  // The entity the visible card is for, so re-showing the same one can keep its
  // measured position instead of blinking through an unpositioned frame.
  const shownKeyRef = useRef<string | null>(null)
  // On mobile the peek is a full-screen sheet (see the anchor's `mobile` class)
  // rather than a card anchored at the tap point, which clipped off-screen.
  const isMobilePeek = window.novalist.isMobile === true

  const pointerIsOverCard = (): boolean => {
    if (pointerOverCardRef.current) return true
    const rect = anchorRef.current?.getBoundingClientRect()
    const pointer = pointerRef.current
    if (!rect || !pointer) return false
    return (
      pointer.x >= rect.left &&
      pointer.x <= rect.right &&
      pointer.y >= rect.top &&
      pointer.y <= rect.bottom
    )
  }

  const clearHide = (): void => {
    if (hoverHideRef.current) {
      clearTimeout(hoverHideRef.current)
      hoverHideRef.current = null
    }
  }
  const scheduleHide = (): void => {
    if (pinnedRef.current || pointerIsOverCard()) return
    clearHide()
    hoverHideRef.current = setTimeout(() => {
      hoverHideRef.current = null
      // Re-checked on expiry, not only when scheduled: the pointer may have
      // reached the card in the meantime, and hiding it out from under the
      // pointer is what the editor beneath reads as a fresh hover.
      if (pinnedRef.current || pointerIsOverCard()) return
      shownKeyRef.current = null
      setHoverCard(null)
    }, 260)
  }
  const hide = (): void => {
    // A pinned card survives (matches the desktop: a click in the editor does not
    // dismiss a pinned peek).
    if (pinnedRef.current) return
    clearHide()
    shownKeyRef.current = null
    setHoverCard(null)
  }
  const showAt = (
    target: { entityType: string; entityId: string },
    anchor: PeekAnchor,
    prefer: 'below' | 'beside' = 'below'
  ): void => {
    // A pinned card stays put and ignores hover changes.
    if (pinnedRef.current) return
    clearHide()
    const key = `${target.entityType}:${target.entityId}`
    if (shownKeyRef.current !== key) {
      // A different entity: measure it hidden before showing it anywhere.
      shownKeyRef.current = key
      setPosition(null)
    }
    setHoverCard((current) =>
      current &&
      current.entityType === target.entityType &&
      current.entityId === target.entityId &&
      current.prefer === prefer &&
      sameAnchor(current.anchor, anchor)
        ? // Same entity, same word: nothing to re-place and nothing to refetch.
          current
        : { anchor, prefer, entityType: target.entityType, entityId: target.entityId }
    )
  }
  const closeCard = (): void => {
    clearHide()
    setPinned(false)
    shownKeyRef.current = null
    setHoverCard(null)
  }

  // Clear any pending timer if the host unmounts.
  useEffect(() => clearHide, [])

  useEffect(() => {
    const track = (event: PointerEvent): void => {
      pointerRef.current = { x: event.clientX, y: event.clientY }
    }
    window.addEventListener('pointermove', track, { passive: true })
    return () => window.removeEventListener('pointermove', track)
  }, [])

  // The card grows as its asynchronous sections arrive. Measure the rendered
  // element every time it changes instead of clamping against an obsolete
  // guessed width/height, and re-place it when the pane/window resizes.
  // Primitives, so the placement effect re-runs when the anchor actually moves
  // rather than on every render that rebuilds the object around it.
  const anchorLeft = hoverCard?.anchor.left ?? 0
  const anchorTop = hoverCard?.anchor.top ?? 0
  const anchorRight = hoverCard?.anchor.right ?? 0
  const anchorBottom = hoverCard?.anchor.bottom ?? 0
  const prefer = hoverCard?.prefer ?? 'below'
  const showing = hoverCard !== null

  useLayoutEffect(() => {
    const anchor = anchorRef.current
    if (!showing || !anchor || isMobilePeek || pinned) return
    const place = (): void => {
      const styles = getComputedStyle(document.documentElement)
      const gap = Number.parseFloat(styles.getPropertyValue('--nl-space-md')) || 12
      const rect = anchor.getBoundingClientRect()
      // The rule lives in peekPlacement.ts, where it can be checked over every
      // geometry rather than only the ones a hover test happens to produce.
      const { left, top } = placePeekCard({
        anchor: {
          left: anchorLeft,
          top: anchorTop,
          right: anchorRight,
          bottom: anchorBottom
        },
        prefer,
        width: rect.width,
        height: rect.height,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        gap
      })

      setPosition((current) =>
        current?.left === left && current.top === top ? current : { left, top }
      )
    }
    place()
    const observer = new ResizeObserver(place)
    observer.observe(anchor)
    window.addEventListener('resize', place)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', place)
    }
  }, [
    showing,
    anchorLeft,
    anchorTop,
    anchorRight,
    anchorBottom,
    prefer,
    isMobilePeek,
    pinned
  ])

  const overlay = hoverCard ? (
    <div
      ref={anchorRef}
      className={`peek-card-anchor${pinned ? ' pinned' : ''}${isMobilePeek ? ' mobile' : ''}`}
      style={
        isMobilePeek || pinned
          ? undefined
          : position
            ? position
            : { left: 0, top: 0, visibility: 'hidden' }
      }
      onMouseEnter={() => {
        pointerOverCardRef.current = true
        clearHide()
      }}
      onMouseLeave={() => {
        pointerOverCardRef.current = false
        // The window stops seeing pointermove once the cursor is inside the
        // editor iframe, so the last recorded position would otherwise go stale
        // *inside* the card's box and hold it open. Leaving the card is the one
        // moment we know for certain the pointer is not on it.
        pointerRef.current = null
        scheduleHide()
      }}
      // Mobile: the peek is a full-screen sheet; a tap on the scrim (outside the
      // card) closes it, matching the X. The card stops propagation, so only
      // scrim taps reach here.
      onClick={isMobilePeek ? () => closeCard() : undefined}
    >
      <PeekCard
        key={`${hoverCard.entityType}:${hoverCard.entityId}`}
        target={{ entityType: hoverCard.entityType, entityId: hoverCard.entityId }}
        scope={opts.scope}
        onOpen={(type, id) => {
          closeCard()
          opts.onOpen(type, id)
        }}
        onClose={closeCard}
        pinned={pinned}
        onTogglePin={() => setPinned((p) => !p)}
      />
    </div>
  ) : null

  return {
    showAt,
    scheduleHide,
    clearHide,
    hide,
    isPointerOverCard: pointerIsOverCard,
    overlay
  }
}
