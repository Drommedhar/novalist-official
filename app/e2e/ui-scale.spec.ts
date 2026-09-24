import { test, expect } from '@playwright/test'
import { launchApp } from './harness'

test('macOS window buttons keep their space at every interface size', async () => {
  test.skip(process.platform !== 'darwin', 'macOS overlays native window buttons on the toolbar')
  const h = await launchApp('nl-ui-scale-')
  try {
    const openAppearance = async (): Promise<void> => {
      await h.page.evaluate(() =>
        window.novalistStores.shell.getState().openSettings('settings/appearance/interface-scale')
      )
      await expect(h.page.locator('#set-ui-scale')).toBeVisible()
    }
    const zoomFactor = (): Promise<number> => h.app.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0].webContents.getZoomFactor()
    )
    const checkGeometry = async (percent: number): Promise<void> => {
      const factor = percent / 100
      await expect.poll(zoomFactor).toBeCloseTo(factor, 2)
      const geometry = await h.page.evaluate((zoom) => {
        const toolbar = document.querySelector('.toolbar-mac')!.getBoundingClientRect()
        const title = document.querySelector('.toolbar-book')!.getBoundingClientRect()
        const body = document.querySelector('.shell-body')!.getBoundingClientRect()
        const rail = document.querySelector('.mode-rail')!.getBoundingClientRect()
        // Native buttons use window points, while DOM bounds use zoomed CSS
        // pixels. devicePixelRatio also includes Retina scaling and cannot be
        // used to compare these two coordinate systems.
        return {
          titleLeft: title.left * zoom,
          toolbarHeight: toolbar.height * zoom,
          bodyTop: body.top * zoom,
          railWidth: rail.width * zoom
        }
      }, factor)
      expect(geometry.titleLeft, `title clearance at ${percent}%`).toBeCloseTo(78, 0)
      expect(geometry.toolbarHeight, `toolbar height at ${percent}%`)
        .toBeCloseTo(Math.max(38, 38 * factor), 0)
      expect(geometry.bodyTop).toBeCloseTo(geometry.toolbarHeight, 0)
      // The rest of the interface must still follow the requested scale.
      expect(geometry.railWidth).toBeCloseTo(84 * factor, 0)
    }

    await openAppearance()
    const scales = await h.page.locator('#set-ui-scale option').evaluateAll((options) =>
      options.map((option) => Number((option as HTMLOptionElement).value))
    )
    for (const percent of scales) {
      await h.page.selectOption('#set-ui-scale', String(percent))
      await checkGeometry(percent)
    }

    // The saved scale must reserve the same native space on startup too.
    await h.page.selectOption('#set-ui-scale', '75')
    await checkGeometry(75)
    await h.page.reload()
    await expect(h.page.locator('.toolbar-mac')).toBeVisible()
    await checkGeometry(75)
    await openAppearance()
    await expect(h.page.locator('#set-ui-scale')).toHaveValue('75')
    await h.page.getByRole('button', { name: 'Reset to 100%' }).click()
    await checkGeometry(100)
  } finally {
    await h.close()
  }
})
