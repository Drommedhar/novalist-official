import { expect, test } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, resizeWindow, seedBook, selectCodexTab, type Harness } from './harness'

interface ApiStatus { running: boolean; url: string; token: string }

async function openImporter(h: Harness): Promise<void> {
  await expect.poll(() => h.app.evaluate(({ Menu }) => {
    const file = Menu.getApplicationMenu()?.items.find(item => item.label === 'File')
    return file?.submenu?.items.find(item => item.label === 'Import a folder…')?.enabled
  })).toBe(true)
  await h.app.evaluate(({ Menu, BrowserWindow }) => {
    const file = Menu.getApplicationMenu()!.items.find(item => item.label === 'File')!
    file.submenu!.items.find(item => item.label === 'Import a folder…')!
      .click(undefined!, BrowserWindow.getAllWindows()[0], undefined!)
  })
  await expect(h.page.getByRole('switch', { name: 'Local import API', exact: true })).toBeEnabled()
}

test('import choices are grouped, actions look like buttons, and the API is a keyboard accessible switch', async ({}, testInfo) => {
  const h = await launchApp('nl-import-ui-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await resizeWindow(h, 860, 760)
    await openImporter(h)
    let dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    const files = dialog.getByRole('region', { name: 'Import existing files', exact: true })
    const schema = dialog.getByRole('region', { name: 'Prepare files with an agent', exact: true })
    const agent = dialog.getByRole('region', { name: 'Let an agent import directly', exact: true })
    await expect(files.getByLabel('Source folder', { exact: true })).toBeVisible()
    await expect(files.getByRole('button', { name: 'Import files', exact: true })).toBeDisabled()
    await expect(schema.getByRole('button', { name: 'Export import schema…', exact: true })).toBeVisible()
    const choose = files.getByRole('button', { name: 'Choose folder…', exact: true })
    const appearance = await choose.evaluate(button => {
      const style = getComputedStyle(button)
      return { border: style.borderTopStyle, borderWidth: style.borderTopWidth, background: style.backgroundColor }
    })
    expect(appearance.border).toBe('solid')
    expect(Number.parseFloat(appearance.borderWidth)).toBeGreaterThan(0)
    expect(appearance.background).not.toBe('rgba(0, 0, 0, 0)')
    expect(await files.getByRole('button', { name: 'Import files', exact: true }).evaluate(button => Number(getComputedStyle(button).opacity))).toBeLessThan(1)
    const toggle = agent.getByRole('switch', { name: 'Local import API', exact: true })
    await expect(toggle).not.toBeChecked()
    await expect(agent.getByRole('button', { name: 'Copy agent instructions', exact: true })).toHaveCount(0)
    await expect(files.getByRole('heading', { name: 'Import existing files', exact: true })).toBeInViewport()
    await expect(toggle).toBeInViewport({ ratio: 1 })
    await dialog.screenshot({ path: testInfo.outputPath('import-options-initial.png') })
    await toggle.focus()
    await toggle.press('Space')
    await expect(toggle).toBeChecked()
    await expect(agent.getByText('On', { exact: true })).toBeVisible()
    await expect(agent.getByRole('button', { name: 'Copy agent instructions', exact: true })).toBeInViewport()
    await dialog.screenshot({ path: testInfo.outputPath('import-api-on.png') })
    await toggle.press('Space')
    await expect(toggle).not.toBeChecked()
    await expect(agent.getByText('Off', { exact: true })).toBeVisible()
    await expect(agent.getByRole('button', { name: 'Copy agent instructions', exact: true })).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Close', exact: true }).focus()
    await dialog.locator('.folder-import-body').evaluate(body => { body.scrollTop = 0 })
    await dialog.screenshot({ path: testInfo.outputPath('import-options.png') })
    // German labels and a narrower window keep controls inside their sections.
    await h.page.evaluate(async () => { await window.novalistStores.settings.getState().update('global', { language: 'de' }) })
    await resizeWindow(h, 680, 640)
    dialog = h.page.getByRole('dialog', { name: 'Ordner importieren', exact: true })
    await expect(dialog.getByRole('heading', { name: 'Vorhandene Dateien importieren', exact: true })).toBeVisible()
    await dialog.getByRole('switch', { name: 'Lokale Import-API', exact: true }).scrollIntoViewIfNeeded()
    expect(await dialog.evaluate(card => card.scrollWidth <= card.clientWidth)).toBe(true)
    expect(await dialog.locator('.folder-import-body').evaluate(body => body.scrollWidth <= body.clientWidth)).toBe(true)
    const close = dialog.getByRole('button', { name: 'Schließen', exact: true })
    await expect(close).toBeInViewport()
    await dialog.locator('.folder-import-body').evaluate(body => { body.scrollTop = 0 })
    await dialog.screenshot({ path: testInfo.outputPath('import-options-de.png') })
    await close.click()
  } finally {
    await h.close()
  }
})

test('an external agent discovers custom schemas and imports while the dialog is closed', async ({ request }) => {
  const h = await launchApp('nl-import-api-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    const templates = await h.rpc<{ id: string }[]>('templates/list', ['character'])
    await h.rpc('templates/save', ['character', {
      id: templates[0]?.id ?? 'import-people', name: 'People', builtIn: false,
      fields: ['name', 'surname'].map(key => ({ key, defaultValue: '' })),
      customPropertyDefs: [{ key: 'Skill', type: 'String' }], sections: []
    }])
    await h.rpc('entities/saveCustomType', [{
      typeKey: 'faction', displayName: 'Faction', displayNamePlural: 'Factions',
      fields: [{ key: 'strength', displayName: 'Strength', type: 'Int', required: true }],
      includeImages: false, includeRelationships: true, includeSections: true
    }])
    const source = join(h.workDir, 'source')
    mkdirSync(join(source, 'Cast'), { recursive: true })
    writeFileSync(join(source, 'Cast', 'ada.md'), '# Ada Lovelace\nOccupation: Mathematician\n', 'utf8')
    await h.app.evaluate(({ dialog }, path: string) => {
      dialog.showOpenDialog = () => Promise.resolve({ canceled: false, filePaths: [path] }) as never
    }, source)
    await openImporter(h)
    let dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByRole('button', { name: 'Choose folder…' }).click()
    await expect(dialog.getByLabel('Cast', { exact: true })).toBeVisible()
    await dialog.getByRole('switch', { name: 'Local import API', exact: true }).click()
    await expect(dialog.getByRole('switch', { name: 'Local import API', exact: true })).toBeChecked()
    const api = await h.rpc<ApiStatus>('importApi/status')
    await expect(dialog.getByLabel('Local import API URL', { exact: true })).toHaveValue(api.url)
    await dialog.getByRole('button', { name: 'Copy agent instructions', exact: true }).click()
    await expect.poll(() => h.app.evaluate(() => (globalThis as unknown as { __copied?: string }).__copied)).toContain(source)
    const prompt = await h.app.evaluate(() => (globalThis as unknown as { __copied?: string }).__copied!)
    expect(prompt).toContain(api.url)
    expect(prompt).toContain(`Authorization: Bearer ${api.token}`)
    expect(prompt).toContain('/import/validate')
    expect(prompt).toContain('/images')
    expect(prompt).toContain('/images/attach')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    const headers = { Authorization: `Bearer ${api.token}` }
    expect((await request.get(`${api.url}/types`)).status()).toBe(401)
    const types = await (await request.get(`${api.url}/types`, { headers })).json()
    expect(types.types).toContainEqual({ key: 'faction', name: 'Faction', schema: '/v1/types/faction/schema' })
    const schema = await (await request.get(`${api.url}/types/character/schema`, { headers })).json()
    expect(schema.properties.data.properties.customProperties.properties.Skill.type).toBe('string')
    const entries = [
      { sourceId: join(source, 'Cast', 'ada.md'), document: { novalistImport: 1, target: 'character', data: { name: 'Ada', surname: 'Lovelace', customProperties: { Skill: 'Mathematics' } } } },
      { sourceId: join(source, 'guild.md'), document: { novalistImport: 1, target: 'faction', data: { name: 'Guild', fields: { strength: '42' } } } },
      { sourceId: join(source, 'arrival.md'), folder: 'Chapter 1', document: { novalistImport: 1, target: 'scene', data: { title: 'Arrival', synopsis: 'Ada reaches the harbour.' }, content: 'She **arrived**.' } },
      { sourceId: join(source, 'route.md'), document: { novalistImport: 1, target: 'research', data: { title: 'Sea route', rating: 4 }, content: 'Travel north.' } }
    ]
    const validation = await (await request.post(`${api.url}/import/validate`, { headers, data: { entries } })).json()
    expect(validation).toMatchObject({ validated: 4, imported: 0, validateOnly: true })
    expect(await h.rpc('entities/list', ['character'])).toEqual([])
    const imported = await (await request.post(`${api.url}/import`, { headers, data: { entries } })).json()
    expect(imported).toMatchObject({ imported: 4, skipped: 0, failed: 0 })
    await expect(h.page.locator('.codex-nav-scroll').getByText('Sea route', { exact: true })).toBeVisible()
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().chapters.map(chapter => chapter.title))).toContain('Chapter 1')
    const characters = await h.rpc<{ id: string }[]>('entities/list', ['character'])
    expect(await h.rpc('entities/get', ['character', characters[0].id])).toMatchObject({ surname: 'Lovelace', customProperties: { Skill: 'Mathematics' } })
    const guild = await h.rpc<{ id: string }[]>('entities/list', ['faction'])
    expect(await h.rpc('entities/get', ['faction', guild[0].id])).toMatchObject({ fields: { strength: '42' } })
    const repeat = await (await request.post(`${api.url}/import`, { headers, data: { entries } })).json()
    expect(repeat).toMatchObject({ imported: 0, skipped: 4, failed: 0 })
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.evaluate(async () => { await window.novalistStores.codex.getState().setType('character') })
    await expect(h.page.locator('.codex-nav-scroll').getByText('Ada Lovelace', { exact: true })).toBeVisible()
    // Reopening the same dialog keeps the API and its progress available.
    await h.app.evaluate(({ Menu, BrowserWindow }) => {
      Menu.getApplicationMenu()!.items.find(item => item.label === 'File')!.submenu!.items
        .find(item => item.label === 'Import a folder…')!.click(undefined!, BrowserWindow.getAllWindows()[0], undefined!)
    })
    dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await expect(dialog.getByLabel('Local import API URL', { exact: true })).toHaveValue(api.url)
    await expect(dialog.getByText('4 imported; 4 skipped; 0 could not be imported.', { exact: true })).toBeVisible()
    await dialog.getByRole('switch', { name: 'Local import API', exact: true }).click()
    await expect(dialog.getByRole('switch', { name: 'Local import API', exact: true })).toBeEnabled()
    await expect(request.get(`${api.url}/types`, { headers, timeout: 2000 })).rejects.toThrow()
  } finally {
    await h.close()
  }
})

test('agent uploads show real pictures and missing photos can be added to an edited import', async ({ request }) => {
  const h = await launchApp('nl-import-api-images-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await openImporter(h)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByRole('switch', { name: 'Local import API', exact: true }).click()
    await expect(dialog.getByRole('switch', { name: 'Local import API', exact: true })).toBeChecked()
    const api = await h.rpc<ApiStatus>('importApi/status')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    const headers = { Authorization: `Bearer ${api.token}` }
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvIoAAAAASUVORK5CYII=', 'base64')
    const upload = await request.post(`${api.url}/images`, { headers: { ...headers, 'Content-Type': 'image/png' }, data: png })
    expect(upload.status()).toBe(201)
    const { imageId } = await upload.json() as { imageId: string }
    const reference = { imageId, name: 'Portrait', alt: 'A source portrait' }
    const entries = [
      { sourceId: 'new-with-photo', document: { novalistImport: 1, target: 'character', data: { name: 'Ada', surname: 'North', images: [reference] } } },
      { sourceId: 'earlier-without-photo', document: { novalistImport: 1, target: 'character', data: { name: 'Ben', surname: 'North' }, content: 'Original biography.' } }
    ]
    const imported = await (await request.post(`${api.url}/import`, { headers, data: { entries } })).json()
    expect(imported).toMatchObject({ imported: 2, failed: 0 })
    const adaId = imported.results[0].id as string
    const benId = imported.results[1].id as string
    await h.page.evaluate(async (id: string) => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, adaId)
    await selectCodexTab(h.page, 'details')
    const portrait = h.page.locator('.entity-images').getByRole('img', { name: 'A source portrait', exact: true })
    await expect(portrait).toBeVisible()
    await expect.poll(() => portrait.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBe(1)
    await h.page.evaluate(async (id: string) => {
      await window.novalistStores.codex.getState().select(id)
      await window.novalistStores.codex.getState().updateField('surname', 'Edited')
    }, benId)
    await expect(h.page.getByLabel('Surname', { exact: true })).toHaveValue('Edited')
    await selectCodexTab(h.page, 'details')
    await expect(portrait).toHaveCount(0)
    const retryEntries = [{ ...entries[1], document: { ...entries[1].document, data: { ...entries[1].document.data, images: [reference] } } }]
    const skipped = await (await request.post(`${api.url}/import`, { headers, data: { entries: retryEntries } })).json()
    expect(skipped).toMatchObject({ skipped: 1, imported: 0 })
    expect(skipped.results[0].id).toBe(benId)
    const attach = { target: 'character', id: benId, images: [reference] }
    const attached = await (await request.post(`${api.url}/images/attach`, { headers, data: attach })).json()
    expect(attached).toMatchObject({ added: 1, total: 1 })
    // The currently selected entry receives its gallery update without navigation.
    await expect(portrait).toBeVisible()
    await expect.poll(() => portrait.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBe(1)
    await selectCodexTab(h.page, 'overview')
    await expect(h.page.getByLabel('Surname', { exact: true })).toHaveValue('Edited')
    expect(await h.rpc('entities/get', ['character', benId])).toMatchObject({
      surname: 'Edited', sections: [{ content: 'Original biography.' }], images: [{ name: 'Portrait', alt: 'A source portrait' }]
    })
    const repeated = await (await request.post(`${api.url}/images/attach`, { headers, data: attach })).json()
    expect(repeated).toMatchObject({ added: 0, total: 1 })
    await expect(h.page.locator('.entity-images img')).toHaveCount(1)
    const uploadedAgain = await request.post(`${api.url}/images`, { headers: { ...headers, 'Content-Type': 'image/png' }, data: png })
    expect(uploadedAgain.status()).toBe(200)
    expect(await uploadedAgain.json()).toMatchObject({ imageId, reused: true })
  } finally {
    await h.close()
  }
})

test('changing the draft or closing Novalist stops the API and restarting changes its token', async ({ request }) => {
  const h = await launchApp('nl-import-api-lifetime-')
  let closed = false
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await openImporter(h)
    let dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByRole('switch', { name: 'Local import API', exact: true }).click()
    await expect(dialog.getByRole('switch', { name: 'Local import API', exact: true })).toBeChecked()
    const first = await h.rpc<ApiStatus>('importApi/status')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    const drafts = await h.rpc<{ id: string; name: string }[]>('project/createDraft', ['Other', null])
    const other = drafts.find(draft => draft.name === 'Other')!
    await h.page.evaluate(async (id: string) => { await window.novalistStores.project.getState().switchDraft(id) }, other.id)
    expect(await h.rpc('importApi/status')).toMatchObject({ running: false })
    await expect(request.get(`${first.url}/types`, { headers: { Authorization: `Bearer ${first.token}` }, timeout: 2000 })).rejects.toThrow()
    await openImporter(h)
    dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByRole('switch', { name: 'Local import API', exact: true }).click()
    await expect(dialog.getByRole('switch', { name: 'Local import API', exact: true })).toBeChecked()
    const second = await h.rpc<ApiStatus>('importApi/status')
    expect(second.token).not.toEqual(first.token)
    expect((await request.get(`${second.url}/types`, { headers: { Authorization: `Bearer ${first.token}` } })).status()).toBe(401)
    await h.close()
    closed = true
    await expect(request.get(`${second.url}/types`, { headers: { Authorization: `Bearer ${second.token}` }, timeout: 2000 })).rejects.toThrow()
  } finally {
    if (!closed) await h.close()
  }
})
