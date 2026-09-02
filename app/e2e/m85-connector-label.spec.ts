import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour } from './harness'

type StoredCanvas = {
  id: string
  cards: { id: string; x: number; y: number }[]
  connectors: {
    id: string
    label: string
    fromSide: string
    toSide: string
  }[]
}

/**
 * Labelling the line between two planning cards.
 *
 * Connector labels existed in the file format from the start, but the board
 * created every line with an empty label and made the SVG ignore every pointer
 * event. There was consequently no gesture, button or field a writer could use
 * to supply the data the model already knew how to store.
 */
test('a planning-board connector can be labelled, reopened, cleared and deleted', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-connector-label-')
  await seedBook(h, { 'Chapter One': ['Scene One'] })
  // The first-run tour is offered only after a project exists, so dismiss it
  // after seeding rather than racing its delayed appearance.
  await dismissTour(h.page)

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('canvas'))
  const toolbar = h.page.locator('.canvas-toolbar')
  await expect(toolbar).toBeVisible({ timeout: 15_000 })

  await toolbar.getByRole('button', { name: 'New board' }).click()
  const dialog = h.page.locator('.dialog-card')
  await dialog.locator('.dialog-input').fill('Causal chain')
  await dialog.getByRole('button', { name: 'OK' }).click()

  await toolbar.getByRole('button', { name: 'Add card' }).click()
  await toolbar.getByRole('button', { name: 'Add card' }).click()
  const cards = h.page.locator('.canvas-card')
  await expect(cards).toHaveCount(2)
  await cards.nth(0).locator('.canvas-card-title').fill('Cause')
  await cards.nth(1).locator('.canvas-card-title').fill('Effect')

  // The old toolbar mode is gone. Every card exposes four real edge handles,
  // and its title remains an ordinary editable field rather than a drag trap.
  await expect(toolbar.getByRole('button', { name: 'Connect...', exact: true })).toHaveCount(0)
  await expect(cards.nth(0).locator('.canvas-connector-handle')).toHaveCount(4)
  await expect(cards.nth(1).locator('.canvas-connector-handle')).toHaveCount(4)
  expect(
    await cards
      .nth(0)
      .locator('.canvas-connector-handle')
      .evaluateAll((handles) => handles.map((handle) => (handle as HTMLElement).dataset.connectorSide))
  ).toEqual(['top', 'right', 'bottom', 'left'])

  // New cards overlap slightly. The full-width grip is a reliable movement
  // target and commits one final position when the pointer is released.
  const secondGrip = cards.nth(1).locator('.canvas-card-move-handle')
  await expect(secondGrip).toBeVisible()
  const grip = await secondGrip.boundingBox()
  const secondBefore = await cards.nth(1).boundingBox()
  expect(grip).not.toBeNull()
  expect(secondBefore).not.toBeNull()
  expect(grip!.height).toBeGreaterThanOrEqual(28)
  await h.page.mouse.move(grip!.x + grip!.width / 2, grip!.y + grip!.height / 2)
  await h.page.mouse.down()
  await h.page.mouse.move(grip!.x + 400, grip!.y + 200, { steps: 6 })
  await h.page.mouse.up()
  const secondAfter = await cards.nth(1).boundingBox()
  expect(secondAfter).not.toBeNull()
  expect(secondAfter!.x).toBeGreaterThan(secondBefore!.x + 300)

  // The same grip is keyboard-operable. Arrow keys nudge rather than placing a
  // caret in the title or invoking a hidden mode.
  await secondGrip.focus()
  const leftBeforeNudge = Number.parseFloat((await cards.nth(1).getAttribute('style'))?.match(/left:\s*([\d.]+)/)?.[1] ?? '0')
  await h.page.keyboard.press('ArrowRight')
  await expect
    .poll(async () =>
      Number.parseFloat((await cards.nth(1).getAttribute('style'))?.match(/left:\s*([\d.]+)/)?.[1] ?? '0')
    )
    .toBe(leftBeforeNudge + 12)

  // A cancelled grip gesture discards its RAF preview and never reaches the
  // debounced save. Drive it from a real pointerdown so pointer capture is the
  // same as it is for a writer using a mouse or pen.
  const [summary] = await h.rpc<{ id: string }[]>('canvas/list')
  const secondCardId = await cards.nth(1).getAttribute('data-canvas-card-id')
  const positionOf = async (index: number): Promise<{ x: number; y: number }> => {
    const style = await cards.nth(index).getAttribute('style')
    return {
      x: Number.parseFloat(style?.match(/left:\s*([\d.]+)/)?.[1] ?? '-1'),
      y: Number.parseFloat(style?.match(/top:\s*([\d.]+)/)?.[1] ?? '-1')
    }
  }
  const settledPosition = await positionOf(1)
  await expect
    .poll(async () => {
      const stored = await h.rpc<StoredCanvas>('canvas/load', [summary.id])
      const card = stored.cards.find((item) => item.id === secondCardId)
      return card ? { x: card.x, y: card.y } : null
    })
    .toEqual(settledPosition)
  const cancelGripBounds = await secondGrip.boundingBox()
  expect(cancelGripBounds).not.toBeNull()
  await secondGrip.evaluate((handle) => {
    handle.addEventListener(
      'pointerdown',
      (event) => {
        handle.dataset.testPointerId = String((event as PointerEvent).pointerId)
      },
      { once: true }
    )
  })
  await h.page.mouse.move(
    cancelGripBounds!.x + cancelGripBounds!.width / 2,
    cancelGripBounds!.y + cancelGripBounds!.height / 2
  )
  await h.page.mouse.down()
  await h.page.mouse.move(cancelGripBounds!.x + 140, cancelGripBounds!.y + 100, { steps: 6 })
  await expect.poll(async () => (await positionOf(1)).x).toBeGreaterThan(settledPosition.x)
  const cardPointerId = Number(await secondGrip.getAttribute('data-test-pointer-id'))
  await secondGrip.dispatchEvent('pointercancel', {
    pointerId: cardPointerId,
    pointerType: 'mouse',
    isPrimary: true,
    button: 0,
    buttons: 0
  })
  await h.page.mouse.up()
  await expect.poll(async () => positionOf(1)).toEqual(settledPosition)

  // Secondary-clicking a knob is not a drawing gesture.
  const sourceHandle = cards.nth(0).locator('.canvas-connector-handle.right')
  await sourceHandle.click({ button: 'right' })
  await expect(h.page.locator('.canvas-connector')).toHaveCount(0)

  // Draw directly from the first card's right knob and release over the second
  // card's text field. The whole card is a valid target, not only another knob.
  const source = await sourceHandle.boundingBox()
  const targetBody = await cards.nth(1).locator('.canvas-card-text').boundingBox()
  expect(source).not.toBeNull()
  expect(targetBody).not.toBeNull()
  await sourceHandle.evaluate((handle) => {
    handle.addEventListener(
      'pointerdown',
      (event) => {
        handle.dataset.testPointerId = String((event as PointerEvent).pointerId)
      },
      { once: true }
    )
  })
  await h.page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2)
  await h.page.mouse.down()
  await h.page.mouse.move(targetBody!.x - 30, targetBody!.y + targetBody!.height / 2, {
    steps: 6
  })
  await expect(h.page.locator('.canvas-connector-preview')).toHaveCount(1)
  const connectorPointerId = Number(await sourceHandle.getAttribute('data-test-pointer-id'))
  expect(
    await sourceHandle.evaluate((handle, pointerId) => {
      if (!handle.hasPointerCapture(pointerId)) return false
      handle.releasePointerCapture(pointerId)
      return true
    }, connectorPointerId)
  ).toBe(true)
  await sourceHandle.dispatchEvent('lostpointercapture', {
    pointerId: connectorPointerId,
    pointerType: 'mouse',
    isPrimary: true
  })
  await expect(h.page.locator('.canvas-connector-preview')).toHaveCount(0)
  await h.page.mouse.up()
  await h.page.waitForTimeout(2300)
  await expect(h.page.locator('.canvas-connector')).toHaveCount(0)
  const afterCancellation = await h.rpc<StoredCanvas>('canvas/load', [summary.id])
  expect(afterCancellation.cards.find((item) => item.id === secondCardId)).toMatchObject(settledPosition)
  expect(afterCancellation.connectors).toHaveLength(0)

  await h.page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2)
  await h.page.mouse.down()
  await h.page.mouse.move(targetBody!.x + 20, targetBody!.y + targetBody!.height / 2, { steps: 8 })
  await h.page.mouse.up()

  // A new line edits at its midpoint immediately; there is no lower-right
  // connector form to hunt for.
  const label = h.page.getByRole('textbox', {
    name: 'Label connector between Cause and Effect'
  })
  await expect(label).toBeVisible()
  await expect(label).toBeFocused()
  await expect(label.locator('xpath=ancestor::*[contains(@class, "canvas-connector-label-editor")]')).toHaveCount(1)
  await label.fill('  Because of  ')
  await label.press('Enter')
  const labelButton = h.page.getByRole('button', {
    name: 'Edit connector between Cause and Effect'
  })
  await expect(labelButton).toHaveText('Because of')

  await expect
    .poll(async () => {
      const stored = await h.rpc<StoredCanvas>('canvas/load', [summary.id])
      const connector = stored.connectors[0]
      return connector
        ? { label: connector.label, fromSide: connector.fromSide, toSide: connector.toSide }
        : null
    })
    .toEqual({ label: 'Because of', fromSide: 'right', toSide: 'left' })

  // Leave before the two-second debounce elapses. Unmount must flush the label
  // and chosen endpoint sides rather than silently dropping the board edit.
  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await expect(h.page.locator('.canvas-view')).toHaveCount(0)
  await expect
    .poll(async () => {
      const stored = await h.rpc<StoredCanvas>('canvas/load', [summary.id])
      const connector = stored.connectors[0]
      return connector
        ? { label: connector.label, fromSide: connector.fromSide, toSide: connector.toSide }
        : null
    })
    .toEqual({ label: 'Because of', fromSide: 'right', toSide: 'left' })
  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('canvas'))
  await expect(labelButton).toHaveText('Because of', { timeout: 15_000 })

  // A card may cross a connector midpoint. Passive labels can sit beneath the
  // card, but an opened editor must rise above it so the field remains usable.
  const labelBounds = await labelButton.boundingBox()
  expect(labelBounds).not.toBeNull()
  await toolbar.getByRole('button', { name: 'Add card' }).click()
  await expect(cards).toHaveCount(3)
  await cards.nth(2).locator('.canvas-card-title').fill('Overlap')
  const overlapGrip = cards.nth(2).locator('.canvas-card-move-handle')
  const overlapGripBounds = await overlapGrip.boundingBox()
  expect(overlapGripBounds).not.toBeNull()
  await h.page.mouse.move(
    overlapGripBounds!.x + overlapGripBounds!.width / 2,
    overlapGripBounds!.y + overlapGripBounds!.height / 2
  )
  await h.page.mouse.down()
  await h.page.mouse.move(
    labelBounds!.x + labelBounds!.width / 2,
    labelBounds!.y + labelBounds!.height / 2,
    { steps: 6 }
  )
  await h.page.mouse.up()
  const overlapBounds = await cards.nth(2).boundingBox()
  expect(overlapBounds).not.toBeNull()
  expect(labelBounds!.x + labelBounds!.width / 2).toBeGreaterThan(overlapBounds!.x)
  expect(labelBounds!.x + labelBounds!.width / 2).toBeLessThan(overlapBounds!.x + overlapBounds!.width)
  expect(labelBounds!.y + labelBounds!.height / 2).toBeGreaterThan(overlapBounds!.y)
  expect(labelBounds!.y + labelBounds!.height / 2).toBeLessThan(overlapBounds!.y + overlapBounds!.height)

  // Clicking the wide line hit target reopens the inline field. Escape restores
  // the old label; Enter commits and trims; blank remains a valid value.
  const hitLine = h.page.locator('.canvas-connector-hit')
  const hitBounds = await hitLine.boundingBox()
  expect(hitBounds).not.toBeNull()
  await hitLine.click({
    position: { x: hitBounds!.width / 4, y: hitBounds!.height / 4 }
  })
  await expect(label).toBeVisible()
  expect(
    await label.evaluate((input) => {
      const bounds = input.getBoundingClientRect()
      const topmost = document.elementFromPoint(
        bounds.left + bounds.width / 2,
        bounds.top + bounds.height / 2
      )
      return Boolean(topmost?.closest('.canvas-connector-label-editor'))
    })
  ).toBe(true)
  await label.fill('Temporary')
  await label.press('Escape')
  await expect(labelButton).toHaveText('Because of')
  await expect(labelButton).toBeFocused()
  expect(
    await labelButton.evaluate((button) => {
      const bounds = button.getBoundingClientRect()
      return (
        document.elementFromPoint(
          bounds.left + bounds.width / 2,
          bounds.top + bounds.height / 2
        ) === button
      )
    })
  ).toBe(true)

  // Pointer gestures prevent the browser's normal blur. Starting either kind
  // of drag still commits the active inline edit before changing selection.
  await labelButton.click()
  await label.fill('  Committed by grip  ')
  await cards.nth(0).locator('.canvas-card-move-handle').click()
  await expect(labelButton).toHaveText('Committed by grip')
  await hitLine.click({
    position: { x: hitBounds!.width / 4, y: hitBounds!.height / 4 }
  })
  await label.fill('Grip rollback')
  await label.press('Escape')
  await expect(labelButton).toHaveText('Committed by grip')

  await labelButton.click()
  await label.fill('  Committed by handle  ')
  await sourceHandle.click()
  await expect(labelButton).toHaveText('Committed by handle')
  await hitLine.click({
    position: { x: hitBounds!.width / 4, y: hitBounds!.height / 4 }
  })
  await label.fill('Handle rollback')
  await label.press('Escape')
  await expect(labelButton).toHaveText('Committed by handle')

  // Selecting an earlier card raises it while it is dragged, so it remains
  // grabbable above later cards. Releasing a connector on that visible source
  // must not tunnel through to the card hidden underneath it.
  const firstOverlapBounds = await cards.nth(0).boundingBox()
  const overlapCardBounds = await cards.nth(2).boundingBox()
  const firstMoveBounds = await cards.nth(0).locator('.canvas-card-move-handle').boundingBox()
  expect(firstOverlapBounds).not.toBeNull()
  expect(overlapCardBounds).not.toBeNull()
  expect(firstMoveBounds).not.toBeNull()
  const firstMoveX = firstMoveBounds!.x + firstMoveBounds!.width / 2
  const firstMoveY = firstMoveBounds!.y + firstMoveBounds!.height / 2
  await h.page.mouse.move(firstMoveX, firstMoveY)
  await h.page.mouse.down()
  await h.page.mouse.move(
    overlapCardBounds!.x + firstMoveX - firstOverlapBounds!.x,
    overlapCardBounds!.y + firstMoveY - firstOverlapBounds!.y,
    { steps: 6 }
  )
  await h.page.mouse.up()
  expect(
    await cards.nth(0).locator('.canvas-card-move-handle').evaluate((handle) => {
      const bounds = handle.getBoundingClientRect()
      return document
        .elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2)
        ?.closest('[data-canvas-card-id]')?.getAttribute('data-canvas-card-id')
    })
  ).toBe(await cards.nth(0).getAttribute('data-canvas-card-id'))
  const overlapSource = await cards.nth(0).locator('.canvas-connector-handle.right').boundingBox()
  const overlapBody = await cards.nth(0).locator('.canvas-card-text').boundingBox()
  expect(overlapSource).not.toBeNull()
  expect(overlapBody).not.toBeNull()
  await h.page.mouse.move(
    overlapSource!.x + overlapSource!.width / 2,
    overlapSource!.y + overlapSource!.height / 2
  )
  await h.page.mouse.down()
  await h.page.mouse.move(
    overlapBody!.x + overlapBody!.width / 2,
    overlapBody!.y + overlapBody!.height / 2,
    { steps: 6 }
  )
  await h.page.mouse.up()
  await expect(h.page.locator('.canvas-connector')).toHaveCount(1)

  const raisedCardBounds = await cards.nth(0).boundingBox()
  const raisedMoveBounds = await cards.nth(0).locator('.canvas-card-move-handle').boundingBox()
  expect(raisedCardBounds).not.toBeNull()
  expect(raisedMoveBounds).not.toBeNull()
  const raisedMoveX = raisedMoveBounds!.x + raisedMoveBounds!.width / 2
  const raisedMoveY = raisedMoveBounds!.y + raisedMoveBounds!.height / 2
  await h.page.mouse.move(raisedMoveX, raisedMoveY)
  await h.page.mouse.down()
  await h.page.mouse.move(
    firstOverlapBounds!.x + raisedMoveX - raisedCardBounds!.x,
    firstOverlapBounds!.y + raisedMoveY - raisedCardBounds!.y,
    { steps: 6 }
  )
  await h.page.mouse.up()

  // Remove the deliberately overlapping card before continuing the connector
  // lifecycle checks below.
  await cards.nth(2).locator('.canvas-card-title').focus()
  await h.page.getByRole('button', { name: 'Delete card', exact: true }).click()
  await expect(cards).toHaveCount(2)

  await labelButton.click()
  await label.fill('  Blurred label  ')
  await cards.nth(0).locator('.canvas-card-title').focus()
  await expect(labelButton).toHaveText('Blurred label')
  await labelButton.click()
  await label.fill('   ')
  await label.press('Enter')
  await expect(labelButton).toHaveText('Add label')

  // The label button is the connector's one keyboard target and carries its
  // editor and delete action directly on the line.
  await labelButton.click()
  await expect(label).toBeVisible()
  await h.page.getByRole('button', { name: 'Delete connector' }).click()
  await expect(h.page.locator('.canvas-connector')).toHaveCount(0)
  await expect
    .poll(async () => {
      const stored = await h.rpc<StoredCanvas>('canvas/load', [summary.id])
      return stored.connectors.length
    })
    .toBe(0)

  // The edge handles also form a keyboard-only connection path, and Escape
  // cancels an armed source without drawing anything.
  const firstTop = cards.nth(0).locator('.canvas-connector-handle.top')
  const secondBottom = cards.nth(1).locator('.canvas-connector-handle.bottom')
  await firstTop.focus()
  await h.page.keyboard.press('Enter')
  await expect(h.page.getByText('Choose a handle on another card. Press Escape to cancel.')).toBeVisible()
  await expect(firstTop).toHaveAttribute('aria-pressed', 'true')
  await h.page.keyboard.press('Enter')
  await expect(firstTop).toHaveAttribute('aria-pressed', 'false')
  await firstTop.press('Enter')
  await h.page.keyboard.press('Escape')
  await expect(h.page.locator('.canvas-connector')).toHaveCount(0)

  await firstTop.focus()
  await h.page.keyboard.press('Enter')
  await secondBottom.focus()
  await h.page.keyboard.press('Enter')
  await expect(label).toBeVisible()
  await h.page.getByRole('button', { name: 'Delete connector' }).click()

  // Deleting a card also clears an armed keyboard source. The first action on
  // the remaining card starts a new connection instead of targeting a ghost.
  await firstTop.focus()
  await h.page.keyboard.press('Enter')
  await h.page.getByRole('button', { name: 'Delete card', exact: true }).click()
  await expect(cards).toHaveCount(1)
  await cards.nth(0).locator('.canvas-connector-handle.top').focus()
  await h.page.keyboard.press('Enter')
  await expect(h.page.locator('.canvas-connector')).toHaveCount(0)
  await expect(h.page.getByText('Choose a handle on another card. Press Escape to cancel.')).toBeVisible()
  await h.page.keyboard.press('Escape')

  // Labels at the board origin clamp by their own rendered size. They stay
  // fully reachable even when both chosen connector sides sit on x=0 or y=0.
  await toolbar.getByRole('button', { name: 'Add card' }).click()
  await expect(cards).toHaveCount(2)
  await cards.nth(1).locator('.canvas-card-title').fill('Edge')
  const surface = h.page.locator('.canvas-surface')
  await surface.evaluate((element) => {
    element.scrollLeft = 0
    element.scrollTop = 0
  })
  const moveCardTo = async (index: number, x: number, y: number): Promise<void> => {
    const card = cards.nth(index)
    const moveHandle = card.locator('.canvas-card-move-handle')
    const [cardBounds, handleBounds, surfaceBounds] = await Promise.all([
      card.boundingBox(),
      moveHandle.boundingBox(),
      surface.boundingBox()
    ])
    expect(cardBounds).not.toBeNull()
    expect(handleBounds).not.toBeNull()
    expect(surfaceBounds).not.toBeNull()
    const handleX = handleBounds!.x + handleBounds!.width / 2
    const handleY = handleBounds!.y + handleBounds!.height / 2
    const offsetX = handleX - cardBounds!.x
    const offsetY = handleY - cardBounds!.y
    await h.page.mouse.move(handleX, handleY)
    await h.page.mouse.down()
    await h.page.mouse.move(surfaceBounds!.x + x + offsetX, surfaceBounds!.y + y + offsetY, {
      steps: 6
    })
    await h.page.mouse.up()
    await expect
      .poll(async () => {
        const style = await card.getAttribute('style')
        return {
          x: Number.parseFloat(style?.match(/left:\s*([\d.]+)/)?.[1] ?? '-1'),
          y: Number.parseFloat(style?.match(/top:\s*([\d.]+)/)?.[1] ?? '-1')
        }
      })
      .toEqual({ x, y })
  }

  await moveCardTo(0, 0, 0)
  await moveCardTo(1, 0, 200)
  const surfaceBounds = await surface.boundingBox()
  const leftSourceBounds = await cards.nth(0).locator('.canvas-connector-handle.left').boundingBox()
  const leftTargetBounds = await cards.nth(1).locator('.canvas-connector-handle.left').boundingBox()
  expect(surfaceBounds).not.toBeNull()
  expect(leftSourceBounds).not.toBeNull()
  expect(leftTargetBounds).not.toBeNull()
  expect(leftSourceBounds!.x).toBeGreaterThanOrEqual(surfaceBounds!.x)
  expect(leftTargetBounds!.x).toBeGreaterThanOrEqual(surfaceBounds!.x)
  await h.page.mouse.move(
    leftSourceBounds!.x + leftSourceBounds!.width / 2,
    leftSourceBounds!.y + leftSourceBounds!.height / 2
  )
  await h.page.mouse.down()
  await h.page.mouse.move(
    leftTargetBounds!.x + leftTargetBounds!.width / 2,
    leftTargetBounds!.y + leftTargetBounds!.height / 2,
    { steps: 6 }
  )
  await h.page.mouse.up()
  const edgeLabel = h.page.getByRole('textbox', {
    name: 'Label connector between Effect and Edge'
  })
  await expect(edgeLabel).toBeVisible()
  const leftEditorBounds = await edgeLabel
    .locator('xpath=ancestor::*[contains(@class, "canvas-connector-label-editor")]')
    .boundingBox()
  expect(leftEditorBounds).not.toBeNull()
  expect(leftEditorBounds!.x).toBeGreaterThanOrEqual(surfaceBounds!.x)
  await h.page.getByRole('button', { name: 'Delete connector' }).click()

  await moveCardTo(1, 300, 0)
  const topSourceBounds = await cards.nth(0).locator('.canvas-connector-handle.top').boundingBox()
  const topTargetBounds = await cards.nth(1).locator('.canvas-connector-handle.top').boundingBox()
  expect(topSourceBounds).not.toBeNull()
  expect(topTargetBounds).not.toBeNull()
  expect(topSourceBounds!.y).toBeGreaterThanOrEqual(surfaceBounds!.y)
  expect(topTargetBounds!.y).toBeGreaterThanOrEqual(surfaceBounds!.y)
  await h.page.mouse.move(
    topSourceBounds!.x + topSourceBounds!.width / 2,
    topSourceBounds!.y + topSourceBounds!.height / 2
  )
  await h.page.mouse.down()
  await h.page.mouse.move(
    topTargetBounds!.x + topTargetBounds!.width / 2,
    topTargetBounds!.y + topTargetBounds!.height / 2,
    { steps: 6 }
  )
  await h.page.mouse.up()
  await expect(edgeLabel).toBeVisible()
  const topEditorBounds = await edgeLabel
    .locator('xpath=ancestor::*[contains(@class, "canvas-connector-label-editor")]')
    .boundingBox()
  expect(topEditorBounds).not.toBeNull()
  expect(topEditorBounds!.y).toBeGreaterThanOrEqual(surfaceBounds!.y)
  await h.page.getByRole('button', { name: 'Delete connector' }).click()

  await h.close()
})
