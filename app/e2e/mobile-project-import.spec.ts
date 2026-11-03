import { expect, test } from '@playwright/test'
import { copyFileSync, cpSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { mobileHost } from './mobileHarness'
import { dismissTour, type Book } from './harness'

test('Android clears Codex entries when switching between copies of the same book', async ({ page }) => {
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    await page.evaluate(async root => {
      await window.novalistRpc.request('project/create', [root, 'Original', 'Book'])
    }, h.root)
    await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(false)
    await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(true)
    await page.evaluate(async () => {
      await window.novalistRpc.request('entities/create', ['character', 'Original character', null])
      await window.novalistStores.codex.getState().refresh()
    })
    expect(await page.evaluate(() => window.novalistStores.codex.getState().entities.length)).toBe(1)
    const copy = join(h.root, 'Copy')
    cpSync(join(h.root, 'Original'), copy, { recursive: true })
    await page.evaluate(async path => { await window.novalistStores.project.getState().openProject(path) }, copy)
    expect(await page.evaluate(() => window.novalistStores.codex.getState().entities)).toEqual([])
    await page.evaluate(() => window.novalistStores.codex.getState().refresh())
    expect(await page.evaluate(() => window.novalistStores.codex.getState().entities.length)).toBe(1)
  } finally { await h.close() }
})

test('Android opens a scene before loading the full Codex', async ({ page }) => {
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    const path = await page.evaluate(async root => {
      await window.novalistRpc.request('project/create', [root, 'Reading priority', 'Book'])
      const chapter = await window.novalistRpc.request<Book>('project/createChapter', ['Chapter'])
      const book = await window.novalistRpc.request<Book>('project/createScene', [chapter.chapters[0].guid, 'Scene'])
      await window.novalistRpc.request('scenes/write', [book.chapters[0].guid, book.chapters[0].scenes[0].id, '<p>Scene ready.</p>', 'Scene ready.'])
      const path = window.novalistStores.project.getState().projectPath
      await window.novalistStores.project.getState().closeProject()
      return path
    }, h.root)
    const beforeScene = await page.evaluate(async path => {
      const methods: string[] = []
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = (method, params, options) => {
        methods.push(method)
        return request(method, params, options)
      }
      await window.novalistStores.project.getState().openProject(path!)
      const project = window.novalistStores.project.getState()
      await project.openScene(project.chapters[0].guid, project.chapters[0].scenes[0].id)
      return methods.slice(0, methods.indexOf('scenes/read'))
    }, path)
    expect(beforeScene).not.toContain('entities/list')
    expect(beforeScene).not.toContain('project/recent')
    expect(beforeScene).not.toContain('spell/dictionary')
    await expect(page.frameLocator('iframe').locator('[contenteditable="true"]').first()).toContainText('Scene ready.')
  } finally { await h.close() }
})

test('Android opens an existing desktop folder and cancellation keeps the bookshelf', async ({ page }) => {
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    await page.evaluate(async root => {
      await window.novalistRpc.request('project/create', [root, 'Desktop novel', 'Book'])
      await window.novalistStores.project.getState().closeProject()
    }, h.root)
    const open = page.locator('.library-open-folder')
    await open.click()
    await expect(page.locator('.library-open-folder')).toBeVisible()
    h.host.pickedFolder = join(h.root, 'Desktop novel')
    await open.click()
    await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(true)
    expect(await page.evaluate(() => window.novalistStores.project.getState().projectPath)).toBe(h.host.pickedFolder)
  } finally { await h.close() }
})

test('Android shows folder-opening errors and allows another attempt', async ({ page }) => {
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    await page.evaluate(() => {
      window.novalist.pickFolder = () => new Promise((_, reject) => {
        window.addEventListener('finish-folder-pick', () => reject(new Error('Folder access was revoked')), { once: true })
      })
    })
    const open = page.locator('.library-open-folder')
    await open.click()
    await expect(open).toBeDisabled()
    await expect(open).toHaveText('Loading...')
    await page.evaluate(() => window.dispatchEvent(new Event('finish-folder-pick')))
    await expect(page.getByRole('alert')).toContainText('Folder access was revoked')
    await expect(open).toBeEnabled()
    await page.evaluate(() => { window.novalist.pickFolder = async () => null })
    await open.click()
    await expect(page.getByRole('alert')).toHaveCount(0)
    await expect(open).toBeVisible()
  } finally { await h.close() }
})

test('Android restores a desktop backup from the file selector and releases the staged file', async ({ page }) => {
  const h = await mobileHost(page, 'tablet', 'android')
  try {
    const backups = join(h.root, 'backups')
    mkdirSync(backups)
    const archive = await page.evaluate(async ({ root, backups }) => {
      const rpc = window.novalistRpc
      await rpc.request('project/create', [root, 'Desktop original', 'Book'])
      const chapter = await rpc.request<Book>('project/createChapter', ['Chapter'])
      const book = await rpc.request<Book>('project/createScene', [chapter.chapters[0].guid, 'Scene'])
      await rpc.request('scenes/write', [book.chapters[0].guid, book.chapters[0].scenes[0].id, '<p>Existing desktop writing.</p>', 'Existing desktop writing.'])
      await rpc.request('settings/updateGlobal', [{ backupFolder: backups, backupEnabled: false }])
      const backup = await rpc.request<{ path: string }>('backup/createMilestone', ['Desktop backup'])
      await window.novalistStores.project.getState().closeProject()
      return backup.path
    }, { root: h.root, backups })
    h.host.pickedFile = join(h.root, 'desktop.novalist')
    copyFileSync(archive, h.host.pickedFile)
    await page.locator('.library-import-backup').click()
    const dialog = page.getByRole('dialog', { name: 'Restore backup as new project' })
    await dialog.locator('#restore-backup-file').locator('..').getByRole('button', { name: 'Browse' }).click()
    await dialog.locator('#restore-project-name').fill('Tablet restored')
    await dialog.getByRole('button', { name: 'Restore and open' }).click()
    await expect(dialog).toBeHidden()
    await dismissTour(page)
    await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().projectPath)).toBe(join(h.root, 'Tablet restored'))
    await expect.poll(() => h.host.releasedPicks).toContain(h.host.pickedFile)
    const content = await page.evaluate(async () => {
      const book = await window.novalistRpc.request<Book>('project/getState')
      return window.novalistRpc.request<{ html: string }>('scenes/read', [book.chapters[0].guid, book.chapters[0].scenes[0].id])
    })
    expect(content.html).toContain('Existing desktop writing.')
  } finally { await h.close() }
})
