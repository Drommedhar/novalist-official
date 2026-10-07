import { expect, test } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

for (const deviceScaleFactor of [1, 2, 3]) {
  test(`3D map canvas fits its viewport at ${deviceScaleFactor}x pixel density`, async ({ browser }) => {
    const context = await browser.newContext({ deviceScaleFactor, viewport: { width: 550, height: 292 } })
    try {
      const page = await context.newPage()
      await page.route('http://map.test/**', async route => {
        const path = new URL(route.request().url()).pathname
        await route.fulfill({
          body: readFileSync(resolve('src/renderer/public/map', path.slice(1))),
          contentType: path.endsWith('.html') ? 'text/html' : 'text/javascript'
        })
      })
      await page.goto('http://map.test/map.html')
      await page.evaluate(async () => {
        const asset = (name: string): string => new URL(name, location.href).href
        const THREE = await import(asset('three.webgpu.min.js'))
        const { sceneState } = await import(asset('map3d/scene-state.js'))
        const canvas = document.querySelector<HTMLCanvasElement>('#stage3d')!
        // Exercise the real renderer's drawing-buffer sizing and production
        // resize path without requiring a GPU device just to measure layout.
        sceneState.renderer = new THREE.WebGPURenderer({ canvas })
        sceneState.renderer.setPixelRatio(devicePixelRatio)
        sceneState.camera = new THREE.PerspectiveCamera()
        canvas.style.display = 'block'
      })
      for (const viewport of [{ width: 550, height: 292 }, { width: 292, height: 550 }]) {
        await page.setViewportSize(viewport)
        const actual = await page.evaluate(async () => {
          const { resize } = await import(new URL('map3d-bootstrap.js', location.href).href)
          resize()
          const canvas = document.querySelector<HTMLCanvasElement>('#stage3d')!
          const rect = canvas.getBoundingClientRect()
          return { width: rect.width, height: rect.height, bufferWidth: canvas.width, bufferHeight: canvas.height }
        })
        expect(actual).toEqual({
          ...viewport,
          bufferWidth: viewport.width * deviceScaleFactor,
          bufferHeight: viewport.height * deviceScaleFactor
        })
      }
    } finally {
      await context.close()
    }
  })
}
