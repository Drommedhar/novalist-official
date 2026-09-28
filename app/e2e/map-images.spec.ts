import { expect, test } from '@playwright/test'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

// Issue #30: Gallery images decoded, but Maps treated the project-relative
// gallery URL as a renderer-relative URL for both previews and image sizing.
// Use real files and Electron's project protocol, without a personal project.
for (const fixture of [
  { name: 'flood map Küste 100%.png', mime: 'image/png' },
  { name: 'North America 2090.jpeg', mime: 'image/jpeg' }
]) {
  test(`map images preview, keep their dimensions and reload: ${fixture.mime}`, async () => {
    const h = await launchApp('nl-map-images-')
    try {
      await seedBook(h, {})
      await dismissTour(h.page)

      const dataUrl = await h.page.evaluate((mime) => {
        const canvas = document.createElement('canvas')
        canvas.width = 96
        canvas.height = 48
        const ctx = canvas.getContext('2d')!
        ctx.fillStyle = '#2468ac'
        ctx.fillRect(0, 0, canvas.width, canvas.height)
        return canvas.toDataURL(mime)
      }, fixture.mime)
      const source = join(h.workDir, fixture.name)
      writeFileSync(source, Buffer.from(dataUrl.split(',')[1], 'base64'))
      const imported = await h.rpc<{ path: string; url: string }>('gallery/import', [source])

      // Establish the reported contrast: this file already works in Gallery.
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('gallery'))
      const galleryImage = h.page.locator('.gallery-item img')
      await expect(galleryImage).toHaveCount(1)
      await expect.poll(() => galleryImage.evaluate((img: HTMLImageElement) =>
        [img.naturalWidth, img.naturalHeight]
      )).toEqual([96, 48])

      const map = await h.rpc<{ id: string }>('maps/create', ['Coast'])
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))
      await expect.poll(() => h.page.frameLocator('iframe[title="map"]').locator('body').evaluate(() => {
        const w = window as unknown as { getMapData?: () => string }
        return w.getMapData ? JSON.parse(w.getMapData()).id : null
      })).toBe(map.id)
      await h.page.getByTitle('Add an image to the active layer.', { exact: true }).click()
      const preview = h.page.locator('.map-image-choice img')
      await expect(preview).toHaveCount(1)
      // Soft so the unfixed run also exercises placement and catches its
      // independent broken probe (which silently falls back to 256 x 256).
      await expect.soft.poll(() => preview.evaluate((img: HTMLImageElement) =>
        [img.naturalWidth, img.naturalHeight]
      )).toEqual([96, 48])
      await h.page.locator('.map-image-choice').click()

      const placed = h.page.frameLocator('iframe[title="map"]').locator('.nv-image')
      await expect(placed).toHaveCount(1)
      await expect.poll(() => placed.evaluate((img: HTMLImageElement) =>
        [img.naturalWidth, img.naturalHeight]
      )).toEqual([96, 48])

      // Wait on the real autosave, and assert portable storage and the natural
      // aspect ratio, not just that a broken <img> element was inserted.
      const savedImages = async () => {
        const saved = await h.rpc<{ json: string }>('maps/load', [map.id])
        return ((JSON.parse(saved.json).layers[0]?.images ?? []) as {
          path: string; width: number; height: number
        }[]).map(({ path, width, height }) => ({ path, width, height }))
      }
      await expect.soft.poll(savedImages).toEqual([
        { path: imported.path, width: 96, height: 48 }
      ])

      // Remount the map so this decodes from the saved path and book base URL.
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('gallery'))
      await expect(h.page.locator('iframe[title="map"]')).toHaveCount(0)
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('maps'))
      await expect(placed).toHaveCount(1)
      await expect.poll(() => placed.evaluate((img: HTMLImageElement) =>
        [img.naturalWidth, img.naturalHeight]
      )).toEqual([96, 48])
    } finally {
      await h.close()
    }
  })
}
