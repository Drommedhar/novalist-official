import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('Codex table cells hand focus to the named writer and retain other columns', async () => {
  const apps: Harness[] = []
  try {
    const owner = await launchApp('nl-table-owner-')
    apps.push(owner)
    await seedBook(owner, { Chapter: ['Scene'] })
    const entity = await owner.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    await owner.rpc('entities/update', ['character', entity.id, { surname: 'Vale' }])
    const lore = await owner.rpc<{ id: string }>('entities/create', ['lore', 'Charter'])
    await owner.rpc('entities/update', ['lore', lore.id, { description: 'Shared start.' }])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-table-guest-')
    apps.push(guest)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Alex'])
    for (const app of apps) {
      await dismissTour(app.page)
      await app.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await expect(app.page.getByRole('button', { name: 'End this session', exact: true })).toBeVisible()
      await app.page.evaluate(async () => {
        window.novalistStores.shell.getState().setMainView('codex')
        await window.novalistStores.codex.getState().setType('character')
      })
      await app.page.locator('.codex-tab-table').click()
    }
    const ownerRow = owner.page.locator('.codex-table tbody tr').first()
    const guestRow = guest.page.locator('.codex-table tbody tr').first()
    const ownerGroup = ownerRow.locator('input').last()
    const guestGroup = guestRow.locator('input').last()
    await ownerGroup.focus()
    await expect(ownerGroup).not.toHaveAttribute('readonly', '')
    await ownerGroup.fill('House Raven')
    await guestGroup.focus()
    await expect(guestRow.getByText('Morgan is editing this field.', { exact: true })).toBeVisible()
    await expect(guestGroup).toHaveAttribute('readonly', '')
    await ownerGroup.blur()
    await expect(guestGroup).not.toHaveAttribute('readonly', '')
    await expect(guestGroup).toHaveValue('House Raven')
    await guestGroup.fill('House Owl')
    await guestGroup.blur()
    const name = ownerRow.locator('input').first()
    await name.focus()
    await expect(name).not.toHaveAttribute('readonly', '')
    await expect(name).toHaveValue('Mira')
    await name.fill('Miri')
    await name.blur()
    await expect.poll(() => guest.rpc('entities/get', ['character', entity.id])).toMatchObject({
      name: 'Miri', surname: 'Vale', group: 'House Owl'
    })
    for (const app of apps) await app.page.evaluate(() => window.novalistStores.codex.getState().setType('lore'))
    const ownerDescription = owner.page.locator('.codex-table .cm-content')
    const guestDescription = guest.page.locator('.codex-table .cm-content')
    await expect(ownerDescription).toContainText('Shared start.')
    await ownerDescription.press('Control+End')
    await ownerDescription.pressSequentially(' Owner prose.')
    await guestDescription.press('Control+Home')
    await guestDescription.pressSequentially('Guest prose. ')
    for (const description of [ownerDescription, guestDescription]) {
      await expect(description).toContainText('Guest prose. Shared start. Owner prose.')
    }
    await expect.poll(() => owner.rpc('entities/get', ['lore', lore.id])).toMatchObject({
      description: 'Guest prose. Shared start. Owner prose.'
    })
  } finally { for (const app of apps.reverse()) await app.close() }
})
