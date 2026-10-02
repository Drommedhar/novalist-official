import { expect, test } from '@playwright/test'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, shapeOf, state, type Harness } from './harness'

function write(root: string, relative: string, text: string): void {
  const path = join(root, relative)
  mkdirSync(join(path, '..'), { recursive: true })
  writeFileSync(path, text, 'utf8')
}

async function chooseFolder(h: Harness, root: string): Promise<void> {
  await h.app.evaluate(({ dialog }, path: string) => {
    dialog.showOpenDialog = () => Promise.resolve({ canceled: false, filePaths: [path] }) as never
  }, root)
  await expect.poll(() => h.app.evaluate(({ Menu }) => {
    const file = Menu.getApplicationMenu()?.items.find(item => item.label === 'File')
    return file?.submenu?.items.find(item => item.label === 'Import a folder…')?.enabled
  })).toBe(true)
  await h.app.evaluate(({ Menu, BrowserWindow }) => {
    const file = Menu.getApplicationMenu()!.items.find(item => item.label === 'File')!
    file.submenu!.items.find(item => item.label === 'Import a folder…')!
      .click(undefined!, BrowserWindow.getAllWindows()[0], undefined!)
  })
  await h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    .getByRole('button', { name: 'Choose folder…' }).click()
  await expect(h.page.locator('.folder-import-row').first()).toBeVisible()
  await expect(h.page.locator('.folder-import-folders')).toHaveAttribute('aria-busy', 'false')
}

async function assignMixed(h: Harness): Promise<void> {
  const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
  await dialog.getByLabel('Cast', { exact: true }).selectOption('character')
  await dialog.getByLabel('Cast/Places', { exact: true }).selectOption('location')
  await dialog.getByLabel('Scenes', { exact: true }).selectOption('scene')
  await dialog.getByLabel('Notes', { exact: true }).selectOption('research')
}

test('File menu imports mixed folders using inherited targets and skips repeated imports', async ({}, testInfo) => {
  const h = await launchApp('nl-folder-import-')
  try {
    await seedBook(h, { Existing: ['Already written'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    const root = join(h.workDir, 'source')
    write(root, 'Cast/ada.md', '---\ntitle: Ada\ntags: [lead]\n---\n# Ada\n\nA brave navigator.')
    write(root, 'Cast/Heroes/ben.md', '# Ben\n\nA loyal companion.')
    write(root, 'Cast/Places/port.md', '# Harbour\n\nAn old port.')
    write(root, 'Scenes/arrival.md', '# Arrival\n\nShe **entered** the harbour.')
    write(root, 'Notes/route.txt', 'The ship travels north.')
    write(root, 'Unselected/leave.md', '# Leave this out')
    write(root, 'root.md', '# Leave root out')
    write(root, '.obsidian/state.md', '# Ignore state')
    write(root, 'map.png', 'Unsupported format')
    await chooseFolder(h, root)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await expect(dialog.getByRole('button', { name: 'Import files', exact: true })).toBeDisabled()
    await assignMixed(h)
    await expect(h.page.getByLabel('Cast/Heroes', { exact: true }).locator('option:checked')).toHaveText('Same as parent: Characters')
    await dialog.getByLabel('Tag everything with').fill('archive')
    await h.page.screenshot({ path: testInfo.outputPath('folder-import-preview.png') })
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await expect(dialog.getByText('5 imported; 2 skipped; 0 could not be imported.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Continue import', exact: true })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: 'Import files', exact: true })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeEnabled()
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toHaveClass(/\bprimary\b/)
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(h.page.locator('.codex-nav-scroll').getByText('route', { exact: true })).toBeVisible()
    const book = await state(h)
    expect(shapeOf(book)).toEqual({ Existing: ['Already written'], Scenes: ['Arrival'] })
    const characters = await h.rpc<{ id: string; name: string }[]>('entities/list', ['character'])
    expect(characters.map(character => character.name).sort()).toEqual(['Ada', 'Ben'])
    const ada = await h.rpc<{ tags: string[]; sections: { content: string }[] }>('entities/get', ['character', characters.find(character => character.name === 'Ada')!.id])
    expect(ada.tags).toContain('lead')
    expect(ada.tags).toContain('archive')
    expect(ada.sections[0].content).toContain('navigator')
    const locations = await h.rpc<{ name: string }[]>('entities/list', ['location'])
    expect(locations.map(location => location.name)).toEqual(['Harbour'])
    const notes = await h.rpc<{ title: string; content: string }[]>('research/list')
    expect(notes).toMatchObject([{ title: 'route', content: 'The ship travels north.' }])

    await chooseFolder(h, root)
    await assignMixed(h)
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await expect(dialog.getByText('0 imported; 7 skipped; 0 could not be imported.')).toBeVisible()
    expect(shapeOf(await state(h))).toEqual({ Existing: ['Already written'], Scenes: ['Arrival'] })
  } finally {
    await h.close()
  }
})

test('folder choices survive pagination and search while only one page is rendered', async () => {
  const h = await launchApp('nl-folder-import-pages-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const root = join(h.workDir, 'source')
    for (let index = 0; index < 65; index++) {
      write(root, `Folder${String(index).padStart(2, '0')}/one.md`, `# Entry ${index}\n\nContent`)
    }
    await chooseFolder(h, root)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await expect(h.page.locator('.folder-import-row')).toHaveCount(30)
    await h.page.getByLabel('Folder01', { exact: true }).selectOption('character')
    await dialog.getByRole('button', { name: 'Next', exact: true }).click()
    await expect(h.page.getByLabel('Folder30', { exact: true })).toBeEnabled()
    await expect(h.page.locator('.folder-import-row')).toHaveCount(30)
    await dialog.getByRole('button', { name: 'Previous', exact: true }).click()
    await expect(h.page.getByLabel('Folder01', { exact: true })).toHaveValue('character')
    await dialog.getByLabel('Search subfolders…').fill('folder62')
    await expect(h.page.locator('.folder-import-row')).toHaveCount(1)
    await h.page.getByLabel('Folder62', { exact: true }).selectOption('location')
    await dialog.getByLabel('Search subfolders…').fill('missing')
    await expect(dialog.getByText('No matching folders.', { exact: true })).toBeVisible()
    await dialog.getByLabel('Search subfolders…').fill('')
    await expect(h.page.getByLabel('Folder01', { exact: true })).toHaveValue('character')
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await expect(dialog.getByText('2 imported; 63 skipped; 0 could not be imported.')).toBeVisible()
    expect(await h.rpc('entities/list', ['character'])).toMatchObject([{ name: 'Entry 1' }])
    expect(await h.rpc('entities/list', ['location'])).toMatchObject([{ name: 'Entry 62' }])
  } finally {
    await h.close()
  }
})

test('stopping an import keeps the saved batch and continuing finishes the remaining files', async () => {
  const h = await launchApp('nl-folder-import-stop-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const root = join(h.workDir, 'source')
    for (let index = 0; index < 100; index++) write(root, `Cast/${index}.md`, `# Hero ${index}`)
    await chooseFolder(h, root)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByLabel('Cast', { exact: true }).selectOption('character')
    // Delay the response from a real batch so Stop can be exercised predictably
    // even on a machine that writes forty small files in a single frame.
    await h.page.evaluate(() => {
      const client = window.novalistRpc
      const original = client.request.bind(client)
      client.request = (async (...args: Parameters<typeof client.request>) => {
        const result = await original(...args)
        if (args[0] === 'folderImport/batch') await new Promise(resolve => setTimeout(resolve, 300))
        return result
      }) as typeof client.request
    })
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeDisabled()
    await dialog.getByRole('button', { name: 'Stop after this batch', exact: true }).click()
    await expect(dialog.getByText('Import stopped. Saved entries are kept; continue when you are ready.')).toBeVisible()
    await expect(dialog.getByText('40 of 100 files processed')).toBeVisible()
    expect(await h.rpc('entities/list', ['character'])).toHaveLength(40)
    await dialog.getByRole('button', { name: 'Continue import', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await expect(dialog.getByText('100 imported; 0 skipped; 0 could not be imported.')).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Continue import', exact: true })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: 'Stop after this batch', exact: true })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeEnabled()
    expect(await h.rpc('entities/list', ['character'])).toHaveLength(100)
  } finally {
    await h.close()
  }
})

test('Research shortcut defaults to research and shows imported notes after closing', async () => {
  const h = await launchApp('nl-folder-import-research-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const root = join(h.workDir, 'source')
    write(root, 'Notes/route.md', '# Sea route\n\nTravel north.')
    await h.app.evaluate(({ dialog }, path: string) => {
      dialog.showOpenDialog = () => Promise.resolve({ canceled: false, filePaths: [path] }) as never
    }, root)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.research-actions').getByRole('button', { name: 'Import a folder', exact: true }).click()
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByRole('button', { name: 'Choose folder…' }).click()
    await expect(dialog.getByLabel('Default target (includes files in the main folder)')).toHaveValue('research')
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(h.page.locator('.codex-nav-scroll').getByText('Sea route', { exact: true })).toBeVisible()
  } finally {
    await h.close()
  }
})

test('exported book schema prepares JSON imports with typed custom fields visible in the Codex', async () => {
  const h = await launchApp('nl-folder-import-schema-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const templates = await h.rpc<{ id: string }[]>('templates/list', ['character'])
    const templateId = templates[0]?.id ?? 'import-people'
    await h.rpc('templates/save', ['character', {
      id: templateId, name: 'People', builtIn: false,
      fields: ['name', 'surname', 'age', 'eyeColor'].map(key => ({ key, defaultValue: '' })),
      customPropertyDefs: [{ key: 'Skill', type: 'String', prompt: 'What can they do?' }, { key: 'Alive', type: 'Bool' }],
      sections: []
    }])
    await h.rpc('entities/saveCustomType', [{
      typeKey: 'faction', displayName: 'Faction', displayNamePlural: 'Factions',
      fields: [{ key: 'strength', displayName: 'Military strength', type: 'Int', required: true }],
      includeImages: false, includeRelationships: true, includeSections: true
    }])
    const root = join(h.workDir, 'converted')
    write(root, 'Cast/ada.json', JSON.stringify({
      novalistImport: 1, target: 'character', data: {
        name: 'Ada', surname: 'Lovelace', age: '32', eyeColor: 'brown',
        customProperties: { Skill: 'Mathematics', Alive: 'true' }
      }, content: 'A preserved **biography**.'
    }))
    write(root, 'Cast/ben.md', '---\nname: Ben\nsurname: North\ncustomProperties:\n  Alive: yes\n---\nAge: 28\n| Field | Value |\n| --- | --- |\n| Eye colour | blue |\n## Biography\nA loyal companion.')
    write(root, 'Cast/wrong.json', JSON.stringify({ novalistImport: 1, target: 'location', data: { name: 'Wrong target' } }))
    write(root, 'Factions/guild.json', JSON.stringify({ novalistImport: 1, target: 'faction', data: { name: 'Guild', fields: { strength: '40' } } }))
    await chooseFolder(h, root)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    const exportButton = dialog.getByRole('button', { name: 'Export import schema…' })
    await h.app.evaluate(({ dialog }) => {
      dialog.showSaveDialog = () => Promise.resolve({ canceled: true }) as never
    })
    await exportButton.click()
    await expect(exportButton).toBeEnabled()
    await expect(dialog.getByText('Schema saved.', { exact: false })).toHaveCount(0)
    const output = join(h.workDir, 'novalist-import.schema.json')
    await h.app.evaluate(({ dialog }, path: string) => {
      dialog.showSaveDialog = () => Promise.resolve({ canceled: false, filePath: path }) as never
    }, output)
    await exportButton.click()
    await expect(dialog.getByText('Schema saved.', { exact: false })).toBeVisible()
    const schema = JSON.parse(readFileSync(output, 'utf8')) as {
      oneOf: { properties: { target: { const: string }; data: { properties: Record<string, unknown> } } }[]
    }
    expect(schema.oneOf).toHaveLength(7)
    const characterSchema = schema.oneOf.find(branch => branch.properties.target.const === 'character')!
    expect(characterSchema.properties.data.properties.customProperties).toMatchObject({
      properties: { Skill: { type: 'string' }, Alive: { enum: ['true', 'false'] } }
    })
    expect(characterSchema.properties.data.properties.templateId).toMatchObject({ const: templateId })
    await dialog.getByLabel('Cast', { exact: true }).selectOption('character')
    await dialog.getByLabel('Factions', { exact: true }).selectOption('faction')
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('Import complete.', { exact: true })).toBeVisible()
    await expect(dialog.getByText('3 imported; 0 skipped; 1 could not be imported.')).toBeVisible()
    await expect(dialog.getByText('JSON does not match the selected target’s import schema: Cast/wrong.json')).toBeVisible()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    const characters = await h.rpc<{ id: string; name: string }[]>('entities/list', ['character'])
    expect(characters).toHaveLength(2)
    const ada = characters.find(character => character.name === 'Ada Lovelace')!
    const ben = characters.find(character => character.name === 'Ben North')!
    expect(await h.rpc('entities/get', ['character', ben.id])).toMatchObject({ surname: 'North', age: '28', eyeColor: 'blue', customProperties: { Alive: 'true' } })
    await h.page.evaluate(async (id: string) => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, ada.id)
    await expect(h.page.getByLabel('Surname', { exact: true })).toHaveValue('Lovelace')
    await expect(h.page.getByLabel('Age', { exact: true })).toHaveValue('32')
    await expect(h.page.getByLabel('Eye Color', { exact: true })).toHaveValue('brown')
    await expect(h.page.locator('.entity-rel-row').filter({ hasText: 'Skill' }).locator('input')).toHaveValue('Mathematics')
    await expect(h.page.locator('.entity-rel-row').filter({ hasText: 'Alive' }).getByRole('checkbox')).toBeChecked()
    const factions = await h.rpc<{ id: string }[]>('entities/list', ['faction'])
    expect(factions).toHaveLength(1)
    expect(await h.rpc('entities/get', ['faction', factions[0].id])).toMatchObject({ name: 'Guild', fields: { strength: '40' } })
  } finally {
    await h.close()
  }
})

test('Markdown character sheets show parsed fields, numeric age, image and chapter overrides in the Codex', async () => {
  const h = await launchApp('nl-folder-import-character-sheet-')
  try {
    const book = await seedBook(h, { 'The beginning': ['Childhood'], 'The return': ['Homecoming'] })
    await dismissTour(h.page)
    const templates = await h.rpc<{ id: string }[]>('templates/list', ['character'])
    await h.rpc('templates/save', ['character', {
      id: templates[0]?.id ?? 'import-people', name: 'People', builtIn: false, ageMode: 'date', ageIntervalUnit: 'Years',
      fields: ['name', 'surname', 'age', 'role', 'eyeColor', 'hairColor', 'hairLength', 'build'].map(key => ({ key, defaultValue: '' })),
      customPropertyDefs: [], sections: [], includeRelationships: true, includeImages: true, includeChapterOverrides: true
    }])
    const root = join(h.workDir, 'source', 'NovelProject')
    write(root, 'Characters/Sam North.md', `# Sam North
## General Information
- **Age:** 10
- **Relationship:** Protagonist
## Further Information
- Geburtstag: 8. Juli
- Erscheinung: Kurzes braunes Haar, blaue Augen, etwas übergewichtig
- Eltern: Alex North & Robin North
- Schuljahr: 4. Klasse
## Images
- Portrait: ![[Images/portrait.png]]
## Chapter Relevant Information
- **The beginning** (Order: 1):
  - age: 6
- **The return** (Order: 2):
  - age: 10`)
    const imageDir = join(h.workDir, 'source', 'Images')
    mkdirSync(imageDir, { recursive: true })
    writeFileSync(join(imageDir, 'portrait.png'), Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvIoAAAAASUVORK5CYII=', 'base64'))
    await chooseFolder(h, root)
    const dialog = h.page.getByRole('dialog', { name: 'Import a folder', exact: true })
    await dialog.getByLabel('Characters', { exact: true }).selectOption('character')
    await dialog.getByRole('button', { name: 'Import files', exact: true }).click()
    await expect(dialog.getByText('1 imported; 0 skipped; 0 could not be imported.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    const [character] = await h.rpc<{ id: string; name: string }[]>('entities/list', ['character'])
    expect(character.name).toBe('Sam North')
    expect(await h.rpc('entities/get', ['character', character.id])).toMatchObject({
      name: 'Sam', surname: 'North', age: '10', ageMode: 'number', eyeColor: 'blau', hairColor: 'braun', hairLength: 'Kurz',
      build: 'etwas übergewichtig', role: 'Protagonist', customProperties: { Geburtstag: '8. Juli', Schuljahr: '4. Klasse' },
      chapterOverrides: book.chapters.map(chapter => ({ chapter: chapter.guid, age: chapter.title === 'The beginning' ? '6' : '10' })),
      sections: [{ title: 'Imported content' }]
    })
    await h.page.evaluate(async (id: string) => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, character.id)
    await expect(h.page.getByLabel('Name', { exact: true })).toHaveValue('Sam')
    await expect(h.page.getByLabel('Surname', { exact: true })).toHaveValue('North')
    await expect(h.page.getByLabel('Age', { exact: true })).toHaveValue('10')
    await expect(h.page.getByLabel('Eye Color', { exact: true })).toHaveValue('blau')
    await expect(h.page.getByLabel('Hair Color', { exact: true })).toHaveValue('braun')
    await expect(h.page.locator('.entity-rel-row').filter({ hasText: 'Schuljahr' }).locator('input')).toHaveValue('4. Klasse')
    const portrait = h.page.locator('.entity-images').getByRole('img', { name: 'Portrait', exact: true })
    await expect(portrait).toBeVisible()
    await expect.poll(() => portrait.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBe(1)
    await expect(h.page.locator('.overrides-list').getByText('The beginning', { exact: true })).toBeVisible()
  } finally {
    await h.close()
  }
})
