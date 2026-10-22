import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('two desktop writers host and join a direct session, edit together and retain their copies', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-peer-owner-')
    participants.push(owner)
    const project = await seedBook(owner, { Opening: ['Arrival', 'Elsewhere'] })
    const chapter = project.chapters.find(value => value.title === 'Opening')!
    const scene = chapter.scenes[0]
    await owner.rpc('scenes/write', [chapter.guid, scene.id, '<p>Our direct session.</p>', 'Our direct session.'])
    await owner.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await owner.page.getByLabel('Connection type', { exact: true }).selectOption('steam')
    await expect(owner.page.getByText(/This build does not include Steamworks/)).toBeVisible()
    await expect(owner.page.getByRole('button', { name: 'Host Steam session', exact: true })).toBeDisabled()
    await expect(owner.page.getByRole('button', { name: 'Join Steam session', exact: true })).toBeDisabled()
    await owner.page.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await owner.page.getByLabel('Your name', { exact: true }).fill('Owner')
    await owner.page.getByRole('button', { name: 'Host direct session', exact: true }).click()
    await expect(owner.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 30_000 })
    await owner.page.getByRole('button', { name: 'Create invitation', exact: true }).click()
    const invitation = owner.page.getByLabel('Invitation link', { exact: true })
    await expect(invitation).toHaveValue(/^novalist:\/\/collaboration\/peer#/)
    const link = await invitation.inputValue()

    const guest = await launchApp('novalist-peer-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await guest.page.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await guest.page.getByLabel('Your name', { exact: true }).fill('Guest')
    await guest.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, guest.workDir)
    await guest.page.getByLabel('Invitation link', { exact: true }).fill(link)
    await guest.page.getByLabel('Local project folder name', { exact: true }).fill('Joined')
    await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
    await guest.page.getByRole('button', { name: 'Join direct session', exact: true }).click()
    await guest.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy)
    await dismissTour(guest.page)
    for (const participant of participants) {
      await participant.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
      await expect(participant.page.frameLocator('.editor-frame').locator('#editor')).toHaveText('Our direct session.')
    }
    const ownerEditor = owner.page.frameLocator('.editor-frame').locator('#editor')
    const guestEditor = guest.page.frameLocator('.editor-frame').locator('#editor')
    await ownerEditor.click()
    await owner.page.keyboard.press('Control+End')
    await owner.page.keyboard.type(' Together.')
    await expect(guestEditor).toHaveText('Our direct session. Together.', { timeout: 15_000 })
    await expect(guest.page.frameLocator('.editor-frame').locator('.nv-collaboration-label')).toHaveText('Owner')
    await guestEditor.click()
    await guest.page.keyboard.press('Control+End')
    await guest.page.keyboard.type(' Realtime.')
    await expect(ownerEditor).toHaveText('Our direct session. Together. Realtime.', { timeout: 15_000 })
    const elsewhere = chapter.scenes.find(value => value.title === 'Elsewhere')!
    await owner.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene),
      { chapter: chapter.guid, scene: elsewhere.id })
    await owner.page.frameLocator('.editor-frame').locator('#editor').click()
    const presence = guest.page.locator('.binder-scene-row').filter({ hasText: 'Elsewhere' }).locator('.collaboration-scene-badge')
    await expect(presence).toHaveAttribute('title', 'Editing: Owner')
    await expect(guest.page.locator('.binder-scene-row').filter({ hasText: 'Arrival' }).locator('.collaboration-scene-badge')).toHaveCount(0)
    await guest.page.evaluate(() => {
      window.novalistStores.manuscript.getState().setMode('outliner')
      window.novalistStores.shell.getState().setMainView('manuscript')
    })
    await expect(guest.page.locator('.outliner-scene-title').filter({ hasText: 'Elsewhere' }).locator('.collaboration-scene-badge'))
      .toHaveAttribute('title', 'Editing: Owner')
    await guest.page.evaluate(() => window.novalistStores.manuscript.getState().setMode('corkboard'))
    await expect(guest.page.locator('.corkboard-card-title').filter({ hasText: 'Elsewhere' }).locator('.collaboration-scene-badge'))
      .toHaveAttribute('title', 'Editing: Owner')
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await expect(guest.page.locator('.collaboration-scene-badge')).toHaveCount(0)
    const character = await guest.rpc<{ id: string }>('entities/create', ['character', 'Guest character'])
    await expect.poll(async () => (await owner.rpc<{ name: string }>('entities/get', ['character', character.id])).name).toBe('Guest character')
    for (const participant of participants.slice().reverse()) {
      await participant.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await participant.page.getByRole('button', { name: 'End this session', exact: true }).click()
      await expect(participant.page.getByRole('button', { name: 'Host direct session', exact: true })).toBeVisible()
      await expect(participant.page.getByRole('button', { name: 'End this session', exact: true })).toHaveCount(0)
      const saved = await participant.rpc<{ html: string; shared: unknown }>('scenes/read', [chapter.guid, scene.id])
      expect(saved.html).toContain('Our direct session. Together. Realtime.')
      expect(saved.shared).toBeNull()
    }
  } finally {
    for (const participant of participants.reverse()) await participant.close()
  }
})
