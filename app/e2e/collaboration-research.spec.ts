import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { readFile, readdir } from 'node:fs/promises'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

interface ResearchNote { id: string; title: string; content: string; sharedProse?: { sessionId: string; itemId: string } }
declare global { interface Window { researchCollaborationSave: { pending: boolean; release?: () => void }; researchProseSave: { pending: boolean; release?: () => void } } }

async function openSessionStatus(h: Harness): Promise<void> {
  await h.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
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

    expect((await owner.rpc<ResearchNote[]>('research/list'))[0].sharedProse?.itemId).toBeTruthy()
    const ownerEditor = owner.page.locator('.research-content .cm-content')
    await ownerEditor.fill('Remote revision.')
    const editor = guest.page.locator('.research-content .cm-content')
    await expect(editor).toHaveText('Remote revision.')
    await guest.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.researchCollaborationSave = { pending: false }
      let delay = true
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'collaboration/writeField' && delay) {
          delay = false
          window.researchCollaborationSave.pending = true
          await new Promise<void>(resolve => { window.researchCollaborationSave.release = resolve })
        }
        return original<T>(method, params)
      }
    })
    const title = guest.page.locator('.research-title')
    await title.focus()
    await expect(title).not.toHaveAttribute('readonly')
    await title.fill('My pending research title')
    await title.blur()
    await expect.poll(() => guest.page.evaluate(() => window.researchCollaborationSave.pending)).toBe(true)
    const added = (await owner.rpc<ResearchNote[]>('research/save', [null, 'New shared source', 'Note', 'Another source.', [], []])).find(item => item.id !== note.id)!
    await expect(guest.page.locator('.codex-row', { hasText: 'New shared source' })).toBeVisible()
    await expect(guest.page.locator('.research-title')).toHaveValue('My pending research title')
    await ownerEditor.fill('Prose changes while metadata is pending.')
    await expect(editor).toHaveText('Prose changes while metadata is pending.')
    await guest.page.evaluate(() => window.researchCollaborationSave.release?.())
    await expect.poll(async () => (await guest.rpc<ResearchNote[]>('research/list')).find(value => value.id === note.id)?.title)
      .toBe('My pending research title')
    await ownerEditor.fill('A later shared revision.')
    await expect(editor).toHaveText('A later shared revision.')
    await bothWritersEditAndUndo(participants)
    for (const h of participants) await h.page.evaluate(id => window.novalistStores.shell.getState().navigateToResearch(id), added.id)
    await ownerEditor.fill('A newly shared note is editable too.')
    await expect(editor).toHaveText('A newly shared note is editable too.')
    for (const h of participants) await h.page.evaluate(id => window.novalistStores.shell.getState().navigateToResearch(id), note.id)
    await expect(editor).toContainText('A later shared revision.')
    await holdResearchProse(guest)
    await editor.press('Control+End')
    await editor.pressSequentially(' Saved after deletion.')
    await expect.poll(() => guest.page.evaluate(() => window.researchProseSave.pending)).toBe(true)
    await owner.rpc('research/delete', [note.id])
    await expect(guest.page.locator('.codex-row', { hasText: /^My pending research title/ })).toHaveCount(0)
    await expect(editor).toHaveCount(0)
    await guest.page.evaluate(() => window.researchProseSave.release?.())
    await expect.poll(async () => {
      const root = join(guest.workDir, 'Joined', 'Recovered edits')
      const paths = await readdir(root, { recursive: true }).catch(() => [])
      const copies = await Promise.all(paths.filter(path => path.endsWith('project.json')).map(path => readFile(join(root, path), 'utf8')))
      return copies.some(copy => copy.includes('Saved after deletion.'))
    }).toBe(true)
    expect((await guest.rpc<ResearchNote[]>('research/list')).some(item => item.id === note.id)).toBe(false)
  } finally {
    for (const h of participants.reverse()) {
      if (!h.page.isClosed()) await h.page.evaluate(() => window.researchCollaborationSave?.release?.())
      if (!h.page.isClosed()) await h.page.evaluate(() => window.researchProseSave?.release?.())
      await h.close()
    }
  }
})

async function holdResearchProse(h: Harness): Promise<void> {
  await h.page.evaluate(() => {
    const original = window.novalistRpc.request.bind(window.novalistRpc)
    window.researchProseSave = { pending: false }
    let delay = true
    window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
      if (method === 'collaboration/writeScene' && delay) {
        delay = false
        window.researchProseSave.pending = true
        await new Promise<void>(resolve => { window.researchProseSave.release = resolve })
      }
      return original<T>(method, params)
    }
  })
}

async function bothWritersEditAndUndo(participants: Harness[]): Promise<void> {
  const [owner, guest] = participants
  await Promise.all(participants.map(holdResearchProse))
  const first = owner.page.locator('.research-content .cm-content')
  const second = guest.page.locator('.research-content .cm-content')
  await first.press('Control+End')
  await first.pressSequentially(' Owner contribution.')
  await second.press('Control+End')
  await second.pressSequentially(' Guest contribution.')
  for (const h of participants) {
    await expect.poll(() => h.page.evaluate(() => window.researchProseSave.pending)).toBe(true)
    await h.page.evaluate(() => window.researchProseSave.release?.())
  }
  for (const editor of [first, second]) {
    await expect(editor).toContainText('Owner contribution.')
    await expect(editor).toContainText('Guest contribution.')
  }
  expect(await first.innerText()).toBe(await second.innerText())
  await second.press('Control+z')
  await expect(first).not.toContainText('Guest contribution.')
  await expect(first).toContainText('Owner contribution.')
}
