import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { readFile } from 'node:fs/promises'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local community-server checkout and PostgreSQL fixture.')

async function signIn(h: Harness, server: string): Promise<void> {
  await dismissTour(h.page)
  await h.app.evaluate(({ shell }) => {
    shell.openExternal = async url => {
      const result = await fetch(url)
      if (!result.ok) throw new Error('Test sign-in failed.')
    }
  })
  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
  await h.page.getByLabel('Server address', { exact: true }).fill(server)
  await h.page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(h.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
}

async function editSharedCodex(owner: Harness, guest: Harness, entityId: string): Promise<void> {
  for (const h of [owner, guest]) await h.page.evaluate(async id => {
    window.novalistStores.shell.getState().setMainView('codex')
    await window.novalistStores.codex.getState().setType('character')
    await window.novalistStores.codex.getState().select(id)
  }, entityId)
  const ownerAge = owner.page.getByRole('textbox', { name: 'Age', exact: true })
  const guestAge = guest.page.getByRole('textbox', { name: 'Age', exact: true })
  await ownerAge.focus()
  await expect(ownerAge).not.toHaveAttribute('readonly', '')
  await guestAge.focus()
  await expect(guest.page.getByText('Another writer is editing this field.', { exact: true })).toBeVisible()
  await ownerAge.fill('43')
  await ownerAge.blur()
  await expect(ownerAge).toHaveAttribute('readonly', '')
  await expect.poll(async () => (await guest.rpc<{ age: string }>('entities/get', ['character', entityId])).age).toBe('43')
  await guestAge.blur()
  await expect(guestAge).toHaveValue('43')
  await guestAge.focus()
  await expect(guestAge).not.toHaveAttribute('readonly', '')
  await guestAge.fill('44')
  await guestAge.blur()
  await expect(ownerAge).toHaveValue('44')
}

test('two desktop writers join through Settings, exchange real editor updates, and retain their copies', async () => {
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-live-owner-')
    participants.push(owner)
    const project = await seedBook(owner, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const scene = chapter.scenes[0]
    await owner.rpc('scenes/write', [chapter.guid, scene.id, '<p>Our story.</p>', 'Our story.'])
    const character = await owner.rpc<{ id: string }>('entities/create', ['character', 'Writer'])
    await owner.rpc('entities/update', ['character', character.id, { age: '42' }])
    const worldCharacter = await owner.rpc<{ id: string }>('entities/create', ['character', 'World Writer'])
    await owner.rpc('entities/moveToWorldBible', ['character', worldCharacter.id])
    await owner.rpc('entities/update', ['character', worldCharacter.id, { age: '42' }])
    await signIn(owner, server.address)
    await owner.page.getByRole('button', { name: 'Share this project', exact: true }).click()
    await expect(owner.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(owner.page.getByRole('button', { name: 'Sign out', exact: true })).toBeDisabled()
    await owner.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    const invitation = owner.page.getByLabel('Invitation link', { exact: true })
    await expect(invitation).toHaveValue(/^novalist:/)
    const link = await invitation.inputValue()

    subject = 'guest'
    const guest = await launchApp('novalist-live-guest-')
    participants.push(guest)
    await signIn(guest, server.address)
    await guest.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, guest.workDir)
    await guest.page.getByLabel('Invitation link', { exact: true }).fill(link)
    await guest.page.getByLabel('Local project folder name', { exact: true }).fill('Joined')
    await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
    await guest.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
    await guest.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy)
    await dismissTour(guest.page)
    await guest.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(guest.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(guest.page.getByRole('button', { name: 'Create invitation', exact: true })).toHaveCount(0)
    for (const h of participants) {
      await h.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
      await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toHaveText('Our story.')
    }
    const ownerEditor = owner.page.frameLocator('.editor-frame').locator('#editor')
    const guestEditor = guest.page.frameLocator('.editor-frame').locator('#editor')
    await ownerEditor.click()
    await owner.page.keyboard.press('Control+End')
    await owner.page.keyboard.type(' Owner.')
    await expect(guestEditor).toHaveText('Our story. Owner.', { timeout: 15_000 })
    await expect(guest.page.frameLocator('.editor-frame').locator('.nv-collaboration-label')).toHaveText('Test Writer')
    await guestEditor.click()
    await guest.page.keyboard.press('Control+End')
    await guest.page.keyboard.type(' Guest.')
    await expect(ownerEditor).toHaveText('Our story. Owner. Guest.', { timeout: 15_000 })
    await expect(owner.page.frameLocator('.editor-frame').locator('.nv-collaboration-label')).toHaveText('Test Writer')
    await editSharedCodex(owner, guest, character.id)
    await editSharedCodex(owner, guest, worldCharacter.id)
    for (const h of participants) {
      await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await h.page.getByRole('button', { name: 'End this session', exact: true }).click()
      await expect(h.page.getByRole('button', { name: 'Share this project', exact: true })).toBeEnabled()
      const saved = await h.rpc<{ html: string; shared: unknown }>('scenes/read', [chapter.guid, scene.id])
      expect(saved.html).toContain('Our story. Owner. Guest.')
      expect(saved.shared).toBeNull()
      expect((await h.rpc<{ age: string }>('entities/get', ['character', character.id])).age).toBe('44')
    }
    const metadata = JSON.parse(await readFile(join(guest.workDir, 'Joined/.novalist/project.json'), 'utf8'))
    expect(metadata.name).toBe('Spec')
  } finally {
    for (const h of participants.reverse()) await h.close()
    await server.close()
    await identity.close()
  }
})
