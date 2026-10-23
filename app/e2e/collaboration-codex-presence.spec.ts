import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('Codex names viewers and editors on the matching entry and clears them after navigation', async () => {
  const apps: Harness[] = []
  try {
    const host = await launchApp('nl-codex-presence-host-')
    apps.push(host)
    await seedBook(host, { Chapter: ['Scene'] })
    const first = await host.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    await host.rpc('entities/create', ['character', 'Robin'])
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const invitation = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-codex-presence-guest-')
    apps.push(guest)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Alex'])
    for (const app of apps) {
      await dismissTour(app.page)
      await app.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
      await expect(app.page.getByRole('button', { name: 'End this session', exact: true })).toBeVisible()
      await app.page.evaluate(async id => {
        window.novalistStores.shell.getState().setMainView('codex')
        await window.novalistStores.codex.getState().setType('character')
        await window.novalistStores.codex.getState().select(id)
      }, first.id)
    }
    const mira = guest.page.locator('.codex-row').filter({ hasText: 'Mira' })
    const robin = guest.page.locator('.codex-row').filter({ hasText: 'Robin' })
    await expect(mira.getByRole('img', { name: 'Viewing: Morgan', exact: true })).toBeVisible()
    await expect(guest.page.locator('.codex-detail').getByText('Viewing: Morgan', { exact: true })).toBeVisible()
    await expect(robin.getByRole('img')).toHaveCount(0)
    const age = host.page.getByRole('textbox', { name: 'Age', exact: true })
    await age.focus()
    await expect(age).not.toHaveAttribute('readonly', '')
    await expect(mira.getByRole('img', { name: 'Editing: Morgan', exact: true })).toBeVisible()
    await expect(guest.page.locator('.codex-detail').getByText('Editing: Morgan', { exact: true })).toBeVisible()
    await expect(guest.page.locator('.codex-detail').getByRole('status', { name: 'Morgan is editing this field.', exact: true })).toBeVisible()
    await age.blur()
    await expect(guest.page.locator('.codex-detail').getByRole('status', { name: 'Morgan is editing this field.', exact: true })).toHaveCount(0)
    const name = host.page.getByRole('textbox', { name: 'Name', exact: true })
    const guestName = guest.page.locator('.codex-field').filter({ has: guest.page.getByRole('textbox', { name: 'Name', exact: true }) })
    await name.focus()
    await expect(name).not.toHaveAttribute('readonly', '')
    await expect(guestName.getByRole('status', { name: 'Morgan is editing this field.', exact: true })).toBeVisible()
    await name.blur()
    await expect(guestName.getByRole('status')).toHaveCount(0)
    await expect(mira.getByRole('img', { name: 'Viewing: Morgan', exact: true })).toBeVisible()
    await guest.page.locator('.codex-tab-table').click()
    const tableEntry = guest.page.getByRole('row').filter({ has: guest.page.getByRole('textbox', { name: 'Mira Name', exact: true }) })
    await expect(tableEntry.getByRole('img', { name: 'Viewing: Morgan', exact: true })).toBeVisible()
    await guest.page.locator('.codex-tab-table').click()
    await host.page.locator('.codex-row').filter({ hasText: 'Robin' }).click()
    await expect(robin.getByRole('img', { name: 'Viewing: Morgan', exact: true })).toBeVisible()
    await expect(mira.getByRole('img')).toHaveCount(0)
    await host.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await expect(robin.getByRole('img')).toHaveCount(0)
  } finally { for (const app of apps.reverse()) await app.close() }
})
