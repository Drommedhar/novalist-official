import { expect, test } from '@playwright/test'
import { existsSync, readdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, selectCodexTab } from './harness'

test('shared entries reuse another book portrait without duplicating it', async () => {
  const h = await launchApp('nl-shared-gallery-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const dataUrl = await h.page.evaluate(() => {
      const canvas = document.createElement('canvas')
      canvas.width = 48
      canvas.height = 32
      canvas.getContext('2d')!.fillRect(0, 0, 48, 32)
      return canvas.toDataURL('image/png')
    })
    const source = join(h.workDir, 'shared-portrait.png')
    writeFileSync(source, Buffer.from(dataUrl.split(',')[1], 'base64'))
    const imported = await h.rpc<{ path: string; url: string }>('gallery/import', [source])
    const shared = await h.rpc<{ id: string }>('entities/create', ['character', 'Shared portrait'])
    await h.rpc('entities/moveToWorldBible', ['character', shared.id])
    const books = await h.rpc<{ books: { id: string; name: string }[] }>('project/createBook', ['Book Two'])
    const secondId = books.books.find(book => book.name === 'Book Two')!.id
    await h.page.evaluate(async (id) => {
      const state = await window.novalistRpc.request('project/switchBook', [id])
      window.novalistStores.project.getState().applyState(state as never)
    }, secondId)
    const project = await h.rpc<{ projectPath: string }>('project/getState')
    await h.page.evaluate(async (id) => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, shared.id)
    await selectCodexTab(h.page, 'details')
    await h.page.getByRole('button', { name: 'From gallery', exact: true }).first().click()
    const choice = h.page.locator('.entity-gallery-pick')
    await expect(choice).toHaveCount(1)
    await expect.poll(() => choice.locator('img').evaluate((img: HTMLImageElement) =>
      [img.naturalWidth, img.naturalHeight]
    )).toEqual([48, 32])
    await choice.click()
    const storedImages = () => h.rpc<{ images: { path: string; url: string }[] }>(
      'entities/get', ['character', shared.id]
    )
    await expect.poll(async () => (await storedImages()).images.map(image => image.url))
      .toEqual([imported.url])
    await expect.poll(() => h.page.locator('.entity-images-strip img').first()
      .evaluate((img: HTMLImageElement) => [img.naturalWidth, img.naturalHeight]))
      .toEqual([48, 32])

    await h.rpc('project/open', [project.projectPath])
    const image = (await storedImages()).images[0]
    expect(image.path).toBe('./' + imported.url)
    expect(image.url).toBe(imported.url)
    expect(existsSync(join(project.projectPath, image.url))).toBe(true)
    expect(readdirSync(project.projectPath, { recursive: true })
      .filter(path => String(path).endsWith('shared-portrait.png'))).toHaveLength(1)
  } finally {
    await h.close()
  }
})
