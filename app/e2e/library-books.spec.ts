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
    await books.nth(2).locator('.start-recent-card').click()
    await expect(h.page.getByText('3 books · World Bible', { exact: true })).toBeVisible()
    // Selection holds the cover open after the pointer leaves, and switching
    // from a generated cover to uploaded artwork closes the previous book.
    for (const index of [2, 0]) {
      if (index === 0) await books.nth(index).locator('.start-recent-card').click()
      await expect(books.nth(index).locator('.start-recent-card')).toHaveAttribute('aria-pressed', 'true')
      await h.page.locator('.library-header').hover()
      await expect.poll(() => books.nth(index).locator('.volume-cover-front').evaluate(element =>
        element.getAnimations().length ? 'animating' : getComputedStyle(element).transform)).toContain('matrix3d')
    }
    await expect.poll(() => books.nth(2).locator('.volume-cover-front').evaluate(element => getComputedStyle(element).transform)).toBe('none')
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await expect.poll(() => books.nth(0).locator('.volume-cover-front').evaluate(element => getComputedStyle(element).transform)).toBe('none')
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await h.page.getByRole('button', { name: 'Close book details' }).click()
    await expect.poll(() => books.nth(0).locator('.volume-cover-front').evaluate(element => getComputedStyle(element).transform)).toBe('none')

    // Equal book objects from another refresh must not rebuild the library.
    expect(await h.page.evaluate(async () => {
      const store = window.novalistStores.project
      const before = store.getState().recentProjects
      await store.getState().loadRecents()
      return before === store.getState().recentProjects
    })).toBe(true)

    // Real artwork and generated artwork receive the same book treatment.
    const treatments = await books.locator('.volume-cover-front').evaluateAll((covers) => covers.map((cover) => ({
      shading: getComputedStyle(cover, '::after').backgroundImage,
      edge: getComputedStyle(cover, '::after').boxShadow,
      shadow: getComputedStyle(cover.parentElement!).boxShadow
    })))
    expect(treatments[0].shading).not.toBe('none')
    expect(treatments[0].edge).not.toBe('none')
    expect(treatments[0].shading).toEqual(treatments[2].shading)
    expect(treatments[0].edge).toEqual(treatments[2].edge)
    expect(treatments[0].shadow).not.toBe('none')
    expect(treatments[2].shadow).not.toBe('none')
    for (const index of [0, 2]) {
      const book = books.nth(index)
      const restingBox = await book.locator('.volume-cover').boundingBox()
      await book.locator('.start-recent-card').hover()
      await expect.poll(() => book.locator('.volume-cover-front').evaluate(element => getComputedStyle(element).transform)).toContain('matrix3d')
      expect(await book.locator('.volume-cover').boundingBox()).toEqual(restingBox)
    }
    await h.page.screenshot({ path: 'test-results/library-cover-opening.png', animations: 'disabled' })
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await expect.poll(() => books.nth(2).locator('.volume-cover-front').evaluate(element => getComputedStyle(element).transform)).toBe('none')
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })

    // Neither renaming nor rebuilding the renderer should change a book's
    // visual identity. The new title must still appear on the generated cover.
    const generated = books.nth(2).locator('.volume-generated-cover')
    const coverBefore = await generated.evaluate(element => ({
      palette: getComputedStyle(element).backgroundColor,
      artwork: element.querySelector('svg')!.innerHTML
    }))
    await books.nth(2).locator('.start-recent-card').dblclick()
    const uncoveredId = third.books.find(book => book.name === 'Uncovered Book')!.id
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(uncoveredId)
    await h.rpc('project/renameBook', [uncoveredId, 'Renamed Volume'])
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await h.page.reload()
    await expect(books.nth(2).locator('.volume-title')).toHaveText('Renamed Volume')
    expect(await generated.evaluate(element => ({
      palette: getComputedStyle(element).backgroundColor,
      artwork: element.querySelector('svg')!.innerHTML
    }))).toEqual(coverBefore)

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

    await h.page.getByRole('searchbox', { name: 'Find a project or book…' }).fill('Second Book')
    await expect(books).toHaveCount(1)
    await h.page.getByRole('button', { name: 'Select Second Book — Shared World', exact: true }).click()
    await h.page.getByRole('button', { name: 'Open Second Book — Shared World', exact: true }).click()
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(secondId)
    expect((await h.rpc<{ activeBookId: string }>('project/getState')).activeBookId).toBe(secondId)
  } finally { await h.close() }
})
