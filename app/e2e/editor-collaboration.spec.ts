import { test, expect, type Page } from '@playwright/test'
import { build } from 'vite'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'
import type {} from './fixtures/collaboration'
import * as Y from 'yjs'

const bundle = resolve('out/collaboration-tests/harness.js')

test.beforeAll(async () => {
  await build({
    configFile: false,
    logLevel: 'error',
    build: {
      minify: false,
      outDir: resolve('out/collaboration-tests'),
      lib: { entry: resolve('e2e/fixtures/collaboration.ts'), name: 'CollaborationHarness', formats: ['iife'], fileName: () => 'harness.js' }
    }
  })
})

async function openEditor(page: Page, state?: number[], html = '<p>The story.</p>'): Promise<void> {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.addScriptTag({ path: bundle })
  await page.evaluate(({ state, html }) => {
    const editor = window as unknown as EditorWindow
    editor.setContent(html)
    window.collaboration.start(state)
  }, { state, html })
}

async function caret(page: Page, offset: number): Promise<void> {
  await page.evaluate(offset => {
    const editor = document.getElementById('editor')!
    editor.focus()
    window.getSelection()!.setPosition(editor.firstChild!.firstChild!, offset)
  }, offset)
}

async function exchange(first: Page, second: Page): Promise<void> {
  const left = await first.evaluate(() => window.collaboration.drain())
  const right = await second.evaluate(() => window.collaboration.drain())
  for (const update of left) await second.evaluate(update => window.collaboration.receive(update), update)
  for (const update of right) await first.evaluate(update => window.collaboration.receive(update), update)
}

test('remote images cannot execute attributes before scene sanitization', async ({ page }) => {
  await openEditor(page)
  const document = new Y.Doc()
  Y.applyUpdate(document, new Uint8Array(await page.evaluate(() => window.collaboration.state())))
  const vector = Y.encodeStateVector(document)
  document.getText('prose').insert(0, '\ufffc', { embed: JSON.stringify({ tag: 'img', attributes: {
    src: 'data:image/png;base64,AA==', onerror: 'document.body.dataset.unsafeCollaborationImage="executed"'
  } }) })
  const update = Array.from(Y.encodeStateAsUpdate(document, vector))
  await page.evaluate(update => window.collaboration.receive(update), update)
  await page.waitForTimeout(200)
  expect(await page.locator('body').getAttribute('data-unsafe-collaboration-image')).toBeNull()
  expect(await page.locator('#editor img').getAttribute('onerror')).toBeNull()
  document.destroy()
})

test('schema-invalid updates leave the editor and saved shared state intact', async ({ page }) => {
  await openEditor(page)
  const original = await page.evaluate(() => window.collaboration.state())
  const document = new Y.Doc()
  Y.applyUpdate(document, new Uint8Array(original))
  const vector = Y.encodeStateVector(document)
  document.getText('prose').insertEmbed(0, { unexpected: 'object' })
  const update = Array.from(Y.encodeStateAsUpdate(document, vector))
  await expect(page.evaluate(update => window.collaboration.receive(update), update)).rejects.toThrow()
  expect(await page.evaluate(() => window.collaboration.state())).toEqual(original)
  await expect(page.locator('#editor')).toHaveText('The story.')
  await caret(page, 10)
  await page.keyboard.type(' More.')
  await expect(page.locator('#editor')).toHaveText('The story. More.')
  document.destroy()
})

test('out-of-order invalid formatting cannot block later valid typing', async ({ page }) => {
  await openEditor(page)
  const original = await page.evaluate(() => window.collaboration.state())
  const document = new Y.Doc()
  try {
    Y.applyUpdate(document, new Uint8Array(original))
    const prose = document.getText('prose')
    const block = prose.toDelta().at(-1).attributes.block
    const updates: Uint8Array[] = []
    document.on('update', update => updates.push(update))
    prose.insert(0, 'New ')
    prose.format(0, 1, { block })
    await page.evaluate(update => window.collaboration.receive(update), Array.from(updates[1]))
    await expect(page.evaluate(() => window.collaboration.state())).rejects.toThrow(/missing history/)
    await expect(page.locator('#editor')).toHaveText('The story.')
    await page.evaluate(update => window.collaboration.receive(update), Array.from(updates[0]))
    await expect(page.locator('#editor')).toHaveText('New The story.')
    expect(await page.evaluate(() => window.collaboration.errors())).toHaveLength(1)
    await caret(page, 0)
    await page.keyboard.type('My ')
    await expect(page.locator('#editor')).toHaveText('My New The story.')
  } finally { document.destroy() }
})

test('deleting text between concurrent paragraph splits retains the empty paragraph', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p>The story ends.</p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.press('Enter')
  await caret(other, 9)
  await other.keyboard.press('Enter')
  await exchange(page, other)
  await exchange(page, other)
  await page.evaluate(() => {
    const paragraph = document.querySelectorAll('#editor p')[1]
    const selection = window.getSelection()!
    selection.selectAllChildren(paragraph)
  })
  await page.keyboard.press('Backspace')
  await exchange(page, other)
  await expect(other.locator('#editor p')).toHaveCount(3)
  await expect(other.locator('#editor p').nth(1)).toHaveText('')
  expect(await other.locator('#editor').innerHTML()).toBe(await page.locator('#editor').innerHTML())
})

test('concurrent typing merges in the existing editor and preserves the local caret', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.type('old ')
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  await expect(page.locator('#editor')).toHaveText('The old story continues.')
  await expect(other.locator('#editor')).toHaveText('The old story continues.')
  await page.keyboard.type('good ')
  await exchange(page, other)
  await expect(other.locator('#editor')).toHaveText('The old good story continues.')
})

test('formatting and concurrent typing converge without removing either writer’s work', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await page.evaluate(() => {
    const editor = document.getElementById('editor')!
    editor.focus()
    const text = editor.firstChild!.firstChild!
    window.getSelection()!.setBaseAndExtent(text, 4, text, 9)
    ;(window as unknown as EditorWindow).toggleBold()
  })
  await caret(other, 0)
  await other.keyboard.type('Long ')
  await exchange(page, other)
  for (const writer of [page, other]) {
    await expect(writer.locator('#editor')).toHaveText('Long The story.')
    await expect(writer.locator('#editor b, #editor strong')).toHaveText('story')
  }
})

test('undo removes only the local writer’s edit after a remote update', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.type('old ')
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  await page.keyboard.press('Control+z')
  await exchange(page, other)
  await expect(other.locator('#editor')).toHaveText('The story continues.')
  await page.keyboard.press('Control+Shift+z')
  await exchange(page, other)
  await expect(other.locator('#editor')).toHaveText('The old story continues.')
})

test('remote edits wait for IME confirmation and never publish unfinished pinyin', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  expect(await page.evaluate(() => window.collaboration.drain())).toEqual([])
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  await cdp.send('Input.insertText', { text: '你好' })
  await expect(page.locator('#editor')).toHaveText('The 你好story continues.')
  await exchange(page, other)
  await expect(other.locator('#editor')).toHaveText('The 你好story continues.')
})

test('inserting a paragraph preserves simultaneous edits to the following paragraph', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 0)
  await page.keyboard.press('Enter')
  await page.keyboard.press('ArrowUp')
  await page.keyboard.type('Prologue')
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  for (const writer of [page, other]) await expect(writer.locator('#editor > p')).toHaveText(['Prologue', 'The story continues.'])
})

test('splitting a paragraph keeps remote edits attached to their original words', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.press('Enter')
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  for (const writer of [page, other]) await expect(writer.locator('#editor > p')).toHaveText(['The ', 'story continues.'])
})

test('joining preserves annotations, images, links and nested lists from the existing editor', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p class="nv-style-intro"><span class="nv-comment" data-comment-id="comment-1">The story</span><sup class="nv-fn" data-fn-id="note-1">1</sup><br><a href="https://example.com">Source</a><img src="Images/map.png" alt="Map"></p><ul><li>First<ul><li>Nested</li></ul></li><li>Second</li></ul>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await expect(other.locator('#editor .nv-comment')).toHaveAttribute('data-comment-id', 'comment-1')
  await expect(other.locator('#editor .nv-fn')).toHaveAttribute('data-fn-id', 'note-1')
  await expect(other.locator('#editor img')).toHaveAttribute('src', 'Images/map.png')
  await expect(other.locator('#editor a')).toHaveAttribute('href', 'https://example.com')
  await expect(other.locator('#editor p > br')).toHaveCount(1)
  await expect(other.locator('#editor > ul > li')).toHaveText(['FirstNested', 'Second'])
  await expect(other.locator('#editor > ul > li > ul > li')).toHaveText('Nested')
})

test('malformed remote cursor positions cannot interrupt the editor', async ({ page }) => {
  await openEditor(page)
  await page.evaluate(() => window.collaboration.showPresence({ anchor: [255], focus: [255] }))
  await caret(page, 0)
  await page.keyboard.type('Still writing. ')
  await expect(page.locator('#editor')).toHaveText('Still writing. The story.')
  expect(await page.evaluate(() => window.collaboration.errors())).toEqual([])
})

for (const backward of [false, true]) test(`remote ${backward ? 'backward' : 'forward'} selection preserves its words and focus`, async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await other.evaluate(backward => {
    const text = document.querySelector('#editor p')!.firstChild!
    window.getSelection()!.setBaseAndExtent(text, backward ? 9 : 4, text, backward ? 4 : 9)
  }, backward)
  const selection = await other.evaluate(() => window.collaboration.presence())
  if (!selection) throw new Error('The collaborator selection was not captured.')
  await page.evaluate(selection => window.collaboration.showPresence(selection), selection)
  await expect(page.locator('.nv-collaboration-label')).toHaveText('Bea')
  await caret(page, 0)
  await page.keyboard.type('Long ')
  const selected = await page.evaluate(() => Array.from(CSS.highlights.get('novalist-collaborators') ?? [], range => range instanceof Range ? range.toString() : '').join(''))
  expect(selected).toBe('story')
  const caretError = await page.evaluate(backward => {
    const selected = Array.from(CSS.highlights.get('novalist-collaborators')!)[0] as Range
    const focus = selected.cloneRange()
    focus.collapse(backward)
    return Math.abs(document.querySelector('.nv-collaboration-caret')!.getBoundingClientRect().left - focus.getBoundingClientRect().left)
  }, backward)
  expect(caretError).toBeLessThan(1)
  expect(await page.evaluate(() => (window as unknown as EditorWindow).getContent())).not.toContain('Bea')
})

test('page layout and proofing remain local while remote edits still save normally', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    editor.setPageView(true)
    editor.setGrammarCheckEnabled(true)
    editor.setGrammarIssues(JSON.stringify([{ offset: 4, length: 5, type: 'grammar', message: 'Test finding' }]))
  })
  await expect(page.locator('#editor .nv-page')).toHaveCount(1)
  await expect(page.locator('#editor .grammar-issue')).toHaveCount(1)
  expect(await page.evaluate(() => window.collaboration.drain())).toEqual([])
  await caret(other, 9)
  await other.keyboard.type(' continues')
  await exchange(page, other)
  await expect(page.locator('#editor')).toHaveText('The story continues.')
  await expect(page.locator('#editor .nv-page')).toHaveCount(1)
  const saved = await page.evaluate(() => (window as unknown as EditorWindow).getContent())
  expect(saved).not.toContain('nv-page')
  expect(saved).not.toContain('grammar-issue')
})

test('saved shared state survives restarting both writers and replaying updates twice', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page)
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.type('old ')
  await caret(other, 9)
  await other.keyboard.type(' continues')
  const left = await page.evaluate(() => window.collaboration.state())
  const right = await other.evaluate(() => window.collaboration.state())
  await openEditor(page, left)
  await openEditor(other, right)
  for (let attempt = 0; attempt < 2; attempt++) {
    await page.evaluate(state => window.collaboration.receive(state), right)
    await other.evaluate(state => window.collaboration.receive(state), left)
  }
  await expect(page.locator('#editor')).toHaveText('The old story continues.')
  await expect(other.locator('#editor')).toHaveText('The old story continues.')
  expect(await page.evaluate(() => window.collaboration.drain())).toEqual([])
  expect(await other.evaluate(() => window.collaboration.drain())).toEqual([])
})

test('a comment around an entity mention survives joining and later edits', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p><span class="nv-comment" data-comment-id="comment-1"><span class="nv-entity-mention" data-entity-id="alice">Alice</span></span> writes.</p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await expect(other.locator('#editor .nv-comment .nv-entity-mention')).toHaveText('Alice')
  await expect(other.locator('#editor .nv-comment')).toHaveAttribute('data-comment-id', 'comment-1')
  await other.locator('#editor').focus()
  await other.keyboard.press('Control+End')
  await other.keyboard.type(' Together.')
  await exchange(page, other)
  await expect(page.locator('#editor .nv-comment .nv-entity-mention')).toHaveText('Alice')
})

test('simultaneous paragraph splits preserve both writers’ line breaks', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p>The story ends.</p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 4)
  await page.keyboard.press('Enter')
  await caret(other, 9)
  await other.keyboard.press('Enter')
  await exchange(page, other)
  for (const writer of [page, other]) await expect(writer.locator('#editor > p')).toHaveText(['The ', 'story', ' ends.'])
  await exchange(page, other)
  await expect(page.locator('#editor > p')).toHaveText(['The ', 'story', ' ends.'])
})

test('remote cursors remain visible in empty paragraphs', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p>A</p><p><br></p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await other.evaluate(() => {
    const paragraph = document.querySelectorAll('#editor p')[1]
    window.getSelection()!.setPosition(paragraph, 0)
  })
  const selection = await other.evaluate(() => window.collaboration.presence())
  if (!selection) throw new Error('The empty paragraph selection was not captured.')
  await page.evaluate(selection => window.collaboration.showPresence(selection), selection)
  await expect(page.locator('.nv-collaboration-label')).toBeVisible()
  const paragraph = await page.locator('#editor p').nth(1).boundingBox()
  const cursor = await page.locator('.nv-collaboration-caret').boundingBox()
  expect(cursor?.y).toBeCloseTo(paragraph!.y, 0)
})

test('footnote insertion and renumbering preserve simultaneous prose edits', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p>The story<sup class="nv-fn" data-fn-id="note-1">1</sup> ends.</p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await caret(page, 0)
  await page.evaluate(() => (window as unknown as EditorWindow).insertFootnoteAtSelection('note-new'))
  await caret(other, 4)
  await other.keyboard.type('old ')
  await exchange(page, other)
  for (const writer of [page, other]) {
    await expect(writer.locator('#editor')).toHaveText('1The old story2 ends.')
    await expect(writer.locator('#editor .nv-fn')).toHaveText(['1', '2'])
  }
})

test('text after a nested list stays in the same list item on join', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<ul><li>Before<ul><li>Inside</li></ul>After</li></ul>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await expect(other.locator('#editor > ul > li')).toHaveCount(1)
  await expect(other.locator('#editor > ul > li')).toHaveText('BeforeInsideAfter')
})

test('nested formatting keeps its original order and appearance on join', async ({ page, context }) => {
  const other = await context.newPage()
  await openEditor(page, undefined, '<p><span class="z" style="color:red"><span class="a" style="color:blue">Blue</span></span></p>')
  await openEditor(other, await page.evaluate(() => window.collaboration.state()))
  await expect(other.locator('#editor .z > .a')).toHaveText('Blue')
  await expect(other.locator('#editor .a')).toHaveCSS('color', 'rgb(0, 0, 255)')
})
