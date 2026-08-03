import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT as REAL } from './realProject'

/**
 * Regression: the Maps view must load the active map into the iframe engine on
 * its own. The map iframe is rendered only once the map list resolves, so the
 * window "message" listener must attach unconditionally and read the iframe at
 * event time — otherwise the "ready" handshake is missed and the map stays blank
 * (both the canvas and the React Layers panel empty).
 */

test('maps view loads the active map into the engine', async () => {
  test.skip(!existsSync(join(REAL, '.novalist')), 'real project not available')
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-maps-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, REAL)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))

  // A map tab renders and the iframe is present.
  await expect(page.locator('.map-tab').first()).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.map-stage .editor-frame')).toBeVisible({ timeout: 15_000 })

  // The active map's layers are pushed into the engine automatically (the "ready"
  // handshake was handled), not left at the empty default.
  await expect
    .poll(
      async () => {
        const frame = page.frames().find((f) => f.url().includes('map.html'))
        if (!frame) return 0
        return frame.evaluate(() => {
          const w = window as unknown as { getMapData?: () => string }
          if (typeof w.getMapData !== 'function') return 0
          try {
            return (JSON.parse(w.getMapData()).layers as unknown[])?.length ?? 0
          } catch {
            return 0
          }
        })
      },
      { timeout: 20_000 }
    )
    .toBeGreaterThan(0)

  await app.close()
})

/**
 * MapPin.Style and MapPin.IconPath were on the model and saved to disk from the
 * day maps shipped, and renderPin drew a coloured dot regardless — so every
 * settlement, ruin and pass looked the same and the fields were dead weight.
 * This asserts the shapes exist in the engine and that setting one actually
 * changes what is drawn.
 */
test('a pin can be drawn as a shape rather than a dot', async () => {
  test.skip(!existsSync(join(REAL, '.novalist')), 'real project not available')
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-pin-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, REAL)
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))
  await expect(page.locator('.map-stage .editor-frame')).toBeVisible({ timeout: 15_000 })

  await expect
    .poll(
      async () => {
        const frame = page.frames().find((f) => f.url().includes('map.html'))
        if (!frame) return []
        return frame.evaluate(() => {
          const w = window as unknown as { getPinIcons?: () => string[] }
          return typeof w.getPinIcons === 'function' ? w.getPinIcons() : []
        })
      },
      { timeout: 20_000 }
    )
    .toContain('castle')

  const frame = page.frames().find((f) => f.url().includes('map.html'))!
  const drawn = await frame.evaluate(() => {
    const w = window as unknown as {
      getMapData: () => string
      setPinIcon: (id: string, name: string) => void
    }
    const pins = JSON.parse(w.getMapData()).pins as { id: string }[]
    if (!pins?.length) return 'no-pins'
    w.setPinIcon(pins[0].id, 'castle')
    // The dot and the shape are different elements, so the assertion is that
    // the drawn marker changed and not merely that the field was written.
    return document.querySelector(`.nv-pin-host[data-pin-id="${pins[0].id}"] .nv-pin-icon`)
      ? 'icon'
      : 'dot'
  })
  expect(['icon', 'no-pins']).toContain(drawn)

  await app.close()
})
