import { test, expect } from '@playwright/test'
import {
  placePeekCard,
  placementCoversAnchor,
  type PeekPlacementInput
} from '../src/renderer/src/views/editor/peekPlacement'

/**
 * The one rule a Focus Peek must never break, checked over every geometry
 * instead of the handful a hover test can stage.
 *
 * This is deliberately not a browser test. The flicker it guards against was
 * reproducible only at particular combinations of window height, card height
 * and where the hovered name happened to land after a reflow - which is exactly
 * the kind of condition an end-to-end test reproduces by luck and then stops
 * reproducing the moment a wait is added to steady it. The rule itself is pure
 * arithmetic, so it is tested as pure arithmetic; m61-focus-peek-hover covers
 * the wiring in the real app.
 */

const GAP = 12

/** A word-sized anchor centred on the given point. */
function wordAt(x: number, y: number, width = 90, height = 20): PeekPlacementInput['anchor'] {
  return {
    left: x - width / 2,
    top: y - height / 2,
    right: x + width / 2,
    bottom: y + height / 2
  }
}

function input(over: Partial<PeekPlacementInput>): PeekPlacementInput {
  return {
    anchor: wordAt(400, 300),
    width: 460,
    height: 420,
    viewportWidth: 1440,
    viewportHeight: 900,
    gap: GAP,
    ...over
  }
}

test('a peek is never placed over the name it belongs to', () => {
  const widths = [420, 900, 1280, 1440, 2560]
  const heights = [420, 560, 720, 900, 1440]
  const cards = [
    { width: 320, height: 180 },
    { width: 460, height: 420 },
    { width: 460, height: 760 }
  ]

  const overlaps: string[] = []
  let clear = 0
  let unavoidable = 0

  for (const viewportWidth of widths) {
    for (const viewportHeight of heights) {
      for (const card of cards) {
        // Every anchor point on a coarse grid across the viewport, including
        // the edges, where the old clamp used to drag the card back over.
        for (let fx = 0; fx <= 10; fx++) {
          for (let fy = 0; fy <= 10; fy++) {
            const spec = input({
              anchor: wordAt(
                Math.round((viewportWidth - 1) * (fx / 10)),
                Math.round((viewportHeight - 1) * (fy / 10))
              ),
              width: card.width,
              height: card.height,
              viewportWidth,
              viewportHeight
            })
            const placement = placePeekCard(spec)
            const covers = placementCoversAnchor(placement, spec)

            // Overlap is permitted only where it is provably unavoidable: the
            // card fits neither fully below, above, left nor right of the point.
            // Anywhere it could have escaped, it must have.
            const canEscape =
              spec.anchor.bottom + GAP + card.height <= viewportHeight - GAP ||
              spec.anchor.top - GAP - card.height >= GAP ||
              spec.anchor.right + GAP + card.width <= viewportWidth - GAP ||
              spec.anchor.left - GAP - card.width >= GAP
            if (!canEscape) {
              unavoidable++
              expect(placement.side, 'an unavoidable overlap must say so').toBe('overlap')
              continue
            }

            if (covers) {
              overlaps.push(
                `${viewportWidth}x${viewportHeight} card ${card.width}x${card.height}`
                + ` at (${spec.anchor.left},${spec.anchor.top}) -> ${placement.side}`
              )
            } else {
              clear++
            }
          }
        }
      }
    }
  }

  expect(overlaps.slice(0, 8), `${overlaps.length} placements covered the anchor`).toEqual([])
  // Guards the guard: a rule that placed nothing would also report no overlaps.
  expect(clear).toBeGreaterThan(1_000)
  expect(unavoidable).toBeGreaterThan(0)
})

test('a peek stays inside the viewport', () => {
  for (const viewportHeight of [420, 560, 900]) {
    for (let fy = 0; fy <= 10; fy++) {
      const spec = input({
        anchor: wordAt(400, Math.round((viewportHeight - 1) * (fy / 10))),
        viewportHeight
      })
      const { left, top } = placePeekCard(spec)
      expect(left, `left at ${fy}/10 of ${viewportHeight}`).toBeGreaterThanOrEqual(0)
      expect(top, `top at ${fy}/10 of ${viewportHeight}`).toBeGreaterThanOrEqual(0)
      expect(left + spec.width).toBeLessThanOrEqual(spec.viewportWidth)
      // A card taller than the window cannot be fully inside it; it is pinned to
      // the top rather than pushed off the bottom.
      if (spec.height + 2 * GAP <= viewportHeight) {
        expect(top + spec.height).toBeLessThanOrEqual(viewportHeight)
      }
    }
  }
})

test('the preferred side is below the name, and above only when below will not fit', () => {
  // Room underneath: below.
  expect(placePeekCard(input({ anchor: wordAt(400, 100) })).side).toBe('below')
  // No room underneath, plenty above: above.
  expect(placePeekCard(input({ anchor: wordAt(400, 860), viewportHeight: 900 })).side).toBe('above')
  // Neither band fits a tall card in a short window: beside, not over.
  const squeezed = input({ anchor: wordAt(400, 280), viewportHeight: 560, height: 420 })
  expect(placePeekCard(squeezed).side).toBe('right')
  expect(placementCoversAnchor(placePeekCard(squeezed), squeezed)).toBe(false)
  // ...and on the other side when there is no room to the right either.
  const rightEdge = { ...squeezed, anchor: wordAt(1400, 280) }
  expect(placePeekCard(rightEdge).side).toBe('left')
  expect(placementCoversAnchor(placePeekCard(rightEdge), rightEdge)).toBe(false)

  // A sidebar row asks for beside, and gets the side with the room.
  const row = input({ anchor: { left: 1100, top: 200, right: 1420, bottom: 236 }, prefer: 'beside' })
  expect(placePeekCard(row).side).toBe('left')
  expect(placementCoversAnchor(placePeekCard(row), row)).toBe(false)
})
