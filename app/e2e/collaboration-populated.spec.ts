import { expect, test, type Locator } from '@playwright/test'
import { createHash } from 'node:crypto'
import { cp, readFile, readdir, realpath, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative } from 'node:path'
import { dismissTour, launchApp, type Harness } from './harness'
import { collaborationServer } from './fixtures/collaborationServer'
import { identityProvider } from './fixtures/identityProvider'
import type { ProjectStateDto } from '../src/renderer/src/stores/projectTypes'

const corpus = process.env.NOVALIST_COLLABORATION_CORPUS
test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1' || !corpus,
  'Set NOVALIST_SYNC_INTEGRATION=1 and NOVALIST_COLLABORATION_CORPUS to test an isolated copy of an existing project.')

async function fileHashes(root: string, folder = ''): Promise<Record<string, string>> {
  const hashes: Record<string, string> = {}
  for (const entry of await readdir(join(root, folder), { withFileTypes: true })) {
    if (entry.name === '.git' || entry.isSymbolicLink()) continue
    const path = join(folder, entry.name)
    if (entry.isDirectory()) Object.assign(hashes, await fileHashes(root, path))
    else if (entry.isFile()) hashes[path] = createHash('sha256').update(await readFile(join(root, path))).digest('hex')
  }
  return hashes
}

function sharedFiles(hashes: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(hashes).filter(([path]) => {
    const parts = path.split(/[\\/]/)
    if (parts.some(part => ['Snapshots', 'Recovered edits', 'extensions'].includes(part))) return false
    if (parts[0] === '.novalist')
      return ['project.json', 'settings.json', 'darlings.json', 'gallery.json', 'analysis', 'wiki', 'narration'].includes(parts[1])
    return parts.every(part => !part.startsWith('.') || ['.book', '.nvchapter.json'].includes(part))
  }))
}

async function ready(writer: Harness): Promise<void> {
  await writer.page.waitForFunction(() => {
    const state = window.novalistStores.project.getState()
    return !state.workspaceBusy && !state.workspaceTransitionToken
  }, undefined, { timeout: 30_000 })
}

async function signIn(writer: Harness, address: string): Promise<void> {
  await dismissTour(writer.page)
  await writer.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
  await writer.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
  await writer.page.getByLabel(/^(Server address|Serveradresse)$/).fill(address)
  await writer.page.getByRole('button', { name: /^(Sign in|Anmelden)$/ }).click()
  await expect(writer.page.getByText(/^(Signed in as|Angemeldet als) Test Writer$/)).toBeVisible()
}

async function watchLayout(editor: Locator): Promise<void> {
  await expect(editor.locator('.nv-page').first()).toBeVisible()
  await editor.evaluate(element => {
    const first = element.querySelector('p')!
    const initialTop = first.getBoundingClientRect().top - element.getBoundingClientRect().top
    element.setAttribute('data-layout-monitor', 'running')
    element.setAttribute('data-layout-shift', '0')
    element.setAttribute('data-missing-pages', 'false')
    const sample = (): void => {
      if (element.getAttribute('data-layout-monitor') !== 'running') return
      if (!element.querySelector('.nv-page')) element.setAttribute('data-missing-pages', 'true')
      const current = element.querySelector('p')
      if (current) {
        const shift = Math.abs(current.getBoundingClientRect().top - element.getBoundingClientRect().top - initialTop)
        element.setAttribute('data-layout-shift', String(Math.max(shift, Number(element.getAttribute('data-layout-shift')))))
      }
      requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
  })
}

async function closeNormally(writer: Harness): Promise<void> {
  const closed = writer.page.waitForEvent('close', { timeout: 20_000 })
  await writer.app.evaluate(({ BrowserWindow }) => { BrowserWindow.getAllWindows()[0].close() })
  await closed
}

async function clean(writer: Harness): Promise<void> {
  await writer.close()
  const target = await realpath(writer.workDir)
  if (dirname(target).toLowerCase() !== (await realpath(tmpdir())).toLowerCase() ||
      !basename(target).startsWith('novalist-populated-collab-')) throw new Error('Unsafe populated-project test cleanup target.')
  await rm(target, { recursive: true, force: true })
}

test('an existing populated project shares, joins a new folder, edits, opens Manuscript and closes through the relay', async () => {
  test.setTimeout(240_000)
  const source = await realpath(corpus!)
  const original = await fileHashes(source)
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const writers: Harness[] = []
  try {
    const owner = await launchApp('novalist-populated-collab-owner-', { NOVALIST_DISABLE_SELF_UPDATE: '1' })
    writers.push(owner)
    await dismissTour(owner.page)
    const ownerRoot = join(owner.workDir, 'Project')
    await cp(source, ownerRoot, { recursive: true, filter: path => !relative(source, path).split(/[\\/]/).includes('.git') })
    const project = await owner.rpc<ProjectStateDto>('project/open', [ownerRoot])
    await ready(owner)
    const chapter = project.chapters.find(chapter => chapter.scenes.some(scene => !scene.inactive))!
    const scene = chapter.scenes.find(scene => !scene.inactive)!
    const sceneCount = project.chapters.flatMap(chapter => chapter.scenes).filter(scene => !scene.inactive).length
    expect(sceneCount).toBeGreaterThan(1)
    const beforeSharing = sharedFiles(await fileHashes(ownerRoot))

    await test.step('Share the populated project from Settings without rewriting its files', async () => {
      await signIn(owner, server.address)
      await owner.page.getByRole('button', { name: /^(Share this project|Dieses Projekt teilen)$/ }).click()
      await expect(owner.page.getByText(/^(Connected to the shared project\.|Mit dem geteilten Projekt verbunden\.)$/))
        .toBeVisible({ timeout: 45_000 })
      await ready(owner)
      expect(sharedFiles(await fileHashes(ownerRoot))).toEqual(beforeSharing)
    })
    await owner.page.getByRole('button', { name: /^(Create invitation|Einladung erstellen)$/ }).click()
    const invitation = await owner.page.getByLabel(/^(Invitation link|Einladungslink)$/).inputValue()
    expect(invitation).toMatch(/^novalist:\/\/collaboration\/relay#/)

    subject = 'guest'
    const guest = await launchApp('novalist-populated-collab-guest-', { NOVALIST_DISABLE_SELF_UPDATE: '1' })
    writers.push(guest)
    const guestRoot = join(guest.workDir, 'Joined')
    await test.step('Join through Settings using a new local folder', async () => {
      await signIn(guest, server.address)
      await guest.page.getByLabel('Invitation link', { exact: true }).fill(invitation)
      await expect(guest.page.getByLabel('Use an existing local project', { exact: true })).not.toBeChecked()
      await guest.page.getByLabel('Local project folder name', { exact: true }).fill('Joined')
      await guest.app.evaluate(({ dialog }, folder) => {
        dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
      }, guest.workDir)
      await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
      await expect(guest.page.getByRole('button', { name: 'Join shared project', exact: true })).toBeEnabled()
      await guest.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
      await guest.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded, undefined, { timeout: 60_000 })
      await ready(guest)
      expect((await guest.rpc<ProjectStateDto>('project/getState')).projectPath).toBe(guestRoot)
    })

    for (const writer of writers) {
      await dismissTour(writer.page)
      await writer.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene),
        { chapter: chapter.guid, scene: scene.id })
    }
    const ownerEditor = owner.page.frameLocator('iframe[title="editor"]').locator('#editor')
    const guestEditor = guest.page.frameLocator('iframe[title="editor"]').locator('#editor')
    await watchLayout(ownerEditor)
    await watchLayout(guestEditor)
    await test.step('Type from both windows without losing the page layout', async () => {
      for (const [writer, editor, remote, text] of [
        [owner, ownerEditor, guestEditor, ' Relay owner verification.'],
        [guest, guestEditor, ownerEditor, ' Relay guest verification.']
      ] as const) {
        await editor.click()
        await writer.page.keyboard.press('Control+End')
        await writer.page.keyboard.type(text, { delay: 60 })
        await expect(remote).toContainText(text.trim(), { timeout: 15_000 })
      }
      for (const editor of [ownerEditor, guestEditor]) {
        await expect(editor).toHaveAttribute('data-missing-pages', 'false')
        expect(Number(await editor.getAttribute('data-layout-shift'))).toBeLessThan(2)
        await editor.evaluate(element => element.setAttribute('data-layout-monitor', 'stopped'))
      }
    })

    await test.step(`Open all ${sceneCount} active scenes in Manuscript`, async () => {
      await owner.page.evaluate(() => {
        window.novalistStores.manuscript.getState().setMode('manuscript')
        window.novalistStores.shell.getState().setMainView('manuscript')
      })
      const manuscript = owner.page.frameLocator('iframe[title="manuscript"]')
      await expect(manuscript.locator('.scene-editor')).toHaveCount(sceneCount, { timeout: 30_000 })
      await expect(manuscript.locator(`.scene-editor[data-scene-id="${scene.id}"]`)).toContainText('Relay guest verification.')
    })

    const savedScenes = await Promise.all(writers.map(writer => writer.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])))
    expect(savedScenes[0].html).toBe(savedScenes[1].html)
    await test.step('Close both normal windows and retain their local writing', async () => {
      await closeNormally(guest)
      await closeNormally(owner)
      for (const root of [ownerRoot, guestRoot]) {
        const paths = Object.keys(await fileHashes(root)).filter(path => path.endsWith('.novalist'))
        const contents = await Promise.all(paths.map(path => readFile(join(root, path), 'utf8')))
        expect(contents.some(content => content.includes('Relay owner verification.') && content.includes('Relay guest verification.'))).toBe(true)
        expect(JSON.parse(await readFile(join(root, '.novalist/project.json'), 'utf8')).id).toBeTruthy()
      }
    })
  } finally {
    for (const writer of writers.reverse()) await clean(writer)
    await server.close()
    await identity.close()
    expect(await fileHashes(source)).toEqual(original)
  }
})
