import { expect, test, type Page } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'
import { holdMotion } from './motion'

async function openPalette(page: Page): Promise<void> {
  await page.evaluate(() => window.novalistStores.shell.getState().setCommandPaletteOpen(true))
  await expect(page.locator('.palette-card input')).toBeFocused()
}

test('dialogs animate both ways, become inert on close and survive immediate reopening', async () => {
  const h = await launchApp('nl-motion-dialog-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const motion = await holdMotion(h.page, '.palette-overlay')
    await openPalette(h.page)
    const palette = h.page.locator('.palette-overlay')
    const presence = h.page.locator('[data-motion-presence]').filter({ has: palette })
    const entering = await motion.evaluate(control => control.details())
    expect(entering.some(animation => animation.fading && animation.duration > 0 && animation.duration <= 200)).toBe(true)
    await motion.evaluate(control => control.finish())
    await palette.locator('input').fill('cancelled search')
    await h.page.keyboard.press('Escape')
    await expect(presence).toHaveAttribute('data-motion-presence', 'closing')
    await expect(presence).toHaveAttribute('inert', '')
    await expect(presence).toHaveAttribute('aria-hidden', 'true')
    await expect(h.page.getByRole('dialog')).toHaveCount(0)
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)

    await h.page.evaluate(() => window.novalistStores.shell.getState().setCommandPaletteOpen(true))
    await expect(presence).toHaveAttribute('data-motion-presence', 'open')
    await expect(presence).not.toHaveAttribute('inert', '')
    await expect(presence).not.toHaveAttribute('aria-hidden', 'true')
    await expect(palette.locator('input')).toBeFocused()
    await expect(palette.locator('input')).toHaveValue('')
    await motion.evaluate(control => control.finish())
    await expect(h.page.getByRole('dialog')).toBeVisible()
    await palette.locator('input').fill('settings')
    await h.page.keyboard.press('Escape')
    await expect(presence).toHaveAttribute('data-motion-presence', 'closing')
    await motion.evaluate(control => control.finish())
    await expect(palette).toHaveCount(0)
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})

test('reduced motion skips effects and immediately completes an exit already in progress', async () => {
  const h = await launchApp('nl-motion-reduced-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await openPalette(h.page)
    const palette = h.page.locator('.palette-overlay')
    expect(await palette.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
    await h.page.keyboard.press('Escape')
    await expect(palette).toHaveCount(0)

    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await openPalette(h.page)
    const motion = await holdMotion(h.page, '.palette-overlay')
    await h.page.keyboard.press('Escape')
    await expect(h.page.locator('[data-motion-presence="closing"]').filter({ has: palette })).toHaveCount(1)
    expect((await motion.evaluate(control => control.details())).length).toBeGreaterThan(0)
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await expect(palette).toHaveCount(0)
    await motion.evaluate(control => control.stop())
    await openPalette(h.page)
    await expect(h.page.getByRole('dialog')).toBeVisible()
    expect(await palette.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
  } finally {
    await h.close()
  }
})

test('view changes fade promptly and rapid navigation keeps the final route interactive', async () => {
  const h = await launchApp('nl-motion-views-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    const pane = h.page.locator('.pane-leaf')
    const original = await pane.evaluateHandle(element => element)
    const motion = await holdMotion(h.page, '.pane-leaf')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
    await expect(pane).toHaveAttribute('data-view', 'settings')
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading && animation.duration <= 200)).toBe(true)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('about'))
    await expect(pane).toHaveAttribute('data-view', 'about')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
    await expect(pane).toHaveAttribute('data-view', 'settings')
    expect(await original.evaluate(element => element.isConnected)).toBe(true)
    await motion.evaluate(control => control.finish())
    await h.page.locator('.settings-search').fill('font')
    await expect(h.page.locator('.settings-search')).toHaveValue('font')
    await motion.evaluate(control => control.stop())
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('about'))
    await expect(pane).toHaveAttribute('data-view', 'about')
    expect(await pane.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
  } finally {
    await h.close()
  }
})

test('panel transitions preserve the editor document and caret across desktop sizes', async () => {
  const h = await launchApp('nl-motion-panels-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.rpc('scenes/write', [chapter.guid, scene.id, '<p>The lantern lights the room.</p>', 'The lantern lights the room.'])
    await h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: scene.id })
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('The lantern lights the room.')
    const caret = await editor.evaluateHandle(element => {
      const text = element.querySelector('p')?.firstChild
      if (!text) throw new Error('The seeded scene must contain a paragraph')
      const selection = element.ownerDocument.getSelection()
      if (!selection) throw new Error('The editor document must provide a selection')
      const range = element.ownerDocument.createRange()
      range.setStart(text, 4)
      range.collapse(true)
      element.focus()
      selection.removeAllRanges()
      selection.addRange(range)
      return { element, text }
    })
    for (const width of [1420, 800]) {
      await h.page.setViewportSize({ width, height: 850 })
      await expect(h.page.locator('.shell')).toHaveAttribute('data-shell-capacity', width === 800 ? 'compact' : 'wide')
      await h.page.evaluate(() => window.novalistStores.shell.getState().toggleBinder())
      await expect(h.page.locator('.binder')).toHaveCount(0)
      if (width === 800) {
        const widths = await h.page.locator('.shell-main').evaluate(element => ({ main: element.getBoundingClientRect().width, viewport: document.documentElement.clientWidth, overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth }))
        expect(widths.main).toBeGreaterThanOrEqual(widths.viewport * 0.6 - 1)
        expect(widths.overflow).toBeLessThanOrEqual(0)
      }
      await h.page.evaluate(() => window.novalistStores.shell.getState().toggleBinder())
      await expect(h.page.locator('.binder')).toHaveCount(1)
      expect(await caret.evaluate(({ element, text }) => ({ connected: element.isConnected, sameAnchor: element.ownerDocument.getSelection()?.anchorNode === text, offset: element.ownerDocument.getSelection()?.anchorOffset }))).toEqual({ connected: true, sameAnchor: true, offset: 4 })
    }
    await h.page.keyboard.type('bright ')
    await expect(editor).toHaveText('The bright lantern lights the room.')
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleFocusMode())
    await expect(h.page.locator('.shell')).toHaveClass(/shell-focus/)
    await expect(h.page.locator('.workspace-sidebar')).toHaveCount(0)
    const focusWidth = await h.page.locator('.shell-main').evaluate(element => ({
      main: element.getBoundingClientRect().width,
      viewport: document.documentElement.clientWidth
    }))
    expect(focusWidth.main).toBeCloseTo(focusWidth.viewport, 0)
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleFocusMode())
    await h.page.setViewportSize({ width: 1420, height: 900 })
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
    await h.page.locator('#dock-synopsis').fill('Synopsis saved while the dock closes.')
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
    await expect(h.page.locator('.notes-dock')).toHaveCount(0)
    await expect.poll(() => h.rpc('scenes/getMeta', [chapter.guid, scene.id])).toMatchObject({ synopsis: 'Synopsis saved while the dock closes.' })
    await h.page.screenshot({ path: '../artifacts/ui-motion-tests/writing-settled.png', animations: 'disabled' })
  } finally {
    await h.close()
  }
})

test('inspector disclosures expand and collapse smoothly with native keyboard controls', async () => {
  const h = await launchApp('nl-motion-disclosure-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    await h.rpc('scenes/write', [book.chapters[0].guid, book.chapters[0].scenes[0].id, '<p>The lantern lights the room.</p>', 'The lantern lights the room.'])
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
    await h.page.locator('.inspector').evaluate(async element => {
      await Promise.allSettled(element.getAnimations({ subtree: true }).filter(animation => Number.isFinite(animation.effect?.getComputedTiming().endTime)).map(animation => animation.finished))
    })
    const details = h.page.locator('.inspector details.codex-match').last()
    const summary = details.locator('summary')
    await summary.focus()
    const collapsedHeight = await details.evaluate(element => element.getBoundingClientRect().height)
    const samples = await details.evaluateHandle(element => {
      let active = true
      const values: number[] = []
      const sample = (): void => {
        values.push(Number(getComputedStyle(element, '::details-content').opacity))
        if (active) requestAnimationFrame(sample)
      }
      sample()
      return { intermediate: () => values.some(value => value > 0 && value < 1), reset: () => { values.length = 0 }, stop: () => { active = false } }
    })
    await summary.press('Enter')
    await expect(details).toHaveAttribute('open', '')
    await expect.poll(() => details.evaluate(element => getComputedStyle(element, '::details-content').opacity)).toBe('1')
    expect(await samples.evaluate(control => control.intermediate())).toBe(true)
    await expect.poll(() => details.evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThan(collapsedHeight + 10)
    await samples.evaluate(control => control.reset())
    await summary.press('Space')
    await expect(details).not.toHaveAttribute('open', '')
    await expect.poll(() => details.evaluate(element => getComputedStyle(element, '::details-content').opacity)).toBe('0')
    expect(await samples.evaluate(control => control.intermediate())).toBe(true)
    await expect.poll(() => details.evaluate(element => element.getBoundingClientRect().height)).toBeCloseTo(collapsedHeight, 0)
    await expect(summary).toBeFocused()
    await samples.evaluate(control => control.stop())
    const section = h.page.locator('.ctx-section').first()
    await expect(section.locator('.ctx-section-head')).toBeVisible()
    const motion = await holdMotion(h.page, '.ctx-section-content')
    await section.locator('.ctx-section-head').click()
    await expect(section.locator('[data-motion-presence="closing"]')).toHaveAttribute('inert', '')
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await expect(section.locator('.ctx-section-content')).toHaveCount(0)
    await motion.evaluate(control => control.stop())
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await section.locator('.ctx-section-head').click()
    await expect(section.locator('.ctx-section-head')).toHaveAttribute('aria-expanded', 'true')
    expect(await section.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
    await summary.press('Enter')
    await expect(details).toHaveAttribute('open', '')
    expect(await details.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
  } finally {
    await h.close()
  }
})
