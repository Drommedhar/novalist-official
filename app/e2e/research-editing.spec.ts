import { expect, test } from '@playwright/test'
import { launchApp, seedBook } from './harness'

interface ResearchNote {
  id: string
  title: string
  tags: string[]
  status: string
  rating: number
}

test('research editing preserves title, tags and lifecycle when an inbox note is filed', async () => {
  const h = await launchApp('nl-research-editing-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    const notes = await h.rpc<ResearchNote[]>('research/save', [
      null, 'Bridge source', 'Note', 'The bridge opened in 1755.', ['inbox'], []
    ])
    const id = notes[notes.length - 1].id
    const saved = async (): Promise<ResearchNote | undefined> =>
      (await h.rpc<ResearchNote[]>('research/list')).find((note) => note.id === id)

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.research-view .codex-row').filter({ hasText: 'Bridge source' }).click()
    await h.page.getByRole('textbox', { name: 'Title', exact: true }).fill('Bridge evidence')
    await h.page.locator('.research-metadata > summary').click()
    await expect.poll(async () => (await saved())?.title).toBe('Bridge evidence')

    const tag = h.page.getByPlaceholder('Add tag…', { exact: true })
    await tag.fill('source')
    await tag.press('Enter')
    await expect.poll(async () => (await saved())?.tags).toEqual(['inbox', 'source'])
    await h.page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Open')
    await expect.poll(async () => (await saved())?.status).toBe('Open')
    await h.page.getByRole('group', { name: 'Rating', exact: true })
      .getByRole('button', { name: '4 stars', exact: true }).click()
    await expect.poll(async () => (await saved())?.rating).toBe(4)

    await h.page.getByRole('button', { name: 'Keep as research note', exact: true }).click()
    await expect.poll(async () => await saved()).toMatchObject({
      title: 'Bridge evidence', tags: ['source'], status: 'Open', rating: 4
    })
    await expect(h.page.locator('.research-filing')).toHaveCount(0)
  } finally {
    await h.close()
  }
})
