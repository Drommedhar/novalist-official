/**
 * Where a Focus Peek card goes, given the thing it belongs to.
 *
 * Its own module, with no imports, for two reasons. It is the one rule the card
 * must never break - and it is a rule about geometry, which a test can check
 * exhaustively without a window, a reflow, or a hover that has to land.
 *
 * **The rule: the card never overlaps what it is anchored to.**
 *
 * The peek is host DOM drawn over the editor iframe, so a card sitting on the
 * name takes the hover away from the editor underneath it. The editor then
 * reports that the pointer left the name, the host hides the card, the pointer
 * is over the name again, and it reopens - several times a second, for as long
 * as the reader keeps still. Both halves behave correctly; it is the overlap
 * that is the bug, and no amount of debouncing settles it.
 *
 * The anchor is a **rectangle, not a point**, and that distinction is the whole
 * fix. Anchored to the pointer, the card cleared the single pixel under the
 * cursor and still covered the rest of the word - so the smallest drift of a
 * resting hand put the pointer on the card and started the loop again. It also
 * re-anchored on every re-entry, which made the card jump about while the
 * reader thought they were holding still. Anchored to the word, the card sits
 * clear of the whole of it and does not move while the pointer stays on it.
 *
 * An earlier attempt placed the card below the anchor and, when that did not
 * fit, above it - then clamped the result into the viewport. The clamp is what
 * broke the rule: a card taller than the space above it was pushed back down
 * over the very thing it was meant to sit clear of, which is why the flicker
 * showed up on a name near the top of a short window.
 */

/** What the card belongs to, in viewport coordinates. A caret or a tap is a
 *  rectangle of zero size. */
export interface PeekAnchor {
  left: number
  top: number
  right: number
  bottom: number
}

export interface PeekPlacementInput {
  anchor: PeekAnchor
  /** The card as actually rendered, not as guessed. */
  width: number
  height: number
  viewportWidth: number
  viewportHeight: number
  /** Breathing room, and the distance kept from the anchor. */
  gap: number
  /**
   * `below` reads best for a word in prose. `beside` is for an anchor in a side
   * panel, where dropping the card underneath the row it belongs to would bury
   * the rest of the list.
   */
  prefer?: 'below' | 'beside'
}

export interface PeekPlacement {
  left: number
  top: number
  /** Which rule fired, so a test failure names the branch rather than a number. */
  side: 'below' | 'above' | 'right' | 'left' | 'overlap'
}

/**
 * `side` is `overlap` only when the viewport is too small to hold the card clear
 * of the anchor on any side. There is nothing correct to do there; the caller's
 * pointer guard covers it.
 */
export function placePeekCard(input: PeekPlacementInput): PeekPlacement {
  const { anchor, width, height, viewportWidth, viewportHeight, gap } = input
  const clampX = (value: number): number =>
    Math.max(gap, Math.min(value, viewportWidth - width - gap))
  const clampY = (value: number): number =>
    Math.max(gap, Math.min(value, viewportHeight - height - gap))

  const below = (): PeekPlacement | null =>
    anchor.bottom + gap + height <= viewportHeight - gap
      ? { left: clampX(anchor.left), top: anchor.bottom + gap, side: 'below' }
      : null
  const above = (): PeekPlacement | null =>
    anchor.top - gap - height >= gap
      ? { left: clampX(anchor.left), top: anchor.top - gap - height, side: 'above' }
      : null
  const right = (): PeekPlacement | null =>
    anchor.right + gap + width <= viewportWidth - gap
      ? { left: anchor.right + gap, top: clampY(anchor.top), side: 'right' }
      : null
  const left = (): PeekPlacement | null =>
    anchor.left - gap - width >= gap
      ? { left: anchor.left - gap - width, top: clampY(anchor.top), side: 'left' }
      : null

  const order =
    input.prefer === 'beside' ? [left, right, below, above] : [below, above, right, left]
  for (const candidate of order) {
    const placement = candidate()
    if (placement) return placement
  }

  // A window with no room for the card on any side of the anchor.
  return { left: clampX(anchor.left), top: clampY(anchor.bottom + gap), side: 'overlap' }
}

/** True when the placed card would overlap the anchor it belongs to. */
export function placementCoversAnchor(
  placement: PeekPlacement,
  input: PeekPlacementInput
): boolean {
  const { anchor, width, height } = input
  return (
    placement.left < anchor.right &&
    placement.left + width > anchor.left &&
    placement.top < anchor.bottom &&
    placement.top + height > anchor.top
  )
}
