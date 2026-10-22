import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

interface ResearchNote { id: string; title: string; content: string }
declare global { interface Window { researchCollaborationSave: { pending: boolean; release?: () => void } } }

async function openSessionStatus(h: Harness): Promise<void> {
  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
  await expect(h.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
}

test('research refreshes in both desktop sessions without replacing a pending local draft', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-research-owner-')
    participants.push(owner)
    await seedBook(owner, { Opening: ['Arrival'] })
    const [note] = await owner.rpc<ResearchNote[]>('research/save', [null, 'Shared source', 'Note', 'Original source.', [], []])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    await openSessionStatus(owner)
    const link = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('novalist-research-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Guest'])
    await openSessionStatus(guest)
    for (const h of participants) {
      await h.page.evaluate(id => window.novalistStores.shell.getState().navigateToResearch(id), note.id)
      await expect(h.page.locator('.research-content .cm-content')).toHaveText('Original source.')
    }

    await owner.rpc('research/save', [note.id, 'Shared source', 'Note', 'Remote revision.', [], []])
    const editor = guest.page.locator('.research-content .cm-content')
    await expect(editor).toHaveText('Remote revision.')
    await guest.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.researchCollaborationSave = { pending: false }
      let delay = true
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'research/save' && delay) {
          delay = false
          window.researchCollaborationSave.pending = true
          await new Promise<void>(resolve => { window.researchCollaborationSave.release = resolve })
        }
        return original<T>(method, params)
      }
    })
    await editor.fill('My pending research draft.')
    await expect.poll(() => guest.page.evaluate(() => window.researchCollaborationSave.pending)).toBe(true)
    await owner.rpc('research/save', [null, 'New shared source', 'Note', 'Another source.', [], []])
    await expect(guest.page.locator('.codex-row', { hasText: 'New shared source' })).toBeVisible()
    await expect(editor).toHaveText('My pending research draft.')
    await guest.page.evaluate(() => window.researchCollaborationSave.release?.())
    await expect.poll(async () => (await guest.rpc<ResearchNote[]>('research/list')).find(value => value.id === note.id)?.content)
      .toBe('My pending research draft.')
    await owner.rpc('research/save', [note.id, 'Shared source', 'Note', 'A later shared revision.', [], []])
    await expect(editor).toHaveText('A later shared revision.')
    await owner.rpc('research/delete', [note.id])
    await expect(guest.page.locator('.codex-row', { hasText: /^Shared source/ })).toHaveCount(0)
    await expect(editor).toHaveCount(0)
  } finally {
    for (const h of participants.reverse()) {
      if (!h.page.isClosed()) await h.page.evaluate(() => window.researchCollaborationSave?.release?.())
      await h.close()
    }
  }
})
