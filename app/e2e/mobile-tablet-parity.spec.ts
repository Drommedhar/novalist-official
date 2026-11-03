import { test, expect } from '@playwright/test'
import { dismissTour, type Book } from './harness'
import { mobileHost } from './mobileHarness'

const destinations = [
  ['dashboard', '.dashboard'], ['write', 'iframe[title="editor"]'],
  ['manuscript', '.manuscript'], ['timeline', '.timeline'], ['plotGrid', '.plotgrid'],
  ['calendar', '.calendar'], ['relationships', '.relationships'], ['codex', '.codex'],
  ['wiki', '.wiki-view'], ['maps', '.mapsview'], ['research', '.research-view'],
  ['gallery', '.gallery'], ['export', '.export-view'], ['settings', '.settings-view']
] as const

for (const platform of ['darwin', 'android'] as const) {
  test(`${platform}: every tablet destination uses the shared iPad workspace`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 })
    const h = await mobileHost(page, 'tablet', platform)
    const errors: string[] = []
    try {
      expect(await page.evaluate(() => window.novalist.platform)).toBe(platform)
      const book = await page.evaluate(async root => {
        const rpc = window.novalistRpc
        await rpc.request('project/create', [root, 'Tablet check', 'Book'])
        const book = await rpc.request<Book>('project/createChapter', ['Chapter'])
        const state = await rpc.request<Book>('project/createScene', [book.chapters[0].guid, 'Scene'])
        window.novalistStores.project.getState().applyState(state as never)
        return state
      }, h.root)
      await dismissTour(page)
      await page.evaluate(() => {
        document.documentElement.style.setProperty('--nl-mobile-sidebar-w', '64px')
        document.documentElement.style.setProperty('--nl-mobile-tabbar-h', '0px')
      })
      await page.evaluate(async chapter => {
        await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
      }, book.chapters[0])
      page.on('pageerror', error => errors.push(error.message))

      for (const [destination, selector] of destinations) {
        await page.evaluate(key => window.__novalistTab?.(key), destination)
        await expect.poll(() => page.evaluate(() => window.novalistStores.shell.getState().mainView)).toBe(destination)
        await expect(page.locator('.tablet-topbar-title')).not.toBeEmpty()
        await expect(page.locator('.tablet-binder')).toBeVisible()
        const main = page.locator('.tablet-main .main-area:visible')
        await expect(main).toBeVisible()
        await expect(main.locator(selector).first()).toBeVisible()
        const geometry = await page.evaluate(() => {
          const binder = document.querySelector('.tablet-binder')!.getBoundingClientRect()
          const main = document.querySelector('.tablet-main')!.getBoundingClientRect()
          return { binderLeft: binder.left, binderRight: binder.right, mainLeft: main.left, mainRight: main.right, width: innerWidth }
        })
        expect(geometry.binderLeft).toBe(64)
        expect(geometry.binderRight).toBeLessThanOrEqual(geometry.mainLeft)
        expect(geometry.mainRight).toBeLessThanOrEqual(geometry.width)
        await expect(page.locator('.mobile-shell')).toHaveCount(0)
      }
      expect(errors).toEqual([])
    } finally { await h.close() }
  })
}
