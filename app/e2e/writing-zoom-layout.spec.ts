import { test, expect, type Locator } from '@playwright/test'
import { launchApp, seedBook, resizeWindow } from './harness'

async function readLines(editor: Locator) {
  return editor.evaluate(root => {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
    const lines: string[] = []
    let top = -Infinity
    let glyphHeight = 0
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      for (let i = 0; i < node.textContent!.length; i++) {
        const range = document.createRange()
        range.setStart(node, i)
        range.setEnd(node, i + 1)
        const rect = range.getBoundingClientRect()
        if (Math.abs(rect.top - top) > 1) {
          lines.push('')
          top = rect.top
        }
        lines[lines.length - 1] += node.textContent![i]
        glyphHeight = Math.max(glyphHeight, rect.height)
      }
    }
    return { lines, glyphHeight }
  })
}

for (const mode of ['scene', 'pages', 'manuscript'] as const) {
  for (const bookWidth of [false, true]) {
    test(`${mode} zoom preserves lines with book width=${bookWidth}`, async () => {
      const h = await launchApp('nl-zoom-layout-')
      try {
        await seedBook(h, { One: ['Opening'] })
        await resizeWindow(h, 1500, 900)
        await h.page.locator('.binder-scene-row').first().click()
        await h.page.evaluate(async ({ bookWidth, mode }) => {
          await window.novalistStores.settings.getState().update('global', {
            enableBookWidth: bookWidth, bookPageFormat: 'Custom', bookTextBlockWidth: 4.63,
            editorFontSize: 17, bookFontSize: 17, editorFontFamily: 'Georgia', bookFontFamily: 'Georgia',
            pageViewEnabled: mode === 'pages', spellCheckEnabled: false, grammarCheckEnabled: false
          })
          window.novalistStores.shell.getState().setMainView(mode === 'manuscript' ? 'manuscript' : 'write')
        }, { bookWidth, mode })
        const frame = h.page.frameLocator(`iframe[title="${mode === 'manuscript' ? 'manuscript' : 'editor'}"]`)
        const editor = frame.locator(mode === 'manuscript' ? '.scene-editor' : '#editor').first()
        await editor.fill('A long paragraph with varied words makes changes in line wrapping visible. '.repeat(12))
        if (mode === 'pages') await expect(frame.locator('.nv-page')).toBeVisible()
        const before = await readLines(editor)
        expect(before.lines.length).toBeGreaterThan(3)
        await editor.hover()
        await h.page.keyboard.down('Control')
        await h.page.mouse.wheel(0, -100)
        await expect.poll(async () => (await readLines(editor)).glyphHeight).toBeGreaterThan(before.glyphHeight)
        await expect.poll(async () => (await readLines(editor)).lines).toEqual(before.lines)
        for (let step = 0; step < 9; step++) await h.page.mouse.wheel(0, -100)
        await expect.poll(async () => (await readLines(editor)).glyphHeight).toBeGreaterThan(before.glyphHeight * 1.8)
        await expect.poll(async () => (await readLines(editor)).lines).toEqual(before.lines)
        const scroller = frame.locator(mode === 'manuscript' ? '#manuscript-wrapper' : '#editor-wrapper')
        const overflow = await scroller.evaluate(el => ({ width: el.scrollWidth, visible: el.clientWidth, overflow: getComputedStyle(el).overflowX }))
        expect(overflow.width).toBeGreaterThan(overflow.visible)
        expect(overflow.overflow).toBe('auto')
        for (let step = 0; step < 9; step++) await h.page.mouse.wheel(0, 100)
        await h.page.mouse.wheel(0, 100)
        await expect.poll(async () => (await readLines(editor)).glyphHeight).toBeCloseTo(before.glyphHeight, 1)
        await expect.poll(async () => (await readLines(editor)).lines).toEqual(before.lines)
        for (let step = 0; step < 5; step++) await h.page.mouse.wheel(0, 100)
        await expect.poll(async () => (await readLines(editor)).glyphHeight).toBeCloseTo(before.glyphHeight / 2, 1)
        await expect.poll(async () => (await readLines(editor)).lines).toEqual(before.lines)
        expect(await scroller.evaluate(element => element.scrollWidth)).toBe(await scroller.evaluate(element => element.clientWidth))
        await h.page.keyboard.up('Control')
      } finally {
        await h.close()
      }
    })
  }
}
