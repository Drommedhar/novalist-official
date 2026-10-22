import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { readFile, readdir, writeFile } from 'node:fs/promises'
import * as Y from 'yjs'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'
import type { ProjectStateDto } from '../src/renderer/src/stores/projectStore'

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
  for (const h of [owner, guest]) await h.page.getByRole('button', { name: 'Overview', exact: true }).click()
  const ownerAge = owner.page.getByRole('textbox', { name: 'Age', exact: true })
  const guestAge = guest.page.getByRole('textbox', { name: 'Age', exact: true })
  await ownerAge.focus()
  await expect(ownerAge).not.toHaveAttribute('readonly', '')
  await guestAge.focus()
  await expect(guest.page.getByText('Test Writer is editing this field.', { exact: true })).toBeVisible()
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
  for (const h of [owner, guest]) await h.page.getByRole('button', { name: 'Details', exact: true }).click()
  const prose = [owner, guest].map(h => h.page.locator('.cm-content[aria-label="Distinguishing Features"]'))
  await Promise.all(prose.map(editor => expect(editor).toBeVisible()))
  await prose[0].click()
  await owner.page.keyboard.type('Alice prose. ')
  await expect(prose[1]).toContainText('Alice prose.')
  await prose[1].click()
  await guest.page.keyboard.press('Control+End')
  await guest.page.keyboard.type('Bob prose.')
  await expect(prose[0]).toContainText('Bob prose.')
  await prose[0].click()
  await owner.page.keyboard.press('Control+z')
  await expect(prose[0]).toHaveText('Bob prose.')
  await expect(prose[1]).toHaveText('Bob prose.')
  await expect.poll(async () => (await guest.rpc<{ distinguishingFeatures: string }>('entities/get', ['character', entityId])).distinguishingFeatures).toBe('Bob prose.')
  for (const h of [owner, guest]) await h.page.getByRole('button', { name: 'Overview', exact: true }).click()
  const sectionId = crypto.randomUUID()
  await owner.rpc('entities/updateLists', ['character', entityId, null, [{ id: sectionId, title: 'Shared history', content: 'Beginning.' }], null])
  const histories = [owner, guest].map(h => h.page.locator('.cm-content[aria-label="Shared history"]'))
  for (const history of histories) await expect(history).toHaveText('Beginning.')
  await histories[1].click()
  await guest.page.keyboard.press('Control+End')
  await guest.page.keyboard.type(' Together.')
  await expect(histories[0]).toHaveText('Beginning. Together.')
  const title = owner.page.locator('.entity-section-title').last()
  await title.focus()
  await expect(title).not.toHaveAttribute('readonly')
  await title.fill('Renamed history')
  await title.blur()
  await expect.poll(async () => (await guest.rpc<{ sections: { title: string; content: string }[] }>('entities/get', ['character', entityId])).sections)
    .toMatchObject([{ title: 'Renamed history', content: 'Beginning. Together.' }])
}

async function createSharedContent(owner: Harness, guest: Harness): Promise<void> {
  const added = await guest.rpc<{ id: string }>('entities/create', ['character', 'Created together'])
  await expect.poll(async () => (await owner.rpc<{ id: string }[]>('entities/list', ['character']))
    .some(entity => entity.id === added.id), { timeout: 15_000 }).toBe(true)
  await editSharedCodex(owner, guest, added.id)

  const source = join(guest.workDir, 'shared-attachment.bin')
  const bytes = Buffer.from([0, 1, 2, 127, 128, 254, 255])
  await writeFile(source, bytes)
  await guest.rpc('attachments/add', ['character', added.id, source, 'Shared attachment'])
  await expect.poll(() => owner.rpc<unknown[]>('attachments/list', ['character', added.id])).toHaveLength(1)
  const attachments = await owner.rpc<{ fullPath: string }[]>('attachments/list', ['character', added.id])
  expect(await readFile(attachments[0].fullPath)).toEqual(bytes)

  await owner.rpc('research/save', [null, 'Shared source', 'Note', 'A source found together.', [], []])
  await expect.poll(() => guest.rpc('research/list')).toMatchObject([{ title: 'Shared source', content: 'A source found together.' }])

  const createdChapter = await guest.rpc<ProjectStateDto>('project/createChapter', ['Shared chapter'])
  const chapter = createdChapter.chapters.find(chapter => chapter.title === 'Shared chapter')!
  await expect.poll(async () => (await owner.rpc<ProjectStateDto>('project/getState')).chapters.some(item => item.guid === chapter.guid)).toBe(true)
  const createdScene = await guest.rpc<ProjectStateDto>('project/createScene', [chapter.guid, 'Shared scene'])
  const scene = createdScene.chapters.find(item => item.guid === chapter.guid)!.scenes[0]
  await expect.poll(async () => (await owner.rpc<ProjectStateDto>('project/getState')).chapters
    .find(item => item.guid === chapter.guid)?.scenes.some(item => item.id === scene.id)).toBe(true)
  for (const h of [owner, guest]) await h.page.evaluate(({ chapter, scene }) =>
    window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: scene.id })
  const editor = guest.page.frameLocator('.editor-frame').locator('#editor')
  await editor.click()
  await guest.page.keyboard.type('A newly shared scene.')
  await expect(owner.page.frameLocator('.editor-frame').locator('#editor')).toHaveText('A newly shared scene.')
  const target = createdScene.chapters.find(item => item.title === 'Opening')!
  await guest.rpc('project/moveScenes', [[scene.id], target.guid, 1])
  await expect.poll(async () => (await owner.rpc<ProjectStateDto>('project/getState')).chapters
    .find(item => item.guid === target.guid)?.scenes.some(item => item.id === scene.id)).toBe(true)
  const content = await guest.rpc<{ shared: { sessionId: string; itemId: string; state: string } }>('scenes/read', [target.guid, scene.id])
  const concurrent = new Y.Doc()
  Y.applyUpdate(concurrent, Buffer.from(content.shared.state, 'base64'))
  const previous = Y.encodeStateVector(concurrent)
  concurrent.getText('prose').insert(concurrent.getText('prose').length - 1, ' A late concurrent edit.')
  const update = Buffer.from(Y.encodeStateAsUpdate(concurrent, previous)).toString('base64')
  concurrent.destroy()
  await guest.rpc('project/deleteScene', [target.guid, scene.id])
  await guest.rpc('collaboration/writeScene', [content.shared.sessionId, content.shared.itemId, update])
  await expect.poll(async () => (await owner.rpc<ProjectStateDto>('project/getState')).chapters
    .some(item => item.scenes.some(candidate => candidate.id === scene.id))).toBe(false)
  const root = (await owner.rpc<ProjectStateDto>('project/getState')).projectPath!
  await expect.poll(async () => {
    const files = await readdir(join(root, 'Recovered edits'), { recursive: true })
    const prose = await Promise.all(files.filter(file => file.endsWith('.novalist'))
      .map(file => readFile(join(root, 'Recovered edits', file), 'utf8')))
    return prose.some(html => html.includes('A late concurrent edit.'))
  }).toBe(true)
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
    await createSharedContent(owner, guest)
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
