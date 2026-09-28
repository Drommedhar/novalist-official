import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow } from './harness'

test('open shelves fill the workspace and connect filters, details, size, sorting and series', async () => {
  const h = await launchApp('nl-open-shelves-')
  try {
    for (const [project, ...books] of [
      ['The Silent Shadows', 'Frotschwur', 'Winter’s End'],
      ['The Glass Orchard', 'The Glass Orchard'],
      ['The Tidal House', 'A House of Salt', 'Low Water'],
      ['The Last Astronomer', 'The Last Astronomer'],
      ['Letters from June', 'Letters from June'],
      ['Elsewhere', 'The Atlas of Elsewhere'],
      ['Small Hours', 'Small Hours'],
      ['The Quiet Coast', 'The Quiet Coast']
    ]) {
      await h.rpc('project/create', [h.workDir, project, books[0]])
      for (const book of books.slice(1)) await h.rpc('project/createBook', [book])
    }
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await dismissTour(h.page)
    await resizeWindow(h, 1600, 1050)
    const cards = h.page.locator('.library-book')
    const inspector = h.page.getByRole('complementary', { name: 'Book details' })
    const frotschwur = h.page.getByRole('button', { name: 'Select Frotschwur — The Silent Shadows', exact: true })
    await expect(cards).toHaveCount(10)
    const widths = await h.page.evaluate(() => ({
      root: document.querySelector('.project-library')!.clientWidth,
      content: document.querySelector('.library-content')!.clientWidth,
      rows: document.querySelector('.library-shelves')!.clientWidth,
      height: document.querySelector('.project-library')!.clientHeight,
      contentHeight: document.querySelector('.library-content')!.clientHeight
    }))
    expect(widths.content).toBe(widths.root)
    expect(widths.rows).toBe(widths.root)
    expect(widths.contentHeight).toBe(widths.height)
    expect(widths.rows).toBeGreaterThan(1400)
    const positions = await cards.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top))
    expect(positions.filter(top => top === positions[0]).length).toBeGreaterThanOrEqual(5)
    await h.page.screenshot({ path: 'test-results/open-shelves-desktop.png' })

    // The details panel changes the grid width. A real double-click at the end
    // of a row must still open that book, even with time between mouse events.
    const endOfRow = cards.nth(positions.filter(top => top === positions[0]).length - 1)
    const targetName = await endOfRow.locator('.volume-name').textContent()
    const targetBox = (await endOfRow.locator('button').boundingBox())!
    const point = { x: targetBox.x + targetBox.width / 2, y: targetBox.y + targetBox.height / 2 }
    await h.page.mouse.click(point.x, point.y, { delay: 80 })
    await h.page.mouse.click(point.x, point.y, { clickCount: 2, delay: 80 })
    await expect.poll(() => h.page.evaluate(() => {
      const state = window.novalistStores.project.getState()
      return state.isLoaded ? state.books.find(book => book.id === state.activeBookId)?.name : null
    }), { timeout: 2000 }).toBe(targetName)
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())

    await frotschwur.click()
    await expect(inspector).toBeVisible()
    await expect(inspector.locator('.library-related button')).toHaveText(['Frotschwur', 'Winter’s End'])
    await expect(inspector.getByRole('button', { name: 'World Bible', exact: true })).toBeVisible()
    await expect(inspector.getByRole('button', { name: 'Series overview', exact: true })).toBeVisible()
    const palettes = await h.page.getByRole('button', { name: /^Select (Frotschwur|Winter’s End) — The Silent Shadows$/ })
      .locator('.volume-generated-cover').evaluateAll(elements => elements.map(element => getComputedStyle(element).backgroundColor))
    expect(palettes[0]).toBe(palettes[1])
    await h.page.screenshot({ path: 'test-results/open-shelves-details.png' })
    await h.page.keyboard.press('Escape')
    await expect(frotschwur).toBeFocused()
    await expect(inspector).toHaveCount(0)

    const before = (await cards.first().locator('.volume-cover').boundingBox())!.width
    await h.page.getByRole('slider', { name: 'Cover size' }).fill('70')
    const after = (await cards.first().locator('.volume-cover').boundingBox())!.width
    expect(after).toBeLessThan(before)
    await h.page.getByRole('combobox', { name: 'Sort books' }).selectOption('title')
    await expect(h.page.locator('.volume-name').first()).toHaveText('A House of Salt')
    await expect(h.page.locator('.volume-name').nth(1)).toHaveText('Low Water')
    await h.page.reload()
    await expect(h.page.getByRole('slider', { name: 'Cover size' })).toHaveValue('70')
    await expect(h.page.getByRole('combobox', { name: 'Sort books' })).toHaveValue('title')
    await h.page.getByRole('slider', { name: 'Cover size' }).fill('100')

    await h.page.getByRole('button', { name: 'Add shelf', exact: true }).click()
    await h.page.getByRole('textbox', { name: 'Shelf name', exact: true }).fill('Writing')
    await h.page.getByRole('button', { name: 'Create shelf', exact: true }).click()
    await frotschwur.click()
    await inspector.getByRole('combobox', { name: 'Shelf for The Silent Shadows' }).selectOption({ label: 'Writing' })
    await h.page.getByRole('navigation', { name: 'Filter shelves' }).getByRole('button', { name: /^Writing/ }).click()
    await expect(cards).toHaveCount(2)
    await h.page.keyboard.press('/')
    const search = h.page.getByRole('searchbox', { name: 'Find a project or book…' })
    await expect(search).toBeFocused()
    await search.fill('Winter')
    await expect(cards).toHaveCount(1)
    await search.fill('')
    await h.page.getByRole('navigation', { name: 'Filter shelves' }).getByRole('button', { name: /^All/ }).click()
    await expect(cards).toHaveCount(10)

    for (const width of [1024, 768, 393]) {
      await resizeWindow(h, width, 850)
      expect(await h.page.locator('.project-library').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
      expect(await h.page.locator('.library-shelves').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
      await frotschwur.click()
      await expect(inspector.getByRole('button', { name: 'Close book details' })).toBeInViewport()
      await inspector.getByRole('button', { name: 'Close book details' }).click()
    }
    await h.page.locator('.library-shelves').evaluate(element => { element.scrollTop = 0 })
    await h.page.screenshot({ path: 'test-results/open-shelves-narrow.png' })
    await resizeWindow(h, 1600, 1050)
    await frotschwur.click()
    await inspector.getByRole('button', { name: 'Series overview' }).click()
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.shell.getState().mainView)).toBe('series')
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().projectName)).toBe('The Silent Shadows')
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await frotschwur.click()
    await inspector.getByRole('button', { name: 'World Bible', exact: true }).click()
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.shell.getState().mainView)).toBe('codex')
  } finally { await h.close() }
})
