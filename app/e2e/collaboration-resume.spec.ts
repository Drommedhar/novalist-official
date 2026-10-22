import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local community-server checkout and PostgreSQL fixture.')

async function authenticate(h: Harness, server: string): Promise<void> {
  await dismissTour(h.page)
  await h.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
  await h.rpc('collaboration/account/signIn', [server])
}

test('a restarted writer reconnects to their retained project and creates an independent copy', async () => {
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
    const root = (await owner.rpc<{ projectPath: string }>('project/getState')).projectPath
    await owner.rpc('scenes/write', [chapter.guid, scene.id, '<p>Before restart.</p>', 'Before restart.'])
    await owner.rpc('settings/updateProject', [{ editorFontSize: 23 }])
    await authenticate(owner, server.address)
    const original = await owner.rpc<{ sessionId: string; projectId: string }>('collaboration/create', [server.address])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600000).toISOString()])
    subject = 'guest'
    const guest = await launchApp('novalist-resume-guest-')
    participants.push(guest)
    await authenticate(guest, server.address)
    await guest.rpc('collaboration/join', [server.address, invitation, join(guest.workDir, 'Joined')])
    await owner.rpc('collaboration/end')
    await owner.close()
    participants.splice(participants.indexOf(owner), 1)
    await guest.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
    const editor = guest.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Before restart.')
    await editor.click()
    await guest.page.keyboard.press('Control+End')
    await guest.page.keyboard.type(' Written while away.')
    await expect.poll(async () => (await guest.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Written while away.')

    const reopened = await launchApp('novalist-resume-reopened-', {}, owner.workDir)
    participants.push(reopened)
    await dismissTour(reopened.page)
    await reopened.rpc('project/open', [root])
    await dismissTour(reopened.page)
    await reopened.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    const resume = reopened.page.getByRole('button', { name: 'Reconnect to shared project', exact: true })
    await expect(resume).toBeEnabled()
    await resume.click()
    await expect(reopened.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
    const resumed = await reopened.rpc<{ sessionId: string; projectId: string }>('collaboration/status')
    expect(resumed.projectId).toBe(original.projectId)
    expect(resumed.sessionId).not.toBe(original.sessionId)
    expect((await reopened.rpc<{ overrides: { editorFontSize: number } }>('settings/get')).overrides.editorFontSize).toBe(23)
    await expect.poll(async () => (await reopened.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Written while away.')
    await reopened.rpc('collaboration/end')
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
