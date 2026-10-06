import { test, expect, type Locator } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'
import type { MapWindow } from '../src/renderer/src/views/maps/mapModel'

type DeviceLifetime = { created: number; destroyed: number }
type MapProbeWindow = MapWindow & { mapGpuLifetime: DeviceLifetime }

async function requireWebGpu(frame: Locator): Promise<void> {
  // The map UI can be ready before its 3D module finishes loading.
  await expect.poll(() => frame.evaluate((element: HTMLIFrameElement) =>
    Boolean((element.contentWindow as MapWindow | null)?.Map3D)
  ), { timeout: 30_000 }).toBe(true)
  const hasAdapter = await frame.evaluate(async (element: HTMLIFrameElement) => {
    type ProbeDevice = { destroy(): void }
    type ProbeAdapter = { requestDevice(descriptor?: unknown): Promise<ProbeDevice> }
    const navigator = element.contentWindow?.navigator as
      (Navigator & { gpu?: { requestAdapter(): Promise<ProbeAdapter | null> } }) | undefined
    const adapter = await navigator?.gpu?.requestAdapter()
    if (!adapter) return false
    const lifetime: DeviceLifetime = { created: 0, destroyed: 0 }
    ;(element.contentWindow as MapProbeWindow).mapGpuLifetime = lifetime
    const prototype = Object.getPrototypeOf(adapter) as ProbeAdapter
    const requestDevice = prototype.requestDevice
    prototype.requestDevice = async function (descriptor) {
      const device = await requestDevice.call(this, descriptor)
      lifetime.created++
      const destroy = device.destroy.bind(device)
      device.destroy = () => {
        lifetime.destroyed++
        destroy()
      }
      return device
    }
    return true
  })
  test.skip(!hasAdapter, 'No WebGPU adapter is available on this machine')
}

test('3D takes the mouse and releases resources across repeated entry', async () => {
  test.setTimeout(300_000)
  const h = await launchApp('nl-map3d-')
  const page = h.page
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(page)
    const created = await h.rpc<{ id: string }>('maps/create', ['Test map'])
    await h.rpc('maps/generateTerrain', [created.id, 7, 1200, 900])
    await page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))
    const frame = page.locator('iframe[title="map"]')
    await expect(frame).toBeVisible({ timeout: 30_000 })
    await requireWebGpu(frame)

    for (let cycle = 0; cycle < 2; cycle++) {
      await expect(page.locator('.map-toolrail')).toHaveCount(1)
      await expect(page.locator('.map-measure-bar')).toHaveCount(1)
      await page.getByTitle('3D', { exact: true }).click()
      await expect.poll(() => frame.evaluate((element: HTMLIFrameElement) =>
        (element.contentWindow as MapWindow | null)?.Map3D?.isActive() ?? false
      ), { timeout: 120_000 }).toBe(true)

      // The host receives map3dEntered only after scene construction finishes.
      await expect(page.locator('.map-toolrail')).toHaveCount(0, { timeout: 120_000 })
      await expect(page.locator('.map-measure-bar')).toHaveCount(0)
      await expect(page.frameLocator('iframe[title="map"]').locator('#stage3d')).toBeVisible()
      const box = await frame.boundingBox()
      if (!box) throw new Error('Map frame has no visible bounds')
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
      await expect.poll(() => frame.evaluate((element: HTMLIFrameElement) =>
        element.contentDocument?.pointerLockElement?.tagName ?? null
      )).toBe('CANVAS')

      const bar = page.frameLocator('iframe[title="map"]').locator('#nv-bottom-bar')
      await expect(bar).toContainText('3D view')
      await expect(bar).toContainText('Esc')
      await frame.screenshot({ path: test.info().outputPath('map-3d-cycle-' + cycle + '.png') })

      await frame.evaluate((element: HTMLIFrameElement) => element.contentDocument?.exitPointerLock())
      await page.getByTitle('3D', { exact: true }).click()
      await expect.poll(() => frame.evaluate((element: HTMLIFrameElement) =>
        (element.contentWindow as MapWindow | null)?.Map3D?.isActive() ?? true
      )).toBe(false)
      await expect(page.frameLocator('iframe[title="map"]').locator('#stage3d')).toBeHidden()
      await expect(page.locator('.map-toolrail')).toHaveCount(1)
      await expect.poll(() => frame.evaluate((element: HTMLIFrameElement) =>
        (element.contentWindow as MapProbeWindow).mapGpuLifetime
      )).toEqual({ created: cycle + 1, destroyed: cycle + 1 })
    }
  } finally {
    await h.close()
  }
})
