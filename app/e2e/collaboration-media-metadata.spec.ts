import { expect, test } from '@playwright/test'
import { writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('image descriptions and attachment names follow the focused writer across two desktops', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-media-owner-')
    participants.push(owner)
    const book = await seedBook(owner, { Opening: ['Arrival'] })
    const person = await owner.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    const source = join(owner.workDir, 'portrait.png')
    await writeFile(source, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64'))
    const record = await owner.rpc<{ images: { name: string; path: string }[] }>('entities/addImage', ['character', person.id, source, true])
    await owner.rpc('entities/setOverride', [person.id, book.chapters[0].guid, null, { age: '26' }])
    await owner.rpc('entities/setOverrideImages', [person.id, book.chapters[0].guid, null, record.images])
    await owner.rpc('entities/setOverride', [person.id, book.chapters[0].guid, 'Arrival', { age: '27' }])
    await owner.rpc('attachments/addLink', ['character', person.id, 'https://example.com/interview', 'Interview'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('novalist-media-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Guest'])
    for (const h of participants) {
      await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await expect(h.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await h.page.evaluate(async id => {
        window.novalistStores.shell.getState().setMainView('codex')
        await window.novalistStores.codex.getState().setType('character')
        await window.novalistStores.codex.getState().select(id)
      }, person.id)
      await h.page.locator('.codex-entry-tabs').getByRole('button', { name: 'Details', exact: true }).click()
    }
    const fields = [
      ['.entity-images:not(.overrides-media) .entity-image-namefield', 0, 'Portrait'],
      ['.entity-images:not(.overrides-media) .entity-image-namefield', 1, 'Mira beside the harbour'],
      ['.overrides-media .entity-image-namefield', 0, 'Arrival portrait'],
      ['.attachment-name', 0, 'Interview notes']
    ] as const
    for (const [selector, index, value] of fields) {
      for (const h of participants) {
        await h.page.locator('.codex-entry-tabs').getByRole('button', { name: selector.startsWith('.overrides') ? 'Story' : 'Details', exact: true }).click()
        if (selector.startsWith('.overrides')) await h.page.locator('.overrides-scope').first().click()
      }
      const first = owner.page.locator(selector).nth(index)
      const second = guest.page.locator(selector).nth(index)
      await first.focus()
      await expect(first).not.toHaveAttribute('readonly')
      await second.focus()
      await expect(second).toHaveAttribute('readonly')
      await expect(guest.page.getByText('Owner is editing this field.', { exact: true })).toBeVisible()
      await first.fill(value)
      await first.blur()
      await expect(second).not.toHaveAttribute('readonly')
      await expect(second).toHaveValue(value)
      await second.fill(`${value} revised`)
      await second.blur()
      await expect(first).toHaveValue(`${value} revised`)
    }
    for (const h of participants) {
      await h.page.locator('.codex-entry-tabs').getByRole('button', { name: 'Story', exact: true }).click()
      await h.page.locator('.overrides-scope').nth(1).click()
    }
    const inheritedOwner = owner.page.locator('.overrides-item').nth(1).locator('.entity-image-namefield')
    const inheritedGuest = guest.page.locator('.overrides-item').nth(1).locator('.entity-image-namefield')
    await expect(inheritedOwner).toBeVisible()
    await inheritedOwner.focus()
    await expect(inheritedOwner).toBeFocused()
    await expect(inheritedOwner).not.toHaveAttribute('readonly')
    await expect.poll(async () => {
      const entity = await guest.rpc<{ sharedFields: { metadata: Record<string, string> } }>('entities/get', ['character', person.id])
      return Object.keys(entity.sharedFields.metadata).some(key => key.includes(':Arrival:image:'))
    }).toBe(true)
    await inheritedGuest.focus()
    await expect(inheritedGuest).toHaveAttribute('readonly')
    await expect(guest.page.getByText('Owner is editing this field.', { exact: true })).toBeVisible()
    await inheritedOwner.fill('Scene-specific portrait')
    await inheritedOwner.blur()
    await expect(owner.page.locator('.media-metadata-input [role="alert"]')).toHaveCount(0)
    await expect(inheritedGuest).not.toHaveAttribute('readonly')
    await expect(inheritedGuest).toHaveValue('Scene-specific portrait')
    await inheritedGuest.blur()
    for (const h of participants) {
      const media = await h.rpc<{ images: { name: string; alt: string }[]; chapterOverrides: { images: { name: string }[] }[] }>('entities/get', ['character', person.id])
      expect(media.images[0]).toMatchObject({ name: 'Portrait revised', alt: 'Mira beside the harbour revised' })
      expect(media.chapterOverrides[0].images[0].name).toBe('Arrival portrait revised')
      expect(media.chapterOverrides[1].images[0].name).toBe('Scene-specific portrait')
      expect((await h.rpc<{ name: string }[]>('attachments/list', ['character', person.id]))[0].name).toBe('Interview notes revised')
    }
  } finally { for (const h of participants.reverse()) await h.close() }
})
