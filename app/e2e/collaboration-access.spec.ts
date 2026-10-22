import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { readFile } from 'node:fs/promises'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local relay-server checkout.')

async function signIn(h: Harness, server: string): Promise<void> {
  await dismissTour(h.page)
  await h.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
  await h.page.getByLabel('Server address', { exact: true }).fill(server)
  await h.page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(h.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
}

async function joinProject(h: Harness, link: string, folder = 'Joined'): Promise<void> {
  await h.app.evaluate(({ dialog }, folder) => {
    dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
  }, h.workDir)
  await h.page.getByLabel('Invitation link', { exact: true }).fill(link)
  await h.page.getByLabel('Local project folder name', { exact: true }).fill(folder)
  await h.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
  await h.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
  await h.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy)
  await dismissTour(h.page)
  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
}

test('host removal disconnects one writer while others continue and everyone retains a local project', async () => {
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-access-owner-')
    participants.push(owner)
    await seedBook(owner, { Opening: ['Arrival'] })
    await signIn(owner, server.address)
    await owner.page.getByRole('button', { name: 'Share this project', exact: true }).click()
    await expect(owner.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await owner.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    const invitation = owner.page.getByLabel('Invitation link', { exact: true })
    await expect(invitation).toHaveValue(/^novalist:/)
    const link = await invitation.inputValue()
    subject = 'guest'
    const guest = await launchApp('novalist-access-guest-')
    participants.push(guest)
    await signIn(guest, server.address)
    const destination = join(guest.workDir, 'Joined')
    await joinProject(guest, link)
    await owner.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(guest.page.getByRole('heading', { name: 'Manage shared access' })).toHaveCount(0)
    const members = await owner.rpc<{ displayName: string; role: string }[]>('collaboration/members')
    const removed = members.find(member => member.role === 'Contributor')!
    await owner.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    await expect(invitation).toHaveValue(/^novalist:/)
    const nextLink = await invitation.inputValue()
    subject = 'remaining'
    const remaining = await launchApp('novalist-access-remaining-')
    participants.push(remaining)
    await signIn(remaining, server.address)
    await joinProject(remaining, nextLink)
    owner.page.on('dialog', dialog => void dialog.accept())
    await owner.page.locator('.match-row').filter({ hasText: removed.displayName }).getByRole('button', { name: 'Remove access', exact: true }).click()
    await expect.poll(() => owner.rpc('collaboration/status'), { timeout: 30_000 }).toMatchObject({ connection: 'Connected' })
    await owner.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(owner.page.getByRole('button', { name: 'Remove access', exact: true })).toHaveCount(1)
    await expect(guest.page.getByText('Access to this shared project has ended. End this session to continue with your local copy.', { exact: true })).toBeVisible()
    await expect(remaining.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await remaining.rpc('research/save', [null, 'After removal', 'Note', 'Still writing together.', [], []])
    await expect.poll(() => owner.rpc('research/list')).toMatchObject([{ title: 'After removal' }])
    await owner.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    await expect(invitation).toHaveValue(/^novalist:/)
    const replacementLink = await invitation.inputValue()
    await expect(guest.page.getByRole('button', { name: 'Join shared project', exact: true })).toHaveCount(0)
    await guest.page.getByRole('button', { name: 'End this session', exact: true }).click()
    await expect(guest.page.getByRole('button', { name: 'Share this project', exact: true })).toBeEnabled()
    await guest.rpc('research/save', [null, 'Private retained writing', 'Note', 'Keep this local draft.', [], []])
    const retained = await readFile(join(destination, '.novalist', 'project.json'), 'utf8')
    await joinProject(guest, replacementLink, 'Rejoined')
    await expect(guest.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    expect(await readFile(join(destination, '.novalist', 'project.json'), 'utf8')).toBe(retained)
    await expect.poll(() => guest.rpc('research/list')).toMatchObject([{ title: 'After removal' }])
    await guest.rpc('research/save', [null, 'Writing after new invitation', 'Note', 'Invited again.', [], []])
    await expect.poll(() => owner.rpc<unknown[]>('research/list')).toHaveLength(2)
    await owner.page.getByRole('button', { name: 'End session for everyone', exact: true }).click()
    await expect(owner.page.getByRole('button', { name: 'Share this project', exact: true })).toBeEnabled()
    expect(await owner.rpc('collaboration/status')).toBeNull()
    await guest.page.getByRole('button', { name: 'End this session', exact: true }).click()
    await expect(guest.page.getByRole('button', { name: 'Share this project', exact: true })).toBeEnabled()
    expect(await guest.rpc('collaboration/status')).toBeNull()
    expect((await guest.rpc<{ projectPath: string }>('project/getState')).projectPath).toBe(join(guest.workDir, 'Rejoined'))
    expect((await owner.rpc<{ projectPath: string }>('project/getState')).projectPath).toBeTruthy()
    await remaining.page.getByRole('button', { name: 'End this session', exact: true }).click()
    await expect(remaining.page.getByRole('button', { name: 'Share this project', exact: true })).toBeEnabled()
    expect((await remaining.rpc<{ projectPath: string }>('project/getState')).projectPath).toBe(join(remaining.workDir, 'Joined'))
  } finally {
    for (const participant of participants.reverse()) await participant.close()
    await server.close()
    await identity.close()
  }
})
