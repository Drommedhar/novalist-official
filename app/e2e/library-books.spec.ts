import { test, expect } from '@playwright/test'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

test('each book has its own cover and opens directly, including on a narrow shelf', async () => {
  const h = await launchApp('nl-library-books-')
  try {
    await seedBook(h, { Chapter: ['Scene'] }, 'Shared World')
    await dismissTour(h.page)
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=', 'base64')
    const firstCover = join(h.workDir, 'first.png')
    const secondCover = join(h.workDir, 'second.png')
    writeFileSync(firstCover, png)
    writeFileSync(secondCover, Buffer.concat([png, Buffer.from('second')]))
    await h.rpc('dashboard/setCover', [firstCover])
    const created = await h.rpc<{ books: { id: string; name: string }[] }>('project/createBook', ['Second Book'])
    const secondId = created.books.find((book) => book.name === 'Second Book')!.id
    await h.rpc('project/switchBook', [secondId])
    await h.rpc('dashboard/setCover', [secondCover])
    const third = await h.rpc<{ books: { id: string; name: string }[] }>('project/createBook', ['Uncovered Book'])
    await h.rpc('project/switchBook', [third.books.find((book) => book.name === 'Uncovered Book')!.id])
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())

    const books = h.page.locator('.library-book')
    await expect(books).toHaveCount(3)
    await expect(books.nth(0).locator('img')).toHaveAttribute('src', `data:image/png;base64,${png.toString('base64')}`)
    await expect(books.nth(1).locator('img')).toHaveAttribute('src', `data:image/png;base64,${Buffer.concat([png, Buffer.from('second')]).toString('base64')}`)
    await expect(books.nth(2).locator('img')).toHaveCount(0)
    await expect(books.nth(2).locator('.volume-title')).toHaveText('Uncovered Book')
    await expect(h.page.getByText('3 books · World Bible', { exact: true })).toBeVisible()

    // Equal book objects from another refresh must not rebuild the library.
    expect(await h.page.evaluate(async () => {
      const store = window.novalistStores.project
      const before = store.getState().recentProjects
      await store.getState().loadRecents()
      return before === store.getState().recentProjects
    })).toBe(true)

    await resizeWindow(h, 393, 852)
    await expect(books.nth(1)).toBeVisible()
    expect(await h.page.locator('.project-library').evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true)
    const firstBox = await books.nth(0).boundingBox()
    const secondBox = await books.nth(1).boundingBox()
    expect(firstBox!.y).toBe(secondBox!.y)
    expect(secondBox!.x).toBeGreaterThan(firstBox!.x)
    await h.page.screenshot({ path: 'test-results/library-books-narrow.png' })
    await resizeWindow(h, 1300, 900)
    await h.page.screenshot({ path: 'test-results/library-books.png' })

    await h.page.getByRole('textbox', { name: 'Find a project or book…' }).fill('Second Book')
    await expect(books).toHaveCount(1)
    await h.page.getByRole('button', { name: 'Open Second Book — Shared World', exact: true }).click()
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(secondId)
    expect((await h.rpc<{ activeBookId: string }>('project/getState')).activeBookId).toBe(secondId)
  } finally { await h.close() }
})
