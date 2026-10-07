import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

interface Note { id: string; title: string; content: string; status: string; rating: number; tags: string[] }
interface Probe { fail: boolean; calls: number; release?: () => void }
declare global { interface Window { auditResearch: Probe } }

async function openResearch(h: Harness, title: string): Promise<void> {
  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
  await h.page.locator('.codex-row', { hasText: title }).click()
}

async function delayReply(h: Harness, route: string): Promise<void> {
  await h.page.evaluate((route) => {
    const original = window.novalistRpc.request.bind(window.novalistRpc)
    window.auditResearch = { fail: false, calls: 0 }
    window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
      const result = await original<T>(method, params)
      if (method === route && window.auditResearch.calls++ === 0) {
        await new Promise<void>(resolve => { window.auditResearch.release = resolve })
      }
      return result
    }
  }, route)
}

async function releaseReply(h: Harness): Promise<void> {
  await h.page.evaluate(async () => {
    window.auditResearch.release!()
    await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
  })
}

test('failed research writes survive unmount, veto window close, and retry both notes', async () => {
  const h = await launchApp('nl-research-retry-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'First note', 'Note', 'First original', [], []])
    await h.rpc('research/save', [null, 'Second note', 'Note', 'Second original', [], []])
    await h.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.auditResearch = { fail: true, calls: 0 }
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'research/save' && window.auditResearch.fail) {
          window.auditResearch.calls++
          throw new Error('Research acceptance disk failure')
        }
        return original<T>(method, params)
      }
    })
    for (const ordinal of ['First', 'Second']) {
      await openResearch(h, `${ordinal} note`)
      await h.page.getByRole('textbox', { name: 'Title', exact: true }).fill(`${ordinal} revised`)
      await h.page.locator('.research-content .cm-content').fill(`${ordinal} Markdown **draft**`)
    }
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await expect.poll(() => h.page.evaluate(() => window.auditResearch.calls)).toBeGreaterThan(1)
    const previousCalls = await h.page.evaluate(() => window.auditResearch.calls)
    await h.app.evaluate(({ BrowserWindow }) => { BrowserWindow.getAllWindows()[0].close() })
    await expect.poll(() => h.page.evaluate(() => window.auditResearch.calls)).toBeGreaterThan(previousCalls)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(false)
    expect(h.page.isClosed()).toBe(false)
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([
      { title: 'First note', content: 'First original' }, { title: 'Second note', content: 'Second original' }
    ])
    await h.page.evaluate(async () => {
      window.auditResearch.fail = false
      const state = window.novalistStores.project.getState()
      await state.switchBook(state.activeBookId!)
    })
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([
      { title: 'First revised', content: 'First Markdown **draft**' },
      { title: 'Second revised', content: 'Second Markdown **draft**' }
    ])
    await openResearch(h, 'Second revised')
    await expect(h.page.locator('.research-content .cm-content')).toHaveText('Second Markdown draft')
  } finally { await h.close() }
})

test('a delayed content save cannot revert status and rating changed afterward', async () => {
  const h = await launchApp('nl-research-lifecycle-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'Source', 'Note', 'Original', [], []])
    await openResearch(h, 'Source')
    await delayReply(h, 'research/save')
    await h.page.locator('.research-content .cm-content').fill('Saved draft')
    await h.page.waitForFunction(() => !!window.auditResearch.release)
    await h.page.locator('.research-metadata > summary').click()
    const status = h.page.getByRole('combobox', { name: 'Status', exact: true })
    const rating = h.page.getByRole('group', { name: 'Rating', exact: true }).getByRole('button', { name: '4 stars', exact: true })
    await status.selectOption('Open')
    await expect(status).toHaveValue('Open')
    await rating.click()
    await expect(rating).toHaveAttribute('aria-pressed', 'true')
    await releaseReply(h)
    await expect(status).toHaveValue('Open')
    await expect(rating).toHaveAttribute('aria-pressed', 'true')
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([{ content: 'Saved draft', status: 'Open', rating: 4 }])
  } finally { await h.close() }
})

test('a delayed lifecycle response preserves a later acknowledged title and content', async () => {
  const h = await launchApp('nl-research-content-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'Source', 'Note', 'Original', [], []])
    await openResearch(h, 'Source')
    await delayReply(h, 'research/setLifecycle')
    await h.page.locator('.research-metadata > summary').click()
    const status = h.page.getByRole('combobox', { name: 'Status', exact: true })
    await status.selectOption('Open')
    await h.page.waitForFunction(() => !!window.auditResearch.release)
    await h.page.getByRole('textbox', { name: 'Title', exact: true }).fill('Latest title')
    await h.page.locator('.research-content .cm-content').fill('Latest acknowledged content')
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([{ title: 'Latest title', content: 'Latest acknowledged content' }])
    await releaseReply(h)
    await expect(status).toHaveValue('Open')
    await expect(h.page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue('Latest title')
    await expect(h.page.locator('.research-content .cm-content')).toHaveText('Latest acknowledged content')
  } finally { await h.close() }
})

test('delaying note A cannot overwrite note B title, Markdown and tags', async () => {
  const h = await launchApp('nl-research-two-notes-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'First note', 'Note', 'First original', [], []])
    await h.rpc('research/save', [null, 'Second note', 'Note', 'Second original', [], []])
    await openResearch(h, 'First note')
    await delayReply(h, 'research/save')
    await h.page.locator('.research-content .cm-content').fill('First acknowledged content')
    await h.page.waitForFunction(() => !!window.auditResearch.release)
    await openResearch(h, 'Second note')
    const title = h.page.getByRole('textbox', { name: 'Title', exact: true })
    const prose = h.page.locator('.research-content .cm-content')
    await title.fill('Second revised')
    await prose.fill('Second acknowledged content')
    await h.page.locator('.research-metadata > summary').click()
    const tag = h.page.getByPlaceholder('Add tag…', { exact: true })
    await tag.fill('evidence')
    await tag.press('Enter')
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([
      { content: 'First acknowledged content' }, { title: 'Second revised', content: 'Second acknowledged content', tags: ['evidence'] }
    ])
    await releaseReply(h)
    await expect(title).toHaveValue('Second revised')
    await expect(prose).toHaveText('Second acknowledged content')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await openResearch(h, 'Second revised')
    await expect(prose).toHaveText('Second acknowledged content')
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([
      { content: 'First acknowledged content' }, { title: 'Second revised', content: 'Second acknowledged content', tags: ['evidence'] }
    ])
  } finally { await h.close() }
})

for (const field of ['title', 'Markdown']) test(`project close flushes focused research ${field} before reopening`, async () => {
  const h = await launchApp('nl-research-close-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    const { projectPath } = await h.rpc<{ projectPath: string }>('project/getState')
    await h.rpc('research/save', [null, 'Source', 'Note', 'Original', [], []])
    await openResearch(h, 'Source')
    const target = field === 'title'
      ? h.page.getByRole('textbox', { name: 'Title', exact: true })
      : h.page.locator('.research-content .cm-content')
    await target.fill('Focused draft survives close')
    await h.rpc('project/close')
    await h.rpc('project/open', [projectPath])
    const expected = field === 'title'
      ? { title: 'Focused draft survives close', content: 'Original' }
      : { title: 'Source', content: 'Focused draft survives close' }
    await expect.poll(() => h.rpc<Note[]>('research/list')).toMatchObject([expected])
    await openResearch(h, expected.title)
    await expect(target).toBeVisible()
    if (field === 'title') await expect(target).toHaveValue(expected.title)
    else await expect(target).toHaveText(expected.content)
  } finally { await h.close() }
})
