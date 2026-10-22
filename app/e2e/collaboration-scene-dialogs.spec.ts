import { expect, test, type Page } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, enterWriting, launchApp, seedBook, type Harness } from './harness'

async function openSceneDialog(page: Page, name: string) {
  await page.locator('.binder-scene-row').first().click({ button: 'right' })
  await page.getByRole('menuitem', { name, exact: true }).click()
  return page.getByRole('dialog', { name, exact: true })
}

test('shared scene and story-date dialogs retain focus locks and save on Close', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-scene-dialog-owner-')
    participants.push(owner)
    const book = await seedBook(owner, { Opening: ['Arrival'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const link = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('novalist-scene-dialog-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Guest'])
    await enterWriting(guest.page)
    const ownerDialog = await openSceneDialog(owner.page, 'Rename Scene')
    await expect(ownerDialog.locator('input').first()).not.toHaveAttribute('readonly')
    const guestDialog = await openSceneDialog(guest.page, 'Rename Scene')
    await expect(guestDialog.locator('input').first()).toHaveAttribute('readonly')
    await expect(guestDialog).toContainText('Owner')
    await ownerDialog.locator('input').first().fill('Shared arrival')
    await ownerDialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(guestDialog.locator('input').first()).not.toHaveAttribute('readonly')
    await expect(guestDialog.locator('input').first()).toHaveValue('Shared arrival')
    await guestDialog.locator('input').nth(1).focus()
    await expect(guestDialog.locator('input').nth(1)).not.toHaveAttribute('readonly')
    await guestDialog.locator('input').nth(1).fill('Mira')
    await guestDialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect.poll(async () => (await owner.rpc<{ pov: string }>('project/getSceneEdit', [chapter.guid, scene.id])).pov).toBe('Mira')

    const ownerDates = await openSceneDialog(owner.page, 'Set Date')
    await ownerDates.locator('input').first().focus()
    await expect(ownerDates.locator('input').first()).not.toHaveAttribute('readonly')
    await ownerDates.locator('input').first().fill('2031-04-06')
    const guestDates = await openSceneDialog(guest.page, 'Set Date')
    await guestDates.locator('input').first().focus()
    await expect(guestDates.locator('input').first()).toHaveAttribute('readonly')
    await ownerDates.locator('input').nth(2).fill('The day they arrive')
    await ownerDates.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(guestDates.locator('input').first()).not.toHaveAttribute('readonly')
    await expect(guestDates.locator('input').first()).toHaveValue('2031-04-06')
    await expect(guestDates.locator('input').nth(2)).toHaveValue('The day they arrive')
    await guestDates.getByRole('button', { name: 'Clear', exact: true }).click()
    await guestDates.getByRole('button', { name: 'Close', exact: true }).click()
    await expect.poll(async () => (await owner.rpc<{ dateStart: string }>('project/getSceneEdit', [chapter.guid, scene.id])).dateStart).toBe('')
  } finally { for (const participant of participants.reverse()) await participant.close() }
})
