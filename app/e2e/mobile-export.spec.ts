import { test, expect, type Page } from '@playwright/test'
import { dismissTour, type Book } from './harness'
import { mobileHost } from './mobileHarness'

async function seed(page: Page, root: string): Promise<Book> {
  const book = await page.evaluate(async (directory) => {
    const rpc = window.novalistRpc
    await rpc.request('project/create', [directory, 'Export test', 'Book'])
    for (const title of ['Keep', 'Leave out']) {
      const s = await rpc.request<Book>('project/createChapter', [title])
      const chapter = s.chapters.at(-1)!
      const withScene = await rpc.request<Book>('project/createScene', [chapter.guid, title])
      const scene = withScene.chapters.at(-1)!.scenes[0]
      await rpc.request('scenes/write', [chapter.guid, scene.id,
        `<p>${title === 'Keep' ? 'KEPT_PROSE' : 'OMITTED_PROSE'}</p>`, title])
    }
    const state = await rpc.request<Book>('project/getState')
    window.novalistStores.project.getState().applyState(state as never)
    return state
  }, root)
  await dismissTour(page)
  await page.evaluate(() => {
    (window as unknown as { __novalistTab(key: string): void }).__novalistTab(
      window.novalistStores.shell.getState().mobileLayout === 'tablet' ? 'write' : 'manuscript')
  })
  await expect(page.locator('.binder-chapter-title').first()).toHaveText('Keep')
  return book
}

for (const layout of ['phone', 'tablet'] as const) {
  test(`mobile export: ${layout} chapter selection reaches the native share boundary`, async ({ page }) => {
    await page.setViewportSize(layout === 'phone' ? { width: 393, height: 852 } : { width: 1180, height: 820 })
    const h = await mobileHost(page, layout)
    try {
      await seed(page, h.root)
      await page.locator('.binder-chapter').first().getByRole('button', { name: 'Chapters', exact: true }).click()
      await page.getByRole('menuitem', { name: 'Export chapter…' }).click()
      await expect(page.locator('.export-view')).toBeVisible()
      await expect(page.locator('.export-chapters input:checked')).toHaveCount(1)
      await expect(page.locator('.export-chapters').getByLabel('Keep', { exact: true })).toBeChecked()
      if (layout === 'tablet') {
        // Narrow Split View remounts the phone shell. It must not silently turn
        // a one-chapter export into an export of the entire book.
        for (const mode of ['phone', 'tablet']) {
          await page.setViewportSize(mode === 'phone' ? { width: 500, height: 820 } : { width: 1180, height: 820 })
          await page.evaluate((value) => {
            (window as unknown as { __novalistLayout(mode: string): void }).__novalistLayout(value)
          }, mode)
          await expect(page.locator(mode === 'phone' ? '.mobile-shell' : '.tablet-shell')).toBeVisible()
          await expect(page.locator('.export-chapters input:checked')).toHaveCount(1)
        }
      }
      await page.selectOption('#export-format', 'Markdown')
      await page.locator('.export-run').click()
      await expect(page.locator('.export-result')).toHaveText('Export complete.')
      expect(h.host.shared).toHaveLength(1)
      expect(h.host.shared[0].bytes.toString()).toContain('KEPT_PROSE')
      expect(h.host.shared[0].bytes.toString()).not.toContain('OMITTED_PROSE')
      await expect.poll(() => h.host.released).toEqual([h.host.shared[0].path])
      expect(await page.locator('.export-view').evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true)

      // Return via the real native navigation callback; the Write tab must work
      // even though it remained highlighted while Export was open.
      await page.evaluate((mode) => {
        (window as unknown as { __novalistTab(key: string): void }).__novalistTab(mode === 'phone' ? 'manuscript' : 'write')
      }, layout)
      await page.locator('.binder-mobile-actions').getByRole('button', { name: 'Export', exact: true }).click()
      await expect(page.locator('.export-chapters input:checked')).toHaveCount(2)
    } finally { await h.close() }
  })
}

test('mobile export: cancel, retry, and failures never report a successful save', async ({ page }) => {
  const h = await mobileHost(page, 'phone')
  try {
    await seed(page, h.root)
    await page.locator('.binder-mobile-actions').getByRole('button', { name: 'Export', exact: true }).click()
    await page.selectOption('#export-format', 'Markdown')
    h.host.outcome = 'cancel'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export cancelled.')
    await expect(page.locator('.export-run')).toBeEnabled()
    h.host.outcome = 'error'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    h.host.outcome = 'success'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export complete.')
    await expect.poll(() => h.host.released.length).toBe(3)
    h.host.failSave = true
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    expect(h.host.shared).toHaveLength(3)
    h.host.failSave = false
    h.host.failExport = true
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    await expect.poll(() => h.host.released.length).toBe(4)
    expect(h.host.shared).toHaveLength(3)
  } finally { await h.close() }
})

test('Android sharing reports a handoff without claiming the recipient saved the export', async ({ page }) => {
  const h = await mobileHost(page, 'phone', 'android')
  try {
    await seed(page, h.root)
    await page.locator('.binder-mobile-actions').getByRole('button', { name: 'Export', exact: true }).click()
    await page.selectOption('#export-format', 'Markdown')
    h.host.outcome = 'handoff'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export prepared. Finish saving or sharing it in the app you selected.')
    await expect.poll(() => h.host.released.length).toBe(1)
  } finally { await h.close() }
})
