import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow } from './harness'
import { holdMotion } from './motion'

test('book details animate their layout, switch selected books and close without blocking the shelf', async () => {
  const h = await launchApp('nl-bookshelf-motion-')
  try {
    await h.rpc('project/create', [h.workDir, 'Motion Library', 'First Book'])
    await h.rpc('project/createBook', ['Second Book'])
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await dismissTour(h.page)
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const first = h.page.getByRole('button', { name: 'Select First Book — Motion Library', exact: true })
    const second = h.page.getByRole('button', { name: 'Select Second Book — Motion Library', exact: true })
    const slot = h.page.locator('.library-inspector-slot')
    const details = h.page.locator('.library-inspector')
    for (const width of [1600, 800]) {
      await resizeWindow(h, width, 950)
      const shelfWidth = await h.page.locator('.library-shelves').evaluate(element => element.clientWidth)
      const motion = await holdMotion(h.page, '.library-inspector-slot')
      await first.click()
      await expect(details.locator('h2')).toHaveText('First Book')
      expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
      await motion.evaluate(control => control.middle())
      const midway = (await slot.boundingBox())!.width
      const expanded = (await details.boundingBox())!.width
      expect(midway).toBeGreaterThan(0)
      expect(midway).toBeLessThan(expanded)
      expect(await h.page.locator('.project-library').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
      const movingShelf = await h.page.locator('.library-shelves').evaluate(element => element.clientWidth)
      if (width > 900) {
        expect(movingShelf).toBeLessThan(shelfWidth)
        expect(movingShelf).toBeGreaterThan(shelfWidth - expanded)
      } else expect(movingShelf).toBe(shelfWidth)
      await motion.evaluate(control => control.finish())
      await second.press('Enter')
      await expect(details.locator('h2')).toHaveText('Second Book')
      expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
      await motion.evaluate(control => control.finish())
      await expect(details.locator('.library-open-book')).toBeFocused()
      await h.page.keyboard.press('Escape')
      await expect(h.page.getByRole('complementary', { name: 'Book details' })).toHaveCount(0)
      await expect(slot.locator('..')).toHaveAttribute('inert', '')
      await expect(second).toBeFocused()
      await motion.evaluate(control => control.middle())
      expect((await slot.boundingBox())!.width).toBeGreaterThan(0)
      expect((await slot.boundingBox())!.width).toBeLessThan(expanded)
      expect(await h.page.locator('.project-library').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
      await motion.evaluate(control => control.finish())
      await expect(slot).toHaveCount(0)
      await motion.evaluate(control => control.stop())
    }

    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await first.press('Enter')
    await expect(details.locator('.library-open-book')).toBeFocused()
    expect(await slot.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
    await h.page.keyboard.press('Escape')
    await expect(slot).toHaveCount(0)
    await expect(first).toBeFocused()
  } finally { await h.close() }
})

test('library native dialogs animate both ways and release modal focus as soon as closed', async () => {
  const h = await launchApp('nl-library-dialog-motion-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const trigger = h.page.getByRole('button', { name: 'Scratchpad', exact: true })
    const dialog = h.page.locator('.library-scratchpad-dialog:not(.library-removed-dialog)')
    const motion = await holdMotion(h.page, '.library-scratchpad-dialog:not(.library-removed-dialog)')
    await trigger.click()
    await expect(dialog).toHaveAttribute('open', '')
    await expect.poll(async () => (await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(dialog).not.toHaveAttribute('open', '')
    await expect(trigger).toBeFocused()
    await expect.poll(async () => (await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.middle())
    expect(await dialog.evaluate(element => Number(getComputedStyle(element).opacity))).toBeGreaterThan(0)
    expect(await dialog.evaluate(element => {
      const box = element.getBoundingClientRect()
      return element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2))
    })).toBe(false)
    await h.page.keyboard.press('Tab')
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(false)
    await motion.evaluate(control => control.finish())
    await expect(dialog).toBeHidden()
    await motion.evaluate(control => control.stop())
  } finally { await h.close() }
})
