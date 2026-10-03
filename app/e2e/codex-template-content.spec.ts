import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function openEntry(h: Harness, type: string, id: string): Promise<void> {
  await h.page.evaluate(async ({ type, id }) => {
    window.novalistStores.shell.getState().setMainView('codex')
    await window.novalistStores.codex.getState().setType(type)
    await window.novalistStores.codex.getState().select(id)
  }, { type, id })
}

for (const type of ['character', 'faction']) {
  test(`template custom fields appear and remain editable on a ${type}`, async () => {
    const h = await launchApp('novalist-template-fields-')
    try {
      await seedBook(h, {})
      await dismissTour(h.page)
      if (type === 'faction') {
        await h.rpc('entities/saveCustomType', [{
          typeKey: type, displayName: 'Faction', displayNamePlural: 'Factions',
          fields: [], includeImages: false, includeRelationships: true, includeSections: true
        }])
      }
      await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('templates'))
      const groups = h.page.locator('.templates-card .template-group')
      await (type === 'character' ? groups.first() : groups.last()).locator('.binder-rail-item').click()
      const dialog = h.page.locator('.type-manager-card')
      await dialog.locator('input.dialog-input').first().fill('Custom field template')
      for (const [key, value] of [['Motto', 'We hold'], ['Secret', '']]) {
        await dialog.getByRole('button', { name: '+ Add Custom Field', exact: true }).click()
        const row = dialog.locator('.type-manager-field').filter({ has: h.page.getByPlaceholder('Field name') }).last()
        await row.locator('input').first().fill(key)
        await row.locator('input').nth(1).fill(value)
      }
      await dialog.locator('.dialog-actions .primary').click()
      await expect(dialog).toHaveCount(0)
      const templates = await h.rpc<{ id: string; name: string }[]>('templates/list', [type])
      const template = templates.find((t) => t.name === 'Custom field template')!
      const entity = await h.rpc<{ id: string }>('entities/create', [type, 'The Watch', template.id])
      await openEntry(h, type, entity.id)
      if (type === 'character') await h.page.locator('.codex-entry-tabs').getByRole('button', { name: 'Details', exact: true }).click()
      const field = (key: string) => type === 'character'
        ? h.page.locator('.entity-rel-row').filter({ hasText: key }).getByRole('textbox')
        : h.page.getByRole('textbox', { name: key, exact: true })
      await expect(field('Motto')).toHaveValue('We hold')
      await expect(field('Secret')).toHaveValue('')
      await field('Secret').fill('The hidden gate')
      await field('Secret').press('Tab')
      await expect.poll(async () => {
        const record = await h.rpc<Record<string, Record<string, string>>>('entities/get', [type, entity.id])
        return record[type === 'character' ? 'customProperties' : 'fields'].Secret
      }).toBe('The hidden gate')
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
      await openEntry(h, type, entity.id)
      if (type === 'character') await h.page.locator('.codex-entry-tabs').getByRole('button', { name: 'Details', exact: true }).click()
      await expect(field('Secret')).toHaveValue('The hidden gate')
    } finally {
      await h.close()
    }
  })
}

for (const [type, leave] of [
  ['character', 'blur'], ['character', 'wiki'], ['character', 'entry'],
  ['character', 'retry'], ['faction', 'blur']
]) {
  test(`${type} section text survives ${leave} and reaches Wiki`, async () => {
    const h = await launchApp('novalist-section-content-')
    try {
      await seedBook(h, {})
      await dismissTour(h.page)
      if (type === 'faction') {
        await h.rpc('entities/saveCustomType', [{
          typeKey: type, displayName: 'Faction', displayNamePlural: 'Factions',
          fields: [], includeImages: false, includeRelationships: true, includeSections: true
        }])
      }
      const template = await h.rpc<{ id: string }>('templates/save', [type, {
        name: 'History template', sections: [{ title: 'History', defaultContent: '' }]
      }])
      const entity = await h.rpc<{ id: string }>('entities/create', [type, 'The Watch', template.id])
      const other = await h.rpc<{ id: string }>('entities/create', [type, 'Other entry', template.id])
      await openEntry(h, type, entity.id)
      if (leave === 'retry') {
        await h.page.evaluate(() => {
          const rpc = window.novalistRpc
          const original = rpc.request.bind(rpc)
          let failed = false
          rpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
            if (method === 'spec/sectionSaveFailed') return failed as T
            if (method === 'entities/updateLists' && !failed) {
              failed = true
              throw new Error('Temporary section save failure')
            }
            return original<T>(method, params)
          }
        })
      }
      const content = h.page.locator('.entity-section .cm-content')
      await content.fill('The watch began at the **hidden gate**.')
      if (leave === 'wiki') {
        await h.page.locator('.mode-panel-row[data-view="wiki"]').click()
      } else if (leave === 'entry') {
        // Keyboard/menu-driven navigation can replace the editor without a DOM blur.
        await h.page.evaluate((id) => window.novalistStores.codex.getState().select(id), other.id)
        await expect(content).toHaveText('')
      } else {
        await h.page.getByRole('textbox', { name: 'Name', exact: true }).click()
      }
      if (leave === 'retry') {
        await expect.poll(() => h.rpc<boolean>('spec/sectionSaveFailed')).toBe(true)
        const root = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath!)
        await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
        expect(await h.page.evaluate(() => window.novalistStores.project.getState().projectPath)).toBeNull()
        await h.page.evaluate((root) => window.novalistStores.project.getState().openProject(root), root)
      }
      await expect.poll(async () => (await h.rpc<{ sections: { content: string }[] }>(
        'entities/get', [type, entity.id]
      )).sections[0].content).toBe('The watch began at the **hidden gate**.')
      expect((await h.rpc<{ sections: { content: string }[] }>(
        'entities/get', [type, other.id]
      )).sections[0].content).toBe('')
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('wiki'))
      await h.page.locator('.wiki-entry', { hasText: 'The Watch' }).click()
      await expect(h.page.locator('.wiki-article')).toContainText('The watch began at the hidden gate.')
      await openEntry(h, type, entity.id)
      await expect(content).toHaveText('The watch began at the hidden gate.')
    } finally {
      await h.close()
    }
  })
}
