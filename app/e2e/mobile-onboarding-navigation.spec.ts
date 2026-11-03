import { expect, test, type Page } from '@playwright/test'
import { dismissTour, type Book } from './harness'
import { mobileHost } from './mobileHarness'

async function createProject(page: Page, root: string): Promise<Book> {
  return page.evaluate(async directory => {
    const rpc = window.novalistRpc
    await rpc.request('project/create', [directory, 'Mobile navigation', 'Book'])
    const chapter = await rpc.request<Book>('project/createChapter', ['Chapter'])
    const state = await rpc.request<Book>('project/createScene', [chapter.chapters[0].guid, 'Scene'])
    window.novalistStores.project.getState().applyState(state as never)
    return state
  }, root)
}

test('Android tour clears the native sidebar and remains usable after rotation', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 })
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    await createProject(page, h.root)
    await page.evaluate(() => {
      const style = document.documentElement.style
      style.setProperty('--nl-mobile-sidebar-w', '64px')
      style.setProperty('--nl-mobile-tabbar-h', '0px')
      style.setProperty('--nl-safe-top', '24px')
      style.setProperty('--nl-safe-bottom', '24px')
      style.setProperty('--nl-safe-left', '12px')
      style.setProperty('--nl-safe-right', '12px')
    })
    const tour = page.getByRole('dialog', { name: 'Take the tour' })
    await expect(tour).toBeVisible()
    for (const viewport of [{ width: 800, height: 1280 }, { width: 860, height: 240 }]) {
      await page.setViewportSize(viewport)
      const bounds = await tour.evaluate(element => {
        const rect = element.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom,
          clientWidth: element.clientWidth, scrollWidth: element.scrollWidth }
      })
      expect(bounds.left).toBeGreaterThanOrEqual(76)
      expect(bounds.right).toBeLessThanOrEqual(viewport.width - 12)
      expect(bounds.top).toBeGreaterThanOrEqual(24)
      expect(bounds.bottom).toBeLessThanOrEqual(viewport.height - 24)
      expect(bounds.scrollWidth).toBeLessThanOrEqual(bounds.clientWidth)
    }
    await tour.getByRole('button', { name: 'Next', exact: true }).click()
    await expect(tour).toContainText('2 of')
    await tour.getByRole('button', { name: 'Back', exact: true }).click()
    await expect(tour).toContainText('1 of')
    await tour.getByRole('button', { name: 'Skip', exact: true }).click()
    await expect(tour).toBeHidden()
  } finally { await h.close() }
})

test('Android tablet onboarding recommends landscape while in portrait', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 })
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    await createProject(page, h.root)
    const hint = page.getByText('For more room to write and plan, turn your tablet to landscape. Portrait also works.')
    await expect(hint).toBeVisible()
    await page.setViewportSize({ width: 1280, height: 800 })
    await expect(hint).toBeHidden()
  } finally { await h.close() }
})

for (const layout of ['phone', 'tablet'] as const) {
  test(`Android ${layout} has a labeled Library button that saves the open scene`, async ({ page }) => {
    await page.setViewportSize(layout === 'phone' ? { width: 393, height: 852 } : { width: 800, height: 1280 })
    const h = await mobileHost(page, layout, 'android')
    try {
      const book = await createProject(page, h.root)
      await dismissTour(page)
      const header = page.locator(layout === 'phone' ? '.mobile-topbar' : '.tablet-topbar')
      const library = header.getByRole('button', { name: 'Library', exact: true })
      await expect(library).toHaveText('Library')
      await expect(library).toBeInViewport()
      await page.evaluate(async chapter => {
        window.__novalistTab?.(window.novalistStores.shell.getState().mobileLayout === 'tablet' ? 'write' : 'manuscript')
        await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
      }, book.chapters[0])
      const editor = page.frameLocator('.editor-frame').locator('#editor')
      await expect(editor).toBeVisible()
      await editor.fill('A scene saved before returning to the library.')
      const path = await page.evaluate(() => window.novalistStores.project.getState().projectPath)
      await library.click()
      await expect(page.locator('.project-library')).toBeVisible()
      await expect(header).toBeHidden()
      const saved = await page.evaluate(async ({ path, chapter }) => {
        await window.novalistRpc.request('project/open', [path])
        return window.novalistRpc.request<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])
      }, { path, chapter: book.chapters[0] })
      expect(saved.html).toContain('A scene saved before returning to the library.')
    } finally { await h.close() }
  })
}
