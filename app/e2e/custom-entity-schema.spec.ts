import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

for (const source of ['manual', 'faction pack']) {
  test(`custom entity fields, relationships and properties work from ${source}`, async () => {
    const h = await launchApp('novalist-custom-schema-')
    try {
      await seedBook(h, {})
      await dismissTour(h.page)
      await h.rpc('entities/saveCustomType', [{
        typeKey: 'realm', displayName: 'Realm', displayNamePlural: 'Realms', fields: [],
        includeImages: false, includeRelationships: false, includeSections: false
      }])
      const character = await h.rpc<{ id: string }>('entities/create', ['character', 'Character Test'])
      await h.rpc('entities/create', ['location', 'Location Test'])
      const realm = await h.rpc<{ id: string }>('entities/create', ['realm', 'Realm Test'])
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
      await h.page.locator('.codex-tab-manage').click()
      const manager = h.page.locator('.type-manager-card')
      if (source === 'faction pack') {
        await manager.locator('.type-pack', { hasText: 'Faction' }).click()
      } else {
        await manager.locator('.dialog-actions .primary').click()
        await manager.locator('input.dialog-input').first().fill('Guild')
      }
      await manager.locator('.type-manager-features input').nth(1).check()
      // Different fields on the same sheet must each use their own target.
      // Leave the character default untouched, as the type picker allows.
      for (const [name, target] of [['Seat', 'location'], ['Leader', ''], ['Domain', 'realm']]) {
        await manager.locator('.type-manager-fields > button').click()
        const field = manager.locator('.type-manager-field').last()
        await field.locator('input').first().fill(name)
        await field.locator('select').first().selectOption('EntityRef')
        if (target) await field.locator('select').nth(1).selectOption(target)
      }
      await manager.locator('.dialog-actions .primary').click()
      await expect(manager.locator('.type-manager-field')).toHaveCount(0)
      await manager.locator('.type-manager-title-row button').click()
      const type = source === 'faction pack' ? 'faction' : 'guild'
      const entry = await h.rpc<{ id: string }>('entities/create', [type, 'The Watch'])
      await h.page.evaluate(async ({ type, id }) => {
        await window.novalistStores.codex.getState().setType(type)
        await window.novalistStores.codex.getState().select(id)
      }, { type, id: entry.id })

      for (const [label, name] of [['Seat', 'Location Test'], ['Leader', 'Character Test'], ['Domain', 'Realm Test']]) {
        const field = h.page.getByRole('combobox', { name: label, exact: true })
        await expect.poll(() => field.evaluate((input: HTMLInputElement) =>
          Array.from(input.list?.options ?? []).map((option) => option.value)
        )).toEqual([name])
        await field.fill(name)
        await field.press('Tab')
      }

      await h.page.locator('.codex-add-relationship').click()
      const relationship = h.page.locator('.entity-rel-row').filter({ has: h.page.locator('.codex-rel-inverse') }).first()
      await relationship.locator('input').nth(0).fill('led by')
      await relationship.locator('input').nth(1).fill('Character Test')
      await relationship.locator('.codex-rel-kind').selectOption('member')
      await relationship.locator('.codex-rel-inverse').fill('leads')
      await relationship.locator('.codex-rel-inverse').press('Tab')
      await expect.poll(async () => (await h.rpc<{ relationships: { role: string; target: string; category: string }[] }>(
        'entities/get', [type, entry.id]
      )).relationships).toContainEqual({ role: 'led by', target: 'Character Test', category: 'member' })
      await expect.poll(async () => (await h.rpc<{ relationships: { role: string; target: string; category: string }[] }>(
        'entities/get', ['character', character.id]
      )).relationships).toContainEqual(expect.objectContaining({ role: 'leads', target: 'The Watch' }))

      await h.page.getByRole('button', { name: /Add Property/ }).click()
      const dialog = h.page.getByRole('dialog', { name: /Add Property/ })
      await dialog.getByRole('textbox').fill('Motto')
      await dialog.getByRole('textbox').press('Enter')
      const motto = h.page.locator('.entity-rel-row').filter({ hasText: 'Motto' }).getByRole('textbox')
      await expect(motto).toBeVisible()
      await motto.fill('We hold')
      await motto.press('Tab')
      await expect.poll(async () => h.rpc('entities/customProps', [type, entry.id])).toContainEqual({
        key: 'Motto', value: 'We hold', propType: 'String', enumOptions: []
      })

      // Revisit the entry: all three reference values and the property persist.
      await h.page.evaluate(async (id) => {
        await window.novalistStores.codex.getState().setType('realm')
        await window.novalistStores.codex.getState().select(id)
      }, realm.id)
      await expect(h.page.locator('.codex-add-relationship')).toHaveCount(0)
      await h.page.evaluate(async ({ type, id }) => {
        await window.novalistStores.codex.getState().setType(type)
        await window.novalistStores.codex.getState().select(id)
      }, { type, id: entry.id })
      await expect(motto).toHaveValue('We hold')
      await expect(h.page.getByRole('combobox', { name: 'Seat', exact: true })).toHaveValue('Location Test')
      await expect(h.page.getByRole('combobox', { name: 'Leader', exact: true })).toHaveValue('Character Test')
      await expect(h.page.getByRole('combobox', { name: 'Domain', exact: true })).toHaveValue('Realm Test')
      await h.page.getByRole('button', { name: /Delete Motto/ }).click()
      await expect(motto).toHaveCount(0)
      expect(await h.rpc('entities/customProps', [type, entry.id])).toEqual([])
    } finally {
      await h.close()
    }
  })
}
