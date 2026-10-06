import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'
import { holdMotion } from './motion'

test('map popups animate without delaying actions or restarting while filtering', async () => {
  const h = await launchApp('nl-map-popup-motion-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, {})
    await dismissTour(h.page)
    const map = await h.rpc<{ id: string }>('maps/create', ['Coast'])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))
    const iframe = h.page.locator('iframe[title="map"]')
    await expect(iframe).toBeVisible()
    const frame = await (await iframe.elementHandle())?.contentFrame()
    if (!frame) throw new Error('The map frame must be ready')
    await frame.waitForFunction((id) => {
      const engine = window as unknown as { getMapData?(): string; addPinAtCenter?: unknown }
      return engine.getMapData && engine.addPinAtCenter && JSON.parse(engine.getMapData()).id === id
    }, map.id)
    await frame.evaluate(() => {
      const map = window as unknown as { addPinAtCenter(label: string): string }
      map.addPinAtCenter('Harbor')
    })
    const marker = frame.locator('.nv-pin-marker')
    await expect(marker).toHaveCount(1)
    const motion = await holdMotion(frame, '.nv-ctx-menu, .bb-entity-pop')
    const entity = frame.getByPlaceholder('Type to search characters, locations, items, lore…')
    await entity.click()
    const picker = frame.locator('.bb-entity-pop')
    await expect(picker).toHaveCount(1)
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await entity.fill('Nobody')
    expect(await motion.evaluate(control => control.details())).toEqual([])
    await h.page.keyboard.press('Escape')
    await expect(picker).toHaveAttribute('inert', '')
    await expect(picker).toHaveAttribute('aria-hidden', 'true')
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await expect(picker).toHaveCount(0)

    await marker.click({ button: 'right' })
    const menu = frame.locator('.nv-ctx-menu')
    await expect(menu).toHaveCount(1)
    expect(await frame.evaluate(() => document.hasFocus())).toBe(true)
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await h.page.keyboard.press('Escape')
    await expect(menu).toHaveAttribute('inert', '')
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await expect(menu).toHaveCount(0)
    await marker.click({ button: 'right' })
    await expect(menu).toBeVisible()
    expect(await menu.evaluate(element => element.getAnimations().length)).toBe(0)
    await h.page.keyboard.press('Escape')
    await expect(menu).toHaveCount(0)

    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await marker.click({ button: 'right' })
    await motion.evaluate(control => control.finish())
    await menu.getByText('Delete', { exact: true }).click()
    await expect(marker).toHaveCount(0)
    await expect(menu).toHaveAttribute('inert', '')
    await motion.evaluate(control => control.finish())
    await expect(menu).toHaveCount(0)
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})
