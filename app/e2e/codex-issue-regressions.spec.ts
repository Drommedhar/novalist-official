import { expect, test } from '@playwright/test'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, resizeWindow, seedBook, selectCodexTab } from './harness'

test('custom entries keep gallery images after leaving and reopening the entry', async () => {
  const h = await launchApp('nl-custom-images-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.rpc('entities/saveCustomType', [{
      typeKey: 'guild', displayName: 'Guild', fields: [], includeImages: true
    }])
    const entry = await h.rpc<{ id: string }>('entities/create', ['guild', 'Watch'])
    const data = await h.page.evaluate(() => {
      const canvas = document.createElement('canvas')
      canvas.width = 12
      canvas.height = 8
      return canvas.toDataURL('image/png').split(',')[1]
    })
    const source = join(h.workDir, 'crest.png')
    writeFileSync(source, Buffer.from(data, 'base64'))
    await h.rpc('gallery/import', [source])
    await h.page.evaluate(async id => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('guild')
      await window.novalistStores.codex.getState().select(id)
    }, entry.id)
    await selectCodexTab(h.page, 'details')
    await h.page.locator('.entity-image-add').first().click()
    await h.page.locator('.entity-gallery-pick').click()
    const image = h.page.locator('.entity-image > img')
    await expect(image).toHaveCount(1)
    await h.page.evaluate(async id => {
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().setType('guild')
      await window.novalistStores.codex.getState().select(id)
    }, entry.id)
    await selectCodexTab(h.page, 'details')
    await expect.poll(() => image.evaluate((element: HTMLImageElement) =>
      [element.naturalWidth, element.naturalHeight]
    )).toEqual([12, 8])
  } finally {
    await h.close()
  }
})

test('custom type reference controls stay readable and the dialog can be resized', async () => {
  const h = await launchApp('nl-custom-type-layout-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.locator('.codex-tab-manage').click()
    const manager = h.page.locator('.type-manager-card')
    await manager.locator('.dialog-actions .primary').click()
    await manager.locator('.type-manager-fields > button').click()
    const field = manager.locator('.type-manager-field')
    await field.locator('select').first().selectOption('EntityRef')
    for (const width of [1200, 640]) {
      await resizeWindow(h, width, 850)
      for (const input of await field.locator('.dialog-input').all()) {
        const size = await input.boundingBox()
        expect(size!.width).toBeGreaterThanOrEqual(150)
      }
      expect(await manager.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    }
    await resizeWindow(h, 1200, 850)
    const before = (await manager.boundingBox())!
    await h.page.mouse.move(before.x + before.width - 3, before.y + before.height - 3)
    await h.page.mouse.down()
    await h.page.mouse.move(before.x + before.width - 103, before.y + before.height + 35, { steps: 10 })
    await h.page.mouse.up()
    expect((await manager.boundingBox())!.width).toBeLessThan(before.width - 50)
  } finally {
    await h.close()
  }
})

test('custom types support plain multiline text and multiple references through save and reopen', async () => {
  const h = await launchApp('nl-custom-field-values-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const first = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira, the Brave'])
    const second = await h.rpc<{ id: string }>('entities/create', ['character', 'Örn'])
    await h.rpc('entities/create', ['location', 'Hidden location'])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.locator('.codex-tab-manage').click()
    const manager = h.page.locator('.type-manager-card')
    await manager.locator('.dialog-actions .primary').click()
    await manager.locator('input.dialog-input').first().fill('Guild')
    for (const [name, type] of [['History', 'Text'], ['Members', 'EntityRefList']]) {
      await manager.locator('.type-manager-fields > button').click()
      const field = manager.locator('.type-manager-field').last()
      await field.locator('input').first().fill(name)
      await field.locator('select').first().selectOption(type)
    }
    await manager.locator('.dialog-actions .primary').click()
    await expect(manager.locator('.type-manager-field')).toHaveCount(0)
    await manager.locator('.type-manager-title-row button').click()
    const entry = await h.rpc<{ id: string }>('entities/create', ['guild', 'Watch'])
    await h.page.evaluate(async id => {
      await window.novalistStores.codex.getState().setType('guild')
      await window.novalistStores.codex.getState().select(id)
    }, entry.id)
    const history = h.page.getByRole('textbox', { name: 'History', exact: true })
    await expect(history).toHaveJSProperty('tagName', 'TEXTAREA')
    await history.fill('First line.\n**Plain text**, not formatting.')
    await history.press('Tab')
    const members = h.page.getByRole('group', { name: 'Members', exact: true })
    await expect(members.getByRole('checkbox')).toHaveCount(2)
    await members.getByRole('checkbox', { name: 'Mira, the Brave', exact: true }).check()
    await members.getByRole('checkbox', { name: 'Örn', exact: true }).check()
    await expect.poll(async () => (await h.rpc<{ fields: Record<string, string> }>('entities/get', ['guild', entry.id])).fields).toEqual({
      History: 'First line.\n**Plain text**, not formatting.', Members: JSON.stringify([first.id, second.id])
    })
    await h.page.evaluate(async id => {
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().setType('guild')
      await window.novalistStores.codex.getState().select(id)
    }, entry.id)
    await expect(history).toHaveValue('First line.\n**Plain text**, not formatting.')
    await expect(members.getByRole('checkbox', { checked: true })).toHaveCount(2)
    await members.getByRole('checkbox', { name: 'Mira, the Brave', exact: true }).uncheck()
    await expect.poll(async () => (await h.rpc<{ fields: Record<string, string> }>('entities/get', ['guild', entry.id])).fields.Members)
      .toBe(JSON.stringify([second.id]))
  } finally {
    await h.close()
  }
})

test('guided custom entry creation edits multiline text and reference lists', async () => {
  const h = await launchApp('nl-guided-custom-fields-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    const member = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    await h.rpc('entities/saveCustomType', [{ typeKey: 'guild', displayName: 'Guild', fields: [
      { key: 'History', displayName: 'History', type: 'Text', defaultValue: 'Default\nhistory' },
      { key: 'Members', displayName: 'Members', type: 'EntityRefList', enumOptions: ['character'] }
    ] }])
    await h.page.evaluate(async () => {
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('guild')
    })
    await h.page.getByRole('button', { name: 'New entry', exact: true }).first().click()
    const create = h.page.locator('.dialog-card').filter({ has: h.page.locator('#codex-create-name') })
    await create.locator('#codex-create-name').fill('Guided Watch')
    await create.getByRole('checkbox').check()
    await create.locator('.dialog-actions .primary').click()
    const wizard = h.page.getByRole('dialog', { name: 'Guild', exact: true })
    const history = wizard.getByRole('textbox', { name: 'History', exact: true })
    await expect(history).toHaveValue('Default\nhistory')
    await history.fill('A beginning.\nA second line.')
    await wizard.locator('.dialog-actions .primary').click()
    await wizard.getByRole('checkbox', { name: 'Mira', exact: true }).check()
    await wizard.locator('.dialog-actions .primary').click()
    await expect(wizard).toHaveCount(0)
    const entries = await h.rpc<{ id: string }[]>('entities/list', ['guild'])
    await expect.poll(async () => (await h.rpc<{ fields: Record<string, string> }>('entities/get', ['guild', entries[0].id])).fields)
      .toEqual({ History: 'A beginning.\nA second line.', Members: JSON.stringify([member.id]) })
  } finally {
    await h.close()
  }
})

test('reference-list defaults use their custom target type and unavailable entries can be removed', async () => {
  const h = await launchApp('nl-custom-ref-defaults-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.rpc('entities/saveCustomType', [{ typeKey: 'realm', displayName: 'Realm', displayNamePlural: 'Realms', fields: [] }])
    const realm = await h.rpc<{ id: string }>('entities/create', ['realm', 'North, Reach'])
    await h.rpc('entities/create', ['character', 'Not a realm'])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.locator('.codex-tab-manage').click()
    const manager = h.page.locator('.custom-type-manager-card')
    await manager.locator('.dialog-actions .primary').click()
    await manager.locator('input.dialog-input').first().fill('Guild')
    await manager.locator('.type-manager-fields > button').click()
    const field = manager.locator('.type-manager-field')
    await field.locator('input').first().fill('Realms')
    await field.locator('select').first().selectOption('EntityRefList')
    await field.locator('select').nth(1).selectOption('realm')
    const defaults = field.getByRole('group')
    await expect(defaults.getByRole('checkbox')).toHaveCount(1)
    await defaults.getByRole('checkbox', { name: 'North, Reach' }).check()
    await manager.locator('.dialog-actions .primary').click()
    await expect(manager.locator('.type-manager-field')).toHaveCount(0)
    await manager.locator('.type-manager-title-row button').click()
    const entry = await h.rpc<{ id: string }>('entities/create', ['guild', 'Watch'])
    const selectEntry = async (): Promise<void> => h.page.evaluate(async id => {
      await window.novalistStores.codex.getState().setType('guild')
      await window.novalistStores.codex.getState().select(id)
    }, entry.id)
    await selectEntry()
    const choices = h.page.getByRole('group', { name: 'Realms', exact: true })
    await expect(choices.getByRole('checkbox', { name: 'North, Reach' })).toBeChecked()
    await h.rpc('entities/delete', ['realm', realm.id, false])
    await h.page.evaluate(() => window.novalistStores.codex.getState().setType('character'))
    await expect(choices).toHaveCount(0)
    await selectEntry()
    await expect(choices).toHaveText('Unavailable entry')
    await choices.getByRole('checkbox', { name: 'Unavailable entry' }).click()
    await expect(choices.getByRole('checkbox')).toHaveCount(0)
    await expect.poll(async () => (await h.rpc<{ fields: Record<string, string> }>('entities/get', ['guild', entry.id])).fields.Realms).toBe('[]')
  } finally {
    await h.close()
  }
})
