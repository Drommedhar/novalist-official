import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { dismissTour, launchApp, seedBook } from './harness'
import { identityProvider } from './fixtures/identityProvider'
import { collaborationServer } from './fixtures/collaborationServer'

test.skip(process.env.NOVALIST_SYNC_INTEGRATION !== '1', 'Requires the local relay-server checkout.')

test('existing empty paragraphs and grammar highlights survive sharing and joining', async () => {
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const owner = await launchApp('novalist-sharing-')
  let guest: Awaited<ReturnType<typeof launchApp>> | undefined
  try {
    const project = await seedBook(owner, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const html = '<p>Before.</p><p></p><p><br></p><p><span class="grammar-issue" data-issue-index="0"><b>Marked</b></span> after.</p>'
    const expected = '<p>Before.</p><p></p><p><br></p><p><b>Marked</b> after.</p>'
    await owner.rpc('scenes/write', [chapter.guid, chapter.scenes[0].id, html, 'Before.\n\n\nMarked after.'])
    await dismissTour(owner.page)
    await owner.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await owner.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await owner.page.getByLabel('Server address', { exact: true }).fill(server.address)
    await owner.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(owner.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
    await owner.page.evaluate(() => {
      const observed = document.createElement('div')
      observed.id = 'host-progress-observed'
      observed.hidden = true
      document.body.append(observed)
      new MutationObserver(() => {
        const dialog = document.querySelector('[role="dialog"][aria-label="Synchronization progress"]')
        if (dialog?.textContent?.includes('transferred')) observed.dataset.claimedTransfer = 'true'
        if (dialog?.textContent?.includes('Project synchronized.')) observed.dataset.claimedComplete = 'true'
      }).observe(document.body, { childList: true, subtree: true, characterData: true })
    })
    await owner.page.getByRole('button', { name: 'Share this project', exact: true }).click()
    await expect(owner.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible({ timeout: 35_000 })
    await expect(owner.page.getByRole('dialog', { name: 'Synchronization progress' })).toHaveCount(0)
    await expect(owner.page.locator('#host-progress-observed')).not.toHaveAttribute('data-claimed-transfer', 'true')
    await expect(owner.page.locator('#host-progress-observed')).not.toHaveAttribute('data-claimed-complete', 'true')
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600_000).toISOString()])
    subject = 'guest'
    guest = await launchApp('novalist-sharing-guest-')
    await guest.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await guest.rpc('collaboration/account/signIn', [server.address])
    await guest.rpc('collaboration/join', [server.address, invitation, join(guest.workDir, 'Joined')])
    for (const participant of [owner, guest]) {
      const scene = await participant.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])
      expect(scene.html).toBe(expected)
    }
  } finally {
    await guest?.close()
    await owner.close()
    await server.close()
    await identity.close()
  }
})

test('relay joining reuses an existing copy, recovers local differences and requires a live host', async () => {
  let subject = 'owner'
  const identity = await identityProvider(() => subject)
  const server = await collaborationServer(identity.address)
  const owner = await launchApp('novalist-incremental-owner-')
  let guest: Awaited<ReturnType<typeof launchApp>> | undefined
  try {
    await seedBook(owner, { Opening: ['Arrival'] })
    const root = (await owner.rpc<{ projectPath: string }>('project/getState')).projectPath
    await mkdir(join(root, 'Research'), { recursive: true })
    const unchanged = Buffer.alloc(2 * 1024 * 1024, 71)
    const missing = Buffer.alloc(1024 * 1024 + 123, 86)
    await writeFile(join(root, 'Research', 'matching.bin'), unchanged)
    await writeFile(join(root, 'Research', 'changed.txt'), 'Host version.')
    guest = await launchApp('novalist-incremental-guest-')
    const destination = join(guest.workDir, 'Existing')
    await cp(root, destination, { recursive: true })
    await writeFile(join(destination, 'Research', 'changed.txt'), 'Keep this local difference.')
    await writeFile(join(root, 'Research', 'missing.bin'), missing)
    await mkdir(join(destination, '.git'), { recursive: true })
    await writeFile(join(destination, '.git', 'local-only'), 'Retain local history.')

    await owner.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await owner.rpc('collaboration/account/signIn', [server.address])
    await owner.rpc('collaboration/create', [server.address])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600_000).toISOString()])
    subject = 'guest'
    await guest.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await dismissTour(guest.page)
    await guest.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await guest.page.getByLabel('Server address', { exact: true }).fill(server.address)
    await guest.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(guest.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
    await guest.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, destination)
    await guest.page.getByLabel('Invitation link', { exact: true }).fill(invitation)
    await guest.page.getByLabel('Use an existing local project', { exact: true }).check()
    await guest.page.getByRole('button', { name: 'Choose existing project folder', exact: true }).click()
    await guest.page.getByRole('button', { name: 'Join shared project', exact: true }).click()
    await guest.page.waitForFunction(() => window.novalistStores.project.getState().isLoaded && !window.novalistStores.project.getState().workspaceBusy)
    await dismissTour(guest.page)
    await guest.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(guest.page.getByText('Project synchronized.', { exact: true })).toBeVisible()
    await expect(guest.page.getByText(/\d+ of \d+ files · [1-9]\d* reused/)).toBeVisible()
    expect(await readFile(join(destination, 'Research', 'matching.bin'))).toEqual(unchanged)
    expect(await readFile(join(destination, 'Research', 'missing.bin'))).toEqual(missing)
    expect(await readFile(join(destination, 'Research', 'changed.txt'), 'utf8')).toBe('Host version.')
    expect(await readFile(join(destination, '.git', 'local-only'), 'utf8')).toBe('Retain local history.')
    const recovered = await readdir(join(destination, 'Recovered edits'), { recursive: true })
    const differences = await Promise.all(recovered.filter(path => path.endsWith('changed.txt'))
      .map(path => readFile(join(destination, 'Recovered edits', path), 'utf8')))
    expect(differences).toContain('Keep this local difference.')

    const unused = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 3600_000).toISOString()])
    await guest.rpc('collaboration/end')
    await owner.rpc('collaboration/end')
    await expect(guest.rpc('collaboration/join', [server.address, unused, join(guest.workDir, 'HostOffline')])).rejects.toThrow(/host|session|online/i)
    expect(await readFile(join(destination, 'Research', 'changed.txt'), 'utf8')).toBe('Host version.')
  } finally {
    await guest?.close()
    await owner.close()
    await server.close()
    await identity.close()
  }
})
