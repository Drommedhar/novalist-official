import { expect, test } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local relay-server checkout.')

async function openCollaboration(app: Harness): Promise<void> {
  await dismissTour(app.page)
  await app.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
}

async function participants(app: Harness): Promise<string> {
  const session = await app.rpc<{ sessionId: string }>('collaboration/status')
  return JSON.stringify(await app.rpc('collaboration/participants', [session.sessionId]))
}

async function publishPresence(app: Harness, scene: string): Promise<void> {
  const session = await app.rpc<{ sessionId: string }>('collaboration/status')
  await app.rpc('collaboration/presence', [session.sessionId, scene, randomUUID(), null, true])
}

test('changing the relay address updates its login policy and rejects failed discovery', async () => {
  const provider = await identityProvider()
  const server = await collaborationServer(null)
  const app = await launchApp('novalist-auth-policy-switch-')
  try {
    await openCollaboration(app)
    const address = app.page.getByLabel('Server address', { exact: true })
    await address.fill(server.address)
    await app.page.getByLabel('Your name', { exact: true }).fill('Morgan')
    await address.fill(provider.address)
    await expect(app.page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled()
    await expect(app.page.getByLabel('Your name', { exact: true })).toHaveCount(0)
    await address.fill(provider.address + '/missing')
    await expect(app.page.getByRole('alert')).toBeVisible()
    await expect(app.page.getByLabel('Your name', { exact: true })).toHaveCount(0)
    await expect(app.page.getByRole('button', { name: 'Join shared project', exact: true })).toBeDisabled()
    await address.fill(server.address)
    await expect(app.page.getByLabel('Your name', { exact: true })).toHaveValue('Morgan')
  } finally { await app.close(); await server.close(); await provider.close() }
})

for (const authenticated of [false, true]) {
  test(`share, join and reconnect with ${authenticated ? 'automatic browser sign-in' : 'no account'}`, async () => {
    let subject = 'owner'
    const provider = authenticated ? await identityProvider(() => subject, account => account === 'owner' ? 'Morgan' : 'Alex') : undefined
    const server = await collaborationServer(provider?.address ?? null)
    const owner = await launchApp('novalist-auth-policy-owner-')
    let guest: Harness | undefined
    try {
      const book = await seedBook(owner, { Opening: ['Arrival'] })
      const chapter = book.chapters.find(item => item.title === 'Opening')!
      for (const [app, name] of [[owner, 'Morgan'], [guest = await launchApp('novalist-auth-policy-guest-'), 'Alex']] as const) {
        await app.app.evaluate(({ shell }, requiresLogin) => {
          shell.openExternal = async url => {
            if (!requiresLogin) throw new Error('Anonymous collaboration must not open a login browser.')
            const response = await fetch(url)
            if (!response.ok) throw new Error('The test login failed.')
          }
        }, authenticated)
        await openCollaboration(app)
        await app.page.getByLabel('Server address', { exact: true }).fill(server.address)
        if (authenticated) await expect(app.page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled()
        else {
          await app.page.getByLabel('Your name', { exact: true }).fill(name)
          await expect(app.page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0)
        }
      }
      await owner.page.getByRole('button', { name: 'Share this project', exact: true }).click()
      await expect(owner.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 35_000 })
      const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600_000).toISOString()])
      subject = 'guest'
      await guest.app.evaluate(({ dialog }, folder) => {
        dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
      }, guest.workDir)
      await guest.page.getByLabel('Invitation link', { exact: true }).fill(invitation)
      await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
      await guest.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
      await guest.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy)
      await publishPresence(owner, chapter.scenes[0].id)
      await publishPresence(guest, chapter.scenes[0].id)
      await expect.poll(() => participants(owner)).toContain('Alex')
      await expect.poll(() => participants(guest!)).toContain('Morgan')
      await owner.page.evaluate(({ chapter, scene }) => {
        document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="true"]')?.click()
        return window.novalistStores.project.getState().openScene(chapter, scene)
      }, { chapter: chapter.guid, scene: chapter.scenes[0].id })
      await owner.page.frameLocator('.editor-frame').locator('#editor').click()
      await owner.page.keyboard.type('Shared text.')
      await expect.poll(async () => (await guest!.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])).html).toContain('Shared text.')
      await guest.rpc('collaboration/end')
      await guest.page.reload()
      await guest.page.waitForFunction(() => Boolean(window.novalistStores?.shell.getState().backendVersion))
      await openCollaboration(guest)
      await guest.page.getByRole('button', { name: 'Reconnect to shared project', exact: true }).click()
      await expect(guest.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 35_000 })
      await publishPresence(guest, chapter.scenes[0].id)
      await expect.poll(() => participants(owner)).toContain('Alex')
      if (!authenticated) expect(await guest.rpc('collaboration/account/status', [server.address])).toBeNull()
    } finally {
      await guest?.close()
      await owner.close()
      await server.close()
      await provider?.close()
    }
  })
}
