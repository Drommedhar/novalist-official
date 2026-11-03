import { test, expect, type Page } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook, type Harness } from './harness'

const NAME = 'Mira Vance'

async function setup(h: Harness, prose = 'The door stood open.') {
  const book = await seedBook(h, { 'Chapter One': ['Opening', 'Second'] })
  await dismissTour(h.page)
  await enterWriting(h.page)
  const entity = await h.rpc<{ id: string }>('entities/create', ['character', NAME])
  await h.page.evaluate(() => window.novalistStores.codex.getState().refresh())
  const chapter = book.chapters[0]
  await h.rpc('scenes/write', [chapter.guid, chapter.scenes[0].id, `<p>${prose}</p>`, prose])
  await h.page.locator('.binder-scene-row').first().click()
  await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText(prose)
  await expect(h.page.locator('.ctx-stats')).toBeVisible()
  return { chapter, entity }
}

async function holdSceneWrites(page: Page) {
  await page.evaluate(() => {
    const original = window.novalistRpc.request.bind(window.novalistRpc)
    let release!: () => void
    const pending = new Promise<void>((resolve) => { release = resolve })
    window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
      if (method === 'scenes/write') await pending
      return original<T>(method, params)
    }
    ;(window as unknown as { releaseContextWrites: () => void }).releaseContextWrites = () => {
      window.novalistRpc.request = original
      release()
    }
  })
}

async function releaseSceneWrites(page: Page) {
  await page.evaluate(() => {
    ;(window as unknown as { releaseContextWrites?: () => void }).releaseContextWrites?.()
  })
}

test('Context follows added and removed names before the scene is saved', async () => {
  const h = await launchApp('nl-context-live-')
  try {
    const { chapter } = await setup(h)
    await holdSceneWrites(h.page)
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await editor.click()
    await h.page.keyboard.press('ControlOrMeta+End')
    await h.page.keyboard.type(` ${NAME} arrived.`)
    await expect(editor).toContainText(NAME)
    await expect(h.page.locator('.ctx-card-name')).toHaveText(NAME, { timeout: 10_000 })
    const saved = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])
    expect(saved.html).not.toContain(NAME)
    await h.page.keyboard.press('ControlOrMeta+A')
    await h.page.keyboard.type('The door stood open.')
    await expect(h.page.locator('.ctx-card')).toHaveCount(0)
    await expect(h.page.locator('.ctx-stats')).toContainText('4 words')
  } finally {
    await releaseSceneWrites(h.page)
    await h.close()
  }
})

test('Context entity cards open the selected Wiki article and keep Edit in Codex available', async () => {
  const h = await launchApp('nl-context-wiki-')
  try {
    await setup(h, `${NAME} arrived.`)
    await h.page.locator('.ctx-card').filter({ hasText: NAME }).click()
    await expect(h.page.locator('.wiki-article-heading h1')).toHaveText(NAME)
    await expect(h.page.locator('.pane-leaf[data-view="wiki"]')).toBeVisible()
    await h.page.locator('.wiki-edit-btn').click()
    await expect(h.page.locator('.pane-leaf[data-view="codex"]')).toBeVisible()
    await expect(h.page.locator('.pane-leaf[data-view="codex"]')).toContainText(NAME)
  } finally { await h.close() }
})

test('Focus Peek from the Context sidebar opens its Wiki article', async () => {
  const h = await launchApp('nl-context-peek-wiki-')
  try {
    await setup(h, `${NAME} arrived.`)
    await h.page.locator('.ctx-card').filter({ hasText: NAME }).hover()
    await expect(h.page.locator('.peek-title')).toHaveText(NAME)
    await h.page.locator('.peek-actions button').first().click()
    await expect(h.page.locator('.wiki-article-heading h1')).toHaveText(NAME)
    await expect(h.page.locator('.pane-leaf[data-view="wiki"]')).toBeVisible()
  } finally { await h.close() }
})

test('Focus Peek from the prose opens its Wiki article', async () => {
  const h = await launchApp('nl-editor-peek-wiki-')
  try {
    await setup(h, `${NAME} arrived.`)
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    const bounds = await editor.evaluate((root, name) => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
      let node: Node | null
      while ((node = walker.nextNode())) {
        const start = node.textContent?.indexOf(name) ?? -1
        if (start < 0) continue
        const range = document.createRange()
        range.setStart(node, start)
        range.setEnd(node, start + name.length)
        const rect = range.getBoundingClientRect()
        return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 }
      }
      throw new Error('Name is absent from editor')
    }, NAME)
    const frame = (await h.page.locator('.editor-frame').boundingBox())!
    await h.page.mouse.move(frame.x + bounds.x, frame.y + bounds.y)
    await expect(h.page.locator('.peek-title')).toHaveText(NAME)
    await h.page.locator('.peek-actions button').first().click()
    await expect(h.page.locator('.wiki-article-heading h1')).toHaveText(NAME)
    await expect(h.page.locator('.pane-leaf[data-view="wiki"]')).toBeVisible()
  } finally { await h.close() }
})


test('a linked sidebar entity opens its selected Wiki article', async () => {
  const h = await launchApp('nl-context-linked-wiki-')
  try {
    const { chapter, entity } = await setup(h)
    await h.rpc('links/add', [chapter.guid, chapter.scenes[0].id, 'entity', entity.id])
    await h.page.locator('.inspector-tab').nth(3).click()
    await h.page.locator('.inspector-tab').first().click()
    await h.page.locator('.links-target').filter({ hasText: NAME }).click()
    await expect(h.page.locator('.wiki-article-heading h1')).toHaveText(NAME)
    await expect(h.page.locator('.pane-leaf[data-view="wiki"]')).toBeVisible()
  } finally { await h.close() }
})

async function holdNextAnalysis(page: Page) {
  await page.evaluate(() => {
    const original = window.novalistRpc.request.bind(window.novalistRpc)
    const state = window as unknown as {
      contextAnalysisHeld: boolean
      releaseContextAnalysis: () => void
    }
    state.contextAnalysisHeld = false
    let release!: () => void
    let held = false
    const gate = new Promise<void>((resolve) => { release = resolve })
    state.releaseContextAnalysis = () => {
      window.novalistRpc.request = original
      release()
    }
    window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
      const result = await original<T>(method, params)
      if (method === 'context/analyze' && !held) {
        held = true
        state.contextAnalysisHeld = true
        await gate
      }
      return result
    }
  })
}

async function releaseAnalysis(page: Page) {
  await page.evaluate(() => {
    ;(window as unknown as { releaseContextAnalysis?: () => void }).releaseContextAnalysis?.()
  })
}

test('a live Context response preserves an analysis field being edited', async () => {
  const h = await launchApp('nl-context-field-')
  try {
    await setup(h)
    await holdNextAnalysis(h.page)
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await h.page.keyboard.type(` ${NAME} arrived.`)
    await h.page.waitForFunction(() => (window as unknown as { contextAnalysisHeld: boolean }).contextAnalysisHeld)
    const conflict = h.page.locator('.ctx-panel input[placeholder="Conflict"]')
    await conflict.fill('Still deciding what Mira wants')
    await releaseAnalysis(h.page)
    await expect(h.page.locator('.ctx-card-name')).toHaveText(NAME)
    await expect(conflict).toHaveValue('Still deciding what Mira wants')
  } finally {
    await releaseAnalysis(h.page)
    await h.close()
  }
})

test('a delayed Context response cannot replace the next scene', async () => {
  const h = await launchApp('nl-context-scene-race-')
  try {
    const { chapter } = await setup(h)
    await h.rpc('scenes/write', [chapter.guid, chapter.scenes[1].id, '<p>A quiet ending.</p>', 'A quiet ending.'])
    await holdNextAnalysis(h.page)
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await h.page.keyboard.type(` ${NAME} arrived.`)
    await h.page.waitForFunction(() => (window as unknown as { contextAnalysisHeld: boolean }).contextAnalysisHeld)
    await h.page.locator('.binder-scene-row').nth(1).click()
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText('A quiet ending.')
    await expect(h.page.locator('.ctx-stats')).toContainText('3 words')
    await releaseAnalysis(h.page)
    await h.page.evaluate(() => new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))))
    await expect(h.page.locator('.ctx-card')).toHaveCount(0)
    await expect(h.page.locator('.ctx-stats')).toContainText('3 words')
  } finally {
    await releaseAnalysis(h.page)
    await h.close()
  }
})

test('Context follows unsaved manuscript prose rather than the retained scene editor', async () => {
  const h = await launchApp('nl-context-manuscript-')
  try {
    const { chapter } = await setup(h)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    const prose = h.page.frameLocator('iframe[src*="manuscript-editor"]').locator('.scene-editor').first()
    await expect(prose).toContainText('The door stood open.')
    await holdSceneWrites(h.page)
    await prose.click()
    await h.page.keyboard.type(` ${NAME} arrived.`)
    await expect(prose).toContainText(NAME)
    await expect(h.page.locator('.ctx-card-name')).toHaveText(NAME, { timeout: 10_000 })
    const saved = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])
    expect(saved.html).not.toContain(NAME)
  } finally {
    await releaseSceneWrites(h.page)
    await h.close()
  }
})

test('a delayed Context response cannot replace the next book', async () => {
  const h = await launchApp('nl-context-book-race-')
  try {
    await setup(h)
    const state = await h.rpc<{ activeBookId: string; books: { id: string; name: string }[] }>('project/createBook', ['Book Two'])
    const original = state.activeBookId
    const other = state.books.find((book) => book.name === 'Book Two')!.id
    await h.page.evaluate((id) => window.novalistStores.project.getState().switchBook(id), other)
    const created = await h.rpc<{ chapters: { guid: string }[] }>('project/createChapter', ['Other chapter'])
    const chapter = created.chapters[0]
    const next = await h.rpc<{ chapters: { scenes: { id: string }[] }[] }>('project/createScene', [chapter.guid, 'Other scene'])
    await h.rpc('scenes/write', [chapter.guid, next.chapters[0].scenes[0].id, '<p>A quiet ending.</p>', 'A quiet ending.'])
    await h.page.evaluate((id) => window.novalistStores.project.getState().switchBook(id), original)
    await enterWriting(h.page)
    await h.page.locator('.binder-scene-row').first().click()
    await expect(h.page.locator('.ctx-stats')).toBeVisible()
    await holdNextAnalysis(h.page)
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await h.page.keyboard.type(` ${NAME} arrived.`)
    await h.page.waitForFunction(() => (window as unknown as { contextAnalysisHeld: boolean }).contextAnalysisHeld)
    await h.page.evaluate((id) => window.novalistStores.project.getState().switchBook(id), other)
    await enterWriting(h.page)
    await h.page.locator('.binder-scene-row').first().click()
    await expect(h.page.locator('.ctx-stats')).toContainText('3 words')
    await releaseAnalysis(h.page)
    await h.page.evaluate(() => new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))))
    await expect(h.page.locator('.ctx-card')).toHaveCount(0)
    await expect(h.page.locator('.ctx-stats')).toContainText('3 words')
  } finally {
    await releaseAnalysis(h.page)
    await h.close()
  }
})
