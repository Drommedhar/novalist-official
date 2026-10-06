import { expect, test } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'
import { holdMotion } from './motion'

test('editor context menus fade both ways and reduced motion removes the delay', async () => {
  const h = await launchApp('nl-editor-motion-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.rpc('scenes/write', [chapter.guid, scene.id, '<p>The lantern lights the room.</p>', 'The lantern lights the room.'])
    await h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: scene.id })
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    const menu = frame.locator('#context-menu')
    await expect(editor).toHaveText('The lantern lights the room.')
    const editorFrame = await (await h.page.locator('.editor-frame').elementHandle())?.contentFrame()
    if (!editorFrame) throw new Error('The scene editor frame must be ready')
    const motion = await holdMotion(editorFrame, '#context-menu')

    await editor.click({ button: 'right' })
    await expect(menu).toHaveClass('visible')
    await expect.poll(() => motion.evaluate(control => control.details().length)).toBeGreaterThan(0)
    await motion.evaluate(control => control.middle())
    const opacity = await menu.evaluate(element => Number(getComputedStyle(element).opacity))
    expect(opacity).toBeGreaterThan(0)
    expect(opacity).toBeLessThan(1)
    await motion.evaluate(control => control.finish())

    await h.page.keyboard.press('Escape')
    await expect(menu).not.toHaveClass('visible')
    await expect(menu).toHaveCSS('pointer-events', 'none')
    await expect.poll(() => motion.evaluate(control => control.details().length)).toBeGreaterThan(0)
    await motion.evaluate(control => control.finish())
    await expect(menu).toBeHidden()

    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await editor.click({ button: 'right' })
    await expect(menu).toBeVisible()
    expect(await menu.evaluate(element => element.getAnimations().length)).toBe(0)
    await h.page.keyboard.press('Escape')
    await expect(menu).toHaveCSS('display', 'none')
    await expect(editor).toHaveText('The lantern lights the room.')
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})

test('chapter disclosures animate their height and immediately disable closing scene controls', async () => {
  const h = await launchApp('nl-binder-motion-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, { Opening: ['Arrival', 'Departure', 'Return'] })
    await dismissTour(h.page)
    await enterWriting(h.page)
    const chapter = h.page.locator('.binder-chapter').first()
    const toggle = chapter.locator('.binder-expand').first()
    const presence = chapter.locator('.motion-presence')
    const scenes = chapter.locator('.binder-scene-row')
    await expect(scenes).toHaveCount(3)
    await expect.poll(() => chapter.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
    const initialHeight = await chapter.evaluate(element => element.getBoundingClientRect().height)
    const motion = await holdMotion(h.page, '.binder-chapter')
    await toggle.click()
    await expect(presence).toHaveAttribute('data-motion-presence', 'closing')
    await expect(presence).toHaveAttribute('inert', '')
    await expect.poll(() => motion.evaluate(control => control.details().length)).toBeGreaterThan(0)
    await motion.evaluate(control => control.middle())
    const middleHeight = await chapter.evaluate(element => element.getBoundingClientRect().height)
    expect(middleHeight).toBeLessThan(initialHeight)
    await motion.evaluate(control => control.finish())
    await expect(scenes).toHaveCount(0)
    expect(await chapter.evaluate(element => element.getBoundingClientRect().height)).toBeLessThan(middleHeight)

    await toggle.click()
    await expect(scenes).toHaveCount(3)
    await expect.poll(() => motion.evaluate(control => control.details().length)).toBeGreaterThan(0)
    await motion.evaluate(control => control.finish())
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await toggle.click()
    await expect(scenes).toHaveCount(0)
    expect(await chapter.evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})

test('scene navigation fades the retained editor while typing and saving stay immediate', async () => {
  const h = await launchApp('nl-scene-motion-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const [arrival, departure] = chapter.scenes
    for (const scene of chapter.scenes) {
      await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${scene.title} begins.</p>`, `${scene.title} begins.`])
    }
    await h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: arrival.id })
    const iframe = h.page.locator('.editor-frame')
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Arrival begins.')
    const originalFrame = await iframe.evaluateHandle(element => element)
    const originalEditor = await editor.evaluateHandle(element => element)
    await expect.poll(() => iframe.evaluate(element => element.getAnimations().length)).toBe(0)
    const motion = await holdMotion(h.page, '.editor-frame')

    await h.page.locator('.binder-scene-row').filter({ hasText: 'Departure' }).click()
    await expect(editor).toHaveText('Departure begins.')
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    expect(await originalFrame.evaluate(element => element.isConnected)).toBe(true)
    expect(await originalEditor.evaluate(element => element.isConnected)).toBe(true)
    await motion.evaluate(control => control.finish())

    await editor.click()
    await h.page.keyboard.press('ControlOrMeta+End')
    await h.page.keyboard.insertText(' More.')
    await expect(editor).toHaveText('Departure begins. More.')
    await h.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    await expect.poll(async () => (await h.rpc<{ html: string }>('scenes/read', [chapter.guid, departure.id])).html).toContain('More.')
    expect(await motion.evaluate(control => control.details())).toEqual([])
    expect(await editor.evaluate(element => element.ownerDocument.activeElement === element)).toBe(true)
    await h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: departure.id })
    expect(await motion.evaluate(control => control.details())).toEqual([])
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})
