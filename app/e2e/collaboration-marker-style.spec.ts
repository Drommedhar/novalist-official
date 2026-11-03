import { expect, test, type Locator } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function markerStyle(marker: Locator) {
  await expect(marker).toBeVisible()
  return marker.evaluate(element => {
    const style = getComputedStyle(element)
    return { background: style.backgroundColor, color: style.color, fontSize: style.fontSize,
      fontFamily: style.fontFamily, padding: style.padding, borderRadius: style.borderRadius }
  })
}

test('Codex and Manuscript cursor labels match the scene editor', async () => {
  const writers: Harness[] = []
  try {
    const host = await launchApp('nl-marker-host-')
    writers.push(host)
    const project = await seedBook(host, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const scene = chapter.scenes[0]
    const entity = await host.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    await host.rpc('scenes/write', [chapter.guid, scene.id, '<p>Shared prose.</p>', 'Shared prose.'])
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const link = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-marker-guest-')
    writers.push(guest)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Alex'])
    for (const writer of writers) {
      await dismissTour(writer.page)
      await writer.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
      await expect(writer.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await writer.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene),
        { chapter: chapter.guid, scene: scene.id })
    }
    await host.page.frameLocator('iframe[title="editor"]').locator('#editor').click()
    const style = await markerStyle(guest.page.frameLocator('iframe[title="editor"]').locator('.nv-collaboration-label'))
    await guest.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    expect(await markerStyle(guest.page.frameLocator('iframe[title="manuscript"]').locator('.nv-collaboration-label'))).toEqual(style)
    for (const writer of writers) {
      await writer.page.evaluate(async id => {
        window.novalistStores.shell.getState().setMainView('codex')
        await window.novalistStores.codex.getState().setType('character')
        await window.novalistStores.codex.getState().select(id)
      }, entity.id)
      await writer.page.getByRole('button', { name: 'Details', exact: true }).click()
    }
    await host.page.locator('.cm-content[aria-label="Distinguishing Features"]').fill('A visible mark.')
    expect(await markerStyle(guest.page.locator('.nv-collaboration-label'))).toEqual(style)
    for (const writer of writers) await writer.page.getByRole('button', { name: 'Overview', exact: true }).click()
    const name = host.page.getByRole('textbox', { name: 'Name', exact: true })
    const guestName = guest.page.getByRole('textbox', { name: 'Name', exact: true })
    const field = guest.page.locator('.codex-field').filter({ has: guestName })
    await name.focus()
    const marker = field.locator('.nv-collaboration-label')
    await expect(marker).toHaveText('Morgan')
    expect(await markerStyle(marker)).toEqual(style)
    await guestName.focus()
    await expect(guestName).toHaveAttribute('readonly', '')
    await expect(marker).toHaveText('Morgan')
    expect(await markerStyle(marker)).toEqual(style)
    await name.blur()
    await expect(marker).toHaveCount(0)
  } finally { for (const writer of writers.reverse()) await writer.close() }
})
