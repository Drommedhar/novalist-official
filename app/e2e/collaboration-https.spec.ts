import { expect, test } from '@playwright/test'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test.skip(!process.env.NOVALIST_COLLAB_TLS_DIR, 'Run with eng/Test-CollaborationHttps.ps1 to install temporary certificate trust.')

async function signIn(desktop: Harness, server: string): Promise<void> {
  await dismissTour(desktop.page)
  await desktop.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
  await desktop.page.getByRole('button', { name: 'Collaboration', exact: true }).click()
  await desktop.page.getByLabel('Server address', { exact: true }).fill(server)
  await desktop.page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(desktop.page.getByText(/^Signed in as HTTPS /)).toBeVisible()
}

test('two desktops sign in, share, join and edit through HTTPS and WSS with certificate validation', async () => {
  const directory = process.env.NOVALIST_COLLAB_TLS_DIR!
  const tls = { cert: await readFile(join(directory, 'trusted.pem')), key: await readFile(join(directory, 'trusted.key')) }
  let subject = 'Host'
  const identity = await identityProvider(() => subject, value => 'HTTPS ' + value, 0, false, tls)
  const server = await collaborationServer(identity.address, 0, { path: join(directory, 'trusted.pfx'), password: process.env.NOVALIST_COLLAB_TLS_PASSWORD! })
  const host = await launchApp('novalist-https-host-')
  let guest: Harness | undefined
  try {
    expect(server.address).toMatch(/^https:/)
    expect(identity.address).toMatch(/^https:/)
    const project = await seedBook(host, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(value => value.title === 'Opening')!
    const scene = chapter.scenes[0]
    await host.rpc('scenes/write', [chapter.guid, scene.id, '<p>Encrypted collaboration.</p>', 'Encrypted collaboration.'])
    await signIn(host, server.address)
    await host.page.getByRole('button', { name: 'Share this project', exact: true }).click()
    await expect(host.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await host.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    const invitation = await host.page.getByLabel('Invitation link', { exact: true }).inputValue()
    subject = 'Guest'
    guest = await launchApp('novalist-https-guest-')
    await signIn(guest, server.address)
    await guest.page.getByLabel('Invitation link', { exact: true }).fill(invitation)
    await guest.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, guest.workDir)
    await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
    await guest.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
    await expect.poll(() => guest!.page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(true)
    for (const desktop of [host, guest]) {
      const panel = desktop.page.getByRole('dialog', { name: 'Collaboration', exact: true })
      if (await panel.isVisible()) await panel.getByRole('button', { name: 'Close', exact: true }).click()
      await dismissTour(desktop.page)
      await desktop.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
    }
    const hostEditor = host.page.frameLocator('.editor-frame').locator('#editor')
    const guestEditor = guest.page.frameLocator('.editor-frame').locator('#editor')
    await expect(guestEditor).toHaveText('Encrypted collaboration.')
    await hostEditor.click()
    await host.page.keyboard.press('Control+End')
    await host.page.keyboard.type(' From host.')
    await expect(guestEditor).toContainText('From host.', { timeout: 15_000 })
    await expect(guest.page.frameLocator('.editor-frame').locator('.nv-collaboration-label')).toHaveText('HTTPS Host')
    await guestEditor.click()
    await guest.page.keyboard.press('Control+End')
    await guest.page.keyboard.type(' From guest.')
    await expect(hostEditor).toContainText('From guest.', { timeout: 15_000 })
    await guest.rpc('collaboration/end')
    await host.rpc('collaboration/end')
  } finally { await guest?.close(); await host.close(); await server.close(); await identity.close() }
})

test('desktop sign-in rejects an untrusted HTTPS certificate', async () => {
  const directory = process.env.NOVALIST_COLLAB_TLS_DIR!
  const provider = await identityProvider(undefined, undefined, 0, true,
    { cert: await readFile(join(directory, 'untrusted.pem')), key: await readFile(join(directory, 'untrusted.key')) })
  const desktop = await launchApp('novalist-https-untrusted-')
  try {
    await expect(desktop.rpc('collaboration/account/signIn', [provider.address])).rejects.toThrow(/SSL|certificate|trust/i)
  } finally { await desktop.close(); await provider.close() }
})
