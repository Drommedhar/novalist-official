import { test, expect, type Locator } from '@playwright/test'
import { launchApp, resizeWindow, seedBook } from './harness'

async function caretBounds(scroller: Locator) {
  return scroller.evaluate(element => {
    const selection = window.getSelection()!
    const caret = selection.getRangeAt(0).getBoundingClientRect()
    const viewport = element.getBoundingClientRect()
    return {
      x: caret.left - viewport.left, y: caret.top - viewport.top,
      height: caret.height, width: element.clientWidth, viewportHeight: element.clientHeight,
      offset: selection.focusOffset, collapsed: selection.isCollapsed,
      focused: document.hasFocus()
    }
  })
}

async function expectCaret(scroller: Locator, anchor: 'off' | 'top' | 'middle' | 'bottom', before: Awaited<ReturnType<typeof caretBounds>>) {
  const after = await caretBounds(scroller)
  const target = anchor === 'off' ? before.y
    : after.viewportHeight * ({ top: 0.33, middle: 0.5, bottom: 0.66 }[anchor])
  expect(Math.abs(after.y - target)).toBeLessThan(3)
  expect(after.x).toBeGreaterThanOrEqual(0)
  expect(after.x).toBeLessThan(after.width)
  expect(after.offset).toBe(before.offset)
  expect(after.collapsed && after.focused).toBe(true)
}

for (const mode of ['scene', 'pages', 'focus', 'manuscript'] as const) {
  for (const anchor of ['off', 'top', 'middle', 'bottom'] as const) {
    test(`${mode} zoom follows caret with typewriter=${anchor}`, async () => {
      const h = await launchApp('nl-zoom-caret-')
      try {
        const book = await seedBook(h, { One: ['Opening'] })
        const prose = 'A line of prose with varied words stretches across the writing page. '.repeat(3)
        await h.rpc('scenes/write', [book.chapters[0].guid, book.chapters[0].scenes[0].id,
          `<p>${prose}</p>`.repeat(50), prose.repeat(50)])
        await resizeWindow(h, 1300, 900)
        await h.page.locator('.binder-scene-row').first().click()
        await h.page.evaluate(async ({ mode, anchor }) => {
          await window.novalistStores.settings.getState().update('global', {
            enableBookWidth: true, editorFontSize: 17, bookFontSize: 17,
            pageViewEnabled: mode === 'pages' || mode === 'focus', spellCheckEnabled: false, grammarCheckEnabled: false,
            typewriterScrollEnabled: anchor !== 'off', typewriterScrollAnchor: anchor
          })
          window.novalistStores.shell.getState().setMainView(mode === 'manuscript' ? 'manuscript' : 'write')
        }, { mode, anchor })
        const frame = h.page.frameLocator(`iframe[title="${mode === 'manuscript' ? 'manuscript' : 'editor'}"]`)
        const editor = frame.locator(mode === 'manuscript' ? '.scene-editor' : '#editor').first()
        const scroller = frame.locator(mode === 'manuscript' ? '#manuscript-wrapper' : '#editor-wrapper')
        await expect(editor).toContainText(prose.trim())
        if (mode === 'focus') {
          await editor.click()
          await h.page.keyboard.press('F11')
          await expect(h.page.locator('.shell-focus')).toBeVisible()
        }
        if (mode === 'pages' || mode === 'focus') await expect(frame.locator('.nv-page').first()).toBeVisible()
        await editor.focus()
        await expect.poll(() => editor.evaluate(() => {
          const state = (window as Window & { NovalistEditorState?: { isFocusing: boolean } }).NovalistEditorState
          return state?.isFocusing ?? false
        })).toBe(false)
        await editor.evaluate(element => {
          const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
          const nodes: Node[] = []
          for (let node = walker.nextNode(); node; node = walker.nextNode()) nodes.push(node)
          const node = nodes[Math.floor(nodes.length / 2)]
          const range = document.createRange()
          const start = Math.floor(node.textContent!.length / 2)
          let offset = start
          let right = -Infinity
          for (let i = start; i < Math.min(start + 90, node.textContent!.length); i++) {
            range.setStart(node, i)
            range.collapse(true)
            if (range.getBoundingClientRect().left > right) {
              right = range.getBoundingClientRect().left
              offset = i
            }
          }
          range.setStart(node, offset)
          range.collapse(true)
          window.getSelection()!.setPosition(node, offset)
          const wrapper = document.querySelector('#editor-wrapper, #manuscript-wrapper')!
          wrapper.scrollTop += range.getBoundingClientRect().top - wrapper.getBoundingClientRect().top - 150
        })
        expect(Math.abs((await caretBounds(scroller)).y - 150)).toBeLessThan(2)
        if (mode === 'manuscript') await h.page.keyboard.type('x')
        const before = await caretBounds(scroller)
        const bounds = (await scroller.boundingBox())!
        await h.page.mouse.move(bounds.x + bounds.width / 2, bounds.y + 100)
        await h.page.keyboard.down('Control')
        for (const delta of [-100, 100]) {
          for (let step = 0; step < 10; step++) await h.page.mouse.wheel(0, delta)
          // Page view repaginates after its 150 ms debounce.
          await h.page.waitForTimeout(250)
          await expectCaret(scroller, anchor, before)
        }
        if (anchor !== 'off') {
          for (const edge of ['first', 'last']) {
            await editor.evaluate((element, edge) => {
              const paragraph = edge === 'first' ? element.querySelector('p')! : Array.from(element.querySelectorAll('p')).at(-1)!
              const node = paragraph.firstChild!
              window.getSelection()!.setPosition(node, edge === 'first' ? 0 : node.textContent!.length)
            }, edge)
            const edgeCaret = await caretBounds(scroller)
            for (let step = 0; step < 5; step++) await h.page.mouse.wheel(0, 100)
            await h.page.waitForTimeout(250)
            await expectCaret(scroller, anchor, edgeCaret)
            for (let step = 0; step < 5; step++) await h.page.mouse.wheel(0, -100)
          }
        }
        await h.page.keyboard.up('Control')
        const scrollBefore = await scroller.evaluate(element => element.scrollTop)
        await h.page.mouse.wheel(0, -200)
        await expect.poll(() => scroller.evaluate(element => element.scrollTop)).toBeLessThan(scrollBefore)
        await h.page.waitForTimeout(250)
        const scrolled = await scroller.evaluate(element => element.scrollTop)
        await scroller.evaluate(() => window.dispatchEvent(new Event('resize')))
        await h.page.waitForTimeout(250)
        expect(await scroller.evaluate(element => element.scrollTop)).toBeCloseTo(scrolled, 0)
        if (mode === 'manuscript') {
          await h.page.keyboard.press('Control+z')
          await expect(editor).toHaveText(prose.repeat(50))
        }
      } finally {
        await h.close()
      }
    })
  }
}
