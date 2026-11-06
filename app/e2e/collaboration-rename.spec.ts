import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { randomUUID } from 'node:crypto'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

interface Entity {
  id: string
  name: string
  parent: string
  sections: { content: string }[]
  relationships: { role: string; target: string }[]
  sharedFields: { sessionId: string; fields: Record<string, string> }
}

test('a shared Codex rename updates open scene mentions and section links in both retained projects', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('novalist-rename-owner-')
    participants.push(owner)
    const book = await seedBook(owner, { Opening: ['Arrival'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    const person = await owner.rpc<Entity>('entities/create', ['character', 'Mira Vance'])
    const notes = await owner.rpc<Entity>('entities/create', ['lore', 'History'])
    const place = await owner.rpc<Entity>('entities/create', ['location', 'Harbour'])
    await owner.rpc('entities/update', ['location', place.id, { parent: 'Mira Vance' }])
    await owner.rpc('entities/updateLists', ['location', place.id, null, null, [{ role: 'Home', target: 'Mira Vance' }]])
    await owner.rpc('scenes/setPov', [chapter.guid, scene.id, 'Mira Vance'])
    await owner.rpc('entities/updateLists', ['lore', notes.id, null, [{ id: 'history', title: 'History', content: '[[Mira Vance]] and [[Mira Vance|Mira]], with bare Mira Vance.' }], null])
    const html = `<p><span class="nv-entity-mention" data-entity-id="${person.id}">Mira Vance</span> arrives.</p>`
    await owner.rpc('scenes/write', [chapter.guid, scene.id, html, 'Mira Vance arrives.'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const link = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('novalist-rename-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Guest'])
    for (const participant of participants) {
      await participant.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene),
        { chapter: chapter.guid, scene: scene.id })
      await expect(participant.page.frameLocator('.editor-frame').locator('#editor')).toHaveText('Mira Vance arrives.')
    }
    const current = await owner.rpc<Entity>('entities/get', ['character', person.id])
    const { sessionId, fields } = current.sharedFields
    const focus = randomUUID()
    await owner.rpc('collaboration/acquireField', [sessionId, fields.name, focus])
    const parent = (await guest.rpc<Entity>('entities/get', ['location', place.id])).sharedFields
    const parentFocus = randomUUID()
    await guest.rpc('collaboration/acquireField', [parent.sessionId, parent.fields.parent, parentFocus])
    await expect(owner.rpc('collaboration/writeField', [sessionId, fields.name, focus, 'Mira Calder'])).rejects.toThrow()
    expect((await owner.rpc<Entity>('entities/get', ['character', person.id])).name).toBe('Mira Vance')
    await guest.rpc('collaboration/releaseField', [parent.sessionId, parent.fields.parent, parentFocus])
    await owner.rpc('collaboration/writeField', [sessionId, fields.name, focus, 'Mira Calder'])
    await owner.rpc('collaboration/releaseField', [sessionId, fields.name, focus])
    for (const participant of participants) {
      await expect(participant.page.frameLocator('.editor-frame').locator('#editor')).toHaveText('Mira Calder arrives.')
      await expect.poll(async () => (await participant.rpc<Entity>('entities/get', ['lore', notes.id])).sections[0].content)
        .toBe('[[Mira Calder]] and [[Mira Calder|Mira]], with bare Mira Vance.')
      await expect.poll(async () => (await participant.rpc<Entity>('entities/get', ['location', place.id])).parent).toBe('Mira Calder')
      await expect.poll(async () => (await participant.rpc<Entity>('entities/get', ['location', place.id])).relationships[0].target).toBe('Mira Calder')
      await expect.poll(async () => (await participant.rpc<{ pov: string }>('project/getSceneEdit', [chapter.guid, scene.id])).pov).toBe('Mira Calder')
    }
    for (const participant of participants.slice().reverse()) {
      await participant.rpc('collaboration/end')
      expect((await participant.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Mira Calder')
    }
  } finally { for (const participant of participants.reverse()) await participant.close() }
})
