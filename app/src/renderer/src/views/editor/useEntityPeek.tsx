import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { placePeekCard, type PeekAnchor } from './peekPlacement'
import { type PeekScope, type HoverCard, type EntityPeekController } from './peekTypes'
import { PeekCard } from './PeekCard'
import { MotionPresence } from '../../shell/MotionPresence'
import { useProjectStore } from '../../stores/projectStore'

/** Rounded to the pixel: sub-pixel jitter from a reflow is not a new anchor. */
function sameAnchor(a: PeekAnchor, b: PeekAnchor): boolean {
  return (
    Math.round(a.left) === Math.round(b.left) &&
    Math.round(a.top) === Math.round(b.top) &&
    Math.round(a.right) === Math.round(b.right) &&
    Math.round(a.bottom) === Math.round(b.bottom)
  )
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
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
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
      // Nothing is placed until there is something to place. The card's
      // contents come from a request, so between the hover and the answer the
      // anchor is an empty box - and an empty box is not a small card, it is an
      // unmeasured one. Placing it is worse than leaving it hidden: a sidebar
      // peek goes to the left of its row *by its own width*, so a zero-width
      // measurement puts the card squarely on the row it belongs to, and the
      // real measurement then throws it a card's width sideways. The observer
      // below fires the moment the card has a size, which is the first moment
      // the answer is worth anything.
      if (rect.width === 0 || rect.height === 0) return
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

  const overlay = <MotionPresence disabled={suspendMotion}>{hoverCard && (
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
  )}</MotionPresence>

  return {
    showAt,
    scheduleHide,
    clearHide,
    hide,
    isPointerOverCard: pointerIsOverCard,
    overlay
  }
}
