import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('structured Codex controls enforce the named focus owner and refresh before the next writer edits', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-metadata-owner-')
    participants.push(owner)
    await seedBook(owner, { Opening: ['Arrival'] })
    const entity = await owner.rpc<{ id: string }>('entities/create', ['character', 'Mira', null])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const link = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('novalist-metadata-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Guest'])
    for (const h of participants) {
      await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await expect(h.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await h.page.evaluate(async id => {
        window.novalistStores.shell.getState().setMainView('codex')
        await window.novalistStores.codex.getState().setType('character')
        await window.novalistStores.codex.getState().select(id)
      }, entity.id)
    }
    const first = owner.page.locator('.entity-lists .entity-chip-input').first()
    const second = guest.page.locator('.entity-lists .entity-chip-input').first()
    await first.focus()
    await expect(first).not.toHaveAttribute('readonly')
    await second.focus()
    await expect(second).toHaveAttribute('readonly')
    await expect(guest.page.getByText('Owner is editing this field.', { exact: true })).toBeVisible()
    await first.fill('Captain')
    await first.press('Enter')
    await first.blur()
    await expect(second).not.toHaveAttribute('readonly')
    await expect(guest.page.locator('.entity-lists .entity-chip', { hasText: 'Captain' })).toBeVisible()
    await second.fill('Navigator')
    await second.press('Enter')
    await second.blur()
    await expect(owner.page.locator('.entity-lists .entity-chip', { hasText: 'Navigator' })).toBeVisible()
    for (const h of participants) {
      expect((await h.rpc<{ aliases: string[] }>('entities/get', ['character', entity.id])).aliases).toEqual(['Captain', 'Navigator'])
    }
  } finally { for (const h of participants.reverse()) await h.close() }
})
