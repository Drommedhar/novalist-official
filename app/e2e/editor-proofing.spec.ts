import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

type ProofingWindow = EditorWindow & {
  buildGrammarPlainTextMap(): { plainText: string }
  setGrammarIssues(json: string, requestId?: number): void
  proofingRequests: { type: string; requestId: number; plainText: string }[]
}

// Exercise the shipped contenteditable in Chromium, without a project or a
// network grammar server. Only the asynchronous checker response is supplied.
test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate(() => {
    const w = window as unknown as ProofingWindow
    w.proofingRequests = []
    Object.assign(window, {
      invokeCSharpAction: (json: string) => w.proofingRequests.push(JSON.parse(json))
    })
  })
})

async function content(page: Page, html: string): Promise<void> {
  await page.evaluate((value) => (window as unknown as EditorWindow).setContent(value), html)
}

async function issues(page: Page, values: unknown[]): Promise<void> {
  await page.evaluate((value) => {
    const w = window as unknown as EditorWindow
    w.setGrammarCheckEnabled(true)
    w.setGrammarIssues(JSON.stringify(value))
  }, values)
}

async function caret(page: Page, id: string, offset = 0): Promise<void> {
  await page.evaluate(({ id, offset }) => {
    document.getElementById('editor')!.focus({ preventScroll: true })
    const node = document.getElementById(id)!.firstChild!
    window.getSelection()!.setPosition(node, offset)
  }, { id, offset })
}

const issue = (offset: number, length: number, replacement: string) => ({
  offset, length, type: 'grammar', message: 'Fixture correction', replacements: [replacement]
})

test('spelling corrects the clicked occurrence after the caret moves, with one undo', async ({ page }) => {
  await content(page, '<p>teh first. <b id="target">teh</b> second.</p><p id="elsewhere">Elsewhere.</p>')
  await page.locator('#target').click({ button: 'right' })
  await page.evaluate(() => (window as unknown as EditorWindow).setSpellingSuggestions('teh', ['the']))
  await caret(page, 'elsewhere', 3)
  await page.locator('[data-spelling="the"]').click()
  await expect(page.locator('#editor p').first()).toHaveText('teh first. the second.')
  await expect(page.locator('#elsewhere')).toHaveText('Elsewhere.')
  await expect(page.locator('#target')).toHaveText('the')
  await page.keyboard.press('ControlOrMeta+z')
  await expect(page.locator('#editor p').first()).toHaveText('teh first. teh second.')
})

test('a grammar correction across inline markup is one complete undoable edit', async ({ page }) => {
  await content(page, '<p>It is <b>ver</b>y bad today.</p>')
  await issues(page, [issue(6, 8, 'good')])
  await page.locator('.grammar-issue').first().click()
  await page.locator('.gp-suggestion').click()
  await expect(page.locator('#editor')).toHaveText('It is good today.')
  await page.keyboard.press('ControlOrMeta+z')
  await expect(page.locator('#editor')).toHaveText('It is very bad today.')
  await expect(page.locator('#editor b')).toHaveText('ver')
})

test('successive corrections retain the remaining issue targets and suggestions', async ({ page }) => {
  await content(page, '<p>Ths is teh end.</p>')
  await issues(page, [issue(0, 3, 'This'), issue(7, 3, 'the')])
  await page.locator('.grammar-issue', { hasText: 'Ths' }).click()
  await page.locator('.gp-suggestion', { hasText: 'This' }).click()
  await page.locator('.grammar-issue', { hasText: 'teh' }).click()
  await page.locator('.gp-suggestion', { hasText: 'the' }).click()
  await expect(page.locator('#editor')).toHaveText('This is the end.')
})

for (const menu of ['popup', 'context']) {
  test(`learning a word across inline markup saves the whole word from the ${menu} menu`, async ({ page }) => {
    await content(page, '<p>The <b>Ael</b>thorn banner burned.</p>')
    await issues(page, [{ ...issue(4, 8, 'Author'), type: 'spelling' }])
    await page.locator('.grammar-issue').last().click({ button: menu === 'popup' ? 'left' : 'right' })
    const action = menu === 'popup' ? '.gp-suggestion' : '[data-action="addToDictionary"]'
    await page.locator(action, { hasText: 'Add to Dictionary' }).click()
    const messages = await page.evaluate(() => (window as unknown as ProofingWindow).proofingRequests)
    expect(messages).toContainEqual({ type: 'addToDictionary', word: 'Aelthorn' })
  })

  test(`a deletion suggestion has a visible label and is undoable from the ${menu} menu`, async ({ page }) => {
    await content(page, '<p>This is extra text.</p>')
    await issues(page, [issue(8, 6, '')])
    await page.locator('.grammar-issue').click({ button: menu === 'popup' ? 'left' : 'right' })
    await page.locator(menu === 'popup' ? '.gp-suggestion' : '.cm-suggestion', { hasText: 'Delete' }).click()
    await expect(page.locator('#editor')).toHaveText('This is text.')
    await page.keyboard.press('ControlOrMeta+z')
    await expect(page.locator('#editor')).toHaveText('This is extra text.')
  })
}

test('late grammar results cannot underline edited text or another scene', async ({ page }) => {
  await content(page, '<p id="text">Ths is a sentence.</p>')
  await page.evaluate(() => (window as unknown as EditorWindow).setGrammarCheckEnabled(true))
  await expect.poll(() => page.evaluate(() =>
    (window as unknown as ProofingWindow).proofingRequests.filter(r => r.type === 'grammarCheckRequest').length
  )).toBe(1)
  const requestId = await page.evaluate(() =>
    (window as unknown as ProofingWindow).proofingRequests.find(r => r.type === 'grammarCheckRequest')!.requestId
  )
  await caret(page, 'text')
  await page.keyboard.type('New ')
  await page.evaluate(({ requestId, value }) =>
    (window as unknown as ProofingWindow).setGrammarIssues(JSON.stringify(value), requestId),
  { requestId, value: [issue(0, 3, 'This')] })
  await expect(page.locator('.grammar-issue')).toHaveCount(0)

  await content(page, '<p>Ths is a sentence.</p>')
  await page.evaluate(({ requestId, value }) =>
    (window as unknown as ProofingWindow).setGrammarIssues(JSON.stringify(value), requestId),
  { requestId, value: [issue(0, 3, 'This')] })
  await expect(page.locator('.grammar-issue')).toHaveCount(0)
})

const longScene = Array.from({ length: 160 }, (_, i) =>
  `<p id="line-${i}">Paragraph ${i}. The quick brown fox jumps over the lazy dog, then returns home.</p>`
).join('')

for (const pageView of [false, true]) for (const typewriter of [false, true]) {
test(`background grammar preserves the viewport (pages=${pageView}, typewriter=${typewriter})`, async ({ page }) => {
  await content(page, longScene)
  await page.evaluate(({ pageView, typewriter }) => {
    const w = window as unknown as EditorWindow
    w.setPageView(pageView)
    w.setTypewriterScroll(typewriter, 'middle')
  }, { pageView, typewriter })
  await caret(page, 'line-0', 4)
  // Let the native focus/selection events settle, then read far from the caret.
  await page.waitForTimeout(250)
  const before = await page.evaluate(() => {
    const wrapper = document.getElementById('editor-wrapper')!
    wrapper.scrollTop = document.getElementById('line-80')!.offsetTop
    return wrapper.scrollTop
  })
  await issues(page, [issue(0, 9, 'Section')])
  await page.waitForTimeout(200)
  expect(await page.locator('#editor-wrapper').evaluate(el => el.scrollTop)).toBeCloseTo(before, 0)

  await page.evaluate(() => {
    const anchor = document.getElementById('line-80')!.firstChild!
    const focus = document.getElementById('line-79')!.firstChild!
    window.getSelection()!.setBaseAndExtent(anchor, 12, focus, 4)
  })
  const selected = await page.evaluate(() => window.getSelection()!.toString())
  await issues(page, [issue(0, 9, 'Section')])
  expect(await page.evaluate(() => window.getSelection()!.toString())).toBe(selected)
  expect(await page.evaluate(() => window.getSelection()!.anchorNode!.parentElement!.id)).toBe('line-80')
})
}

test('grammar refresh preserves backwards selections through decorated text and empty-line carets', async ({ page }) => {
  await content(page, '<p id="first">Ths is a sentence.</p><p id="empty"><br></p><p id="last">Another sentence.</p>')
  await caret(page, 'last', 4)
  await page.evaluate(() => window.getSelection()!.extend(document.getElementById('first')!.firstChild!, 1))
  const selected = await page.evaluate(() => window.getSelection()!.toString())
  await issues(page, [issue(0, 3, 'This')])
  expect(await page.evaluate(() => window.getSelection()!.toString())).toBe(selected)
  expect(await page.evaluate(() => window.getSelection()!.anchorNode!.parentElement!.id)).toBe('last')
  for (const id of ['empty', 'last']) {
    await page.evaluate(id => window.getSelection()!.setPosition(document.getElementById(id)!, 0), id)
    await issues(page, [issue(0, 3, 'This')])
    expect(await page.evaluate(() => {
      const node = window.getSelection()!.anchorNode!
      return (node.nodeType === Node.ELEMENT_NODE ? node as Element : node.parentElement!).closest('p')!.id
    })).toBe(id)
  }
})

test('only the newest grammar response is accepted, and disabling cancels pending results', async ({ page }) => {
  await page.clock.install()
  await content(page, '<p id="text">Ths is a sentence.</p>')
  await page.evaluate(() => (window as unknown as EditorWindow).setGrammarCheckEnabled(true))
  await page.clock.fastForward(1600)
  await caret(page, 'text')
  await page.keyboard.type('New ')
  await page.clock.fastForward(1600)
  await page.evaluate(value => {
    const w = window as unknown as ProofingWindow
    const requests = w.proofingRequests.filter(r => r.type === 'grammarCheckRequest')
    w.setGrammarIssues(JSON.stringify(value), requests[1].requestId)
    w.setGrammarIssues('[]', requests[0].requestId)
  }, [issue(4, 3, 'This')])
  await expect(page.locator('.grammar-issue')).toHaveText('Ths')

  await caret(page, 'text')
  await page.keyboard.type('More ')
  await page.clock.fastForward(1600)
  await page.evaluate(value => {
    const w = window as unknown as ProofingWindow
    const requests = w.proofingRequests.filter(r => r.type === 'grammarCheckRequest')
    w.setGrammarCheckEnabled(false)
    w.setGrammarIssues(JSON.stringify(value), requests.at(-1)!.requestId)
  }, [issue(9, 3, 'This')])
  await expect(page.locator('.grammar-issue')).toHaveCount(0)
})

test('a spelling target that changed while the menu was open cannot insert at the caret', async ({ page }) => {
  await content(page, '<p><b id="target">teh</b> word.</p>')
  await page.locator('#target').click({ button: 'right' })
  await page.evaluate(() => {
    (window as unknown as EditorWindow).setSpellingSuggestions('teh', ['the'])
    document.getElementById('target')!.textContent = 'edited'
  })
  await page.locator('[data-spelling="the"]').click()
  await expect(page.locator('#editor')).toHaveText('edited word.')
})

test('spelling still targets the word after a background underline refresh', async ({ page }) => {
  await content(page, '<p><b id="target">teh</b> word.</p>')
  await page.locator('#target').click({ button: 'right' })
  await page.evaluate(() => (window as unknown as EditorWindow).setSpellingSuggestions('teh', ['the']))
  await issues(page, [issue(0, 3, 'the')])
  await page.locator('[data-spelling="the"]').click()
  await expect(page.locator('#editor')).toHaveText('the word.')
})

test('background grammar does not take focus from a comment input', async ({ page }) => {
  await content(page, '<p id="text">Ths is a sentence.</p>')
  await caret(page, 'text', 2)
  await page.evaluate(() => {
    const input = document.createElement('textarea')
    input.id = 'comment'
    document.body.append(input)
    input.focus()
  })
  await issues(page, [issue(0, 3, 'This')])
  await expect(page.locator('#comment')).toBeFocused()
})

test('typing deep in page view keeps the paragraph and viewport stable after pagination', async ({ page }) => {
  await content(page, longScene)
  await page.evaluate(() => (window as unknown as EditorWindow).setPageView(true))
  await page.waitForTimeout(250)
  await page.locator('#line-100').click()
  await page.keyboard.press('End')
  const before = await page.locator('#line-100').evaluate(el => el.getBoundingClientRect().top)
  await page.keyboard.type(' HELLO')
  await page.waitForTimeout(350)
  await expect(page.locator('#line-100')).toContainText(' HELLO')
  const after = await page.locator('#line-100').evaluate(el => el.getBoundingClientRect().top)
  expect(Math.abs(after - before)).toBeLessThan(40)
  expect(await page.evaluate(() => window.getSelection()!.anchorNode!.parentElement!.closest('p')!.id)).toBe('line-100')
})

test('page wrappers do not change grammar offsets', async ({ page }) => {
  await content(page, longScene)
  const before = await page.evaluate(() => (window as unknown as ProofingWindow).buildGrammarPlainTextMap().plainText)
  await page.evaluate(() => (window as unknown as EditorWindow).setPageView(true))
  await page.waitForTimeout(200)
  const after = await page.evaluate(() => (window as unknown as ProofingWindow).buildGrammarPlainTextMap().plainText)
  expect(after).toBe(before)
})
