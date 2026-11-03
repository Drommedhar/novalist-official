import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local relay-server checkout.')

async function authenticate(h: Harness, server: string): Promise<void> {
  await dismissTour(h.page)
  await h.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
  await h.rpc('collaboration/account/signIn', [server])
}

test('a restarted guest reconnects to the running host and creates an independent copy', async () => {
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-resume-owner-')
    participants.push(owner)
    const project = await seedBook(owner, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(value => value.title === 'Opening')!
    const scene = chapter.scenes[0]
    await owner.rpc('scenes/write', [chapter.guid, scene.id, '<p>Before restart.</p>', 'Before restart.'])
    await authenticate(owner, server.address)
    await owner.rpc('collaboration/create', [server.address])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600000).toISOString()])
    subject = 'guest'
    const guest = await launchApp('novalist-resume-guest-')
    participants.push(guest)
    await authenticate(guest, server.address)
    const root = join(guest.workDir, 'Joined')
    const original = await guest.rpc<{ sessionId: string; projectId: string }>('collaboration/join', [server.address, invitation, root])
    await guest.rpc('settings/updateProject', [{ editorFontSize: 23 }])
    await guest.rpc('collaboration/end')
    await guest.close()
    participants.splice(participants.indexOf(guest), 1)
    await owner.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
    const editor = owner.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Before restart.')
    await editor.click()
    await owner.page.keyboard.press('Control+End')
    await owner.page.keyboard.type(' Written while away.')
    await expect.poll(async () => (await owner.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Written while away.')
    const createdWhileAway = await owner.rpc<{ id: string }>('entities/create', ['character', 'Created while guest offline'])

    const reopened = await launchApp('novalist-resume-reopened-', {}, guest.workDir)
    participants.push(reopened)
    await dismissTour(reopened.page)
    await reopened.rpc('project/open', [root])
    await dismissTour(reopened.page)
    await reopened.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
    const resume = reopened.page.getByRole('button', { name: 'Reconnect to shared project', exact: true })
    await expect(resume).toBeEnabled()
    await resume.click()
    await expect(reopened.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
    const resumed = await reopened.rpc<{ sessionId: string; projectId: string }>('collaboration/status')
    expect(resumed.projectId).toBe(original.projectId)
    expect(resumed.sessionId).not.toBe(original.sessionId)
    expect((await reopened.rpc<{ overrides: { editorFontSize: number } }>('settings/get')).overrides.editorFontSize).toBe(23)
    await expect.poll(async () => (await reopened.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Written while away.')
    expect((await reopened.rpc<{ id: string }[]>('entities/list', ['character'])).some(entity => entity.id === createdWhileAway.id)).toBe(true)
    await reopened.rpc('collaboration/end')
    await reopened.page.getByRole('button', { name: 'Join', exact: true }).click()
    await reopened.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, reopened.workDir)
    await reopened.page.getByLabel('Local project folder name', { exact: true }).fill('Independent')
    await reopened.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
    await reopened.page.getByRole('button', { name: 'Create independent copy', exact: true }).click()
    await expect(reopened.page.getByText('Independent project copy created.', { exact: true })).toBeVisible()
    await reopened.rpc('project/open', [join(reopened.workDir, 'Independent')])
    expect(await reopened.rpc('collaboration/saved')).toBeNull()
    expect((await reopened.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Written while away.')
  } finally {
    for (const participant of participants.reverse()) await participant.close()
    await server.close()
    await identity.close()
  }
})
