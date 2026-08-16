import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

/**
 * Looking around in 3D, and the 2D editor getting out of the way.
 *
 * The camera reads the mouse through a pointer lock. The map runs in a
 * sandboxed iframe, and a sandbox without `allow-pointer-lock` refuses the
 * request outright - so clicking into the world and dragging did nothing at
 * all, and nothing told the writer why. The failure is silent by construction:
 * the promise rejects inside the iframe and the camera simply never turns.
 *
 * The other half is what was left on top of the world: the drawing rail and the
 * ruler are painted over the map, so in 3D they sat greyed out across a
 * landscape they cannot act on, with the measure bar landing over the sky
 * controls. And the bottom bar went on telling the writer to click an element
 * and use the ribbon while they were standing in a field.
 */
test('3D takes the mouse, and the 2D editor gets out of the way', async () => {
  test.setTimeout(300_000)
  const h = await launchApp('nl-map3d-')
  const page = h.page

  await seedBook(h, { One: ['A'] })
  await dismissTour(page)
  // A map with land on it. An empty map has nothing for the 3D view to build a
  // world out of, so it never enters and this would skip itself into being
  // useless - which is exactly what it did until somebody pointed it out.
  const created = await h.rpc<{ id: string }>('maps/create', ['Test map'])
  await h.rpc('maps/generateTerrain', [created.id, 7, 1200, 900])
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))

  const frame = page.locator('iframe[title="map"]')
  await expect(frame).toBeVisible({ timeout: 30_000 })

  // In 2D both are there, so their absence below means something.
  await expect(page.locator('.map-toolrail')).toHaveCount(1)
  await expect(page.locator('.map-measure-bar')).toHaveCount(1)

  await page.getByTitle('3D', { exact: true }).click()

  // Building the scene needs a working GPU adapter. A machine without one is
  // not a failure of this behaviour, so say so rather than fail on it.
  const entered = await expect
    .poll(
      () =>
        frame.evaluate(
          (f: HTMLIFrameElement) =>
            ((f.contentWindow as unknown as { Map3D?: { isActive(): boolean } })?.Map3D?.isActive() ??
              false) === true
        ),
      { timeout: 120_000 }
    )
    .toBe(true)
    .then(() => true)
    .catch(() => false)
  test.skip(!entered, '3D view could not start on this machine')

  // Nothing 2D is left painted over the world. `active` is set at the top of
  // enter() and the host is told at the bottom, with the whole scene build in
  // between - thousands of trees on a generated map - so this waits on the
  // same budget the loading overlay does rather than on a sample.
  await expect(page.locator('.map-toolrail')).toHaveCount(0, { timeout: 120_000 })
  await expect(page.locator('.map-measure-bar')).toHaveCount(0, { timeout: 10_000 })

  // Clicking the world takes the pointer, which is what turning the camera
  // depends on. Without allow-pointer-lock this stays null for ever.
  const box = (await frame.boundingBox())!
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  await expect
    .poll(
      () =>
        frame.evaluate(
          (f: HTMLIFrameElement) => f.contentDocument?.pointerLockElement?.tagName ?? null
        ),
      { timeout: 15_000 }
    )
    .toBe('CANVAS')

  // And the bar says how to fly rather than how to edit a drawing.
  const bar = page.frameLocator('iframe[title="map"]').locator('#nv-bottom-bar')
  await expect(bar).toContainText('3D view')
  await expect(bar).toContainText('Esc')

  await h.close()
})
