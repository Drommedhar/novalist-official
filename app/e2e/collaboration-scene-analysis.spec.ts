import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'
import type { SceneContext } from '../src/renderer/src/shell/contextTypes'

test('shared scenes keep the writing language and update analysis without focusing the receiving fields', async () => {
  const writers: Harness[] = []
  try {
    const host = await launchApp('nl-analysis-host-')
    writers.push(host)
    const project = await seedBook(host, { Opening: ['Arrival'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const scene = chapter.scenes[0]
    await host.page.evaluate(() => window.novalistStores.settings.getState().update('project', { autoReplacementLanguage: 'de-guillemet' }))
    const prose = 'Angst und Panik erfüllten sie. Der Kampf drohte, und sie wollte fliehen.'
    await host.rpc('scenes/write', [chapter.guid, scene.id, `<p>${prose}</p>`, prose])
    const expected = (await host.rpc<SceneContext>('context/analyze', [chapter.guid, scene.id])).analysis
    expect(expected.conflict).not.toBe('')
    expect(expected.intensity).toBeLessThan(0)
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const link = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-analysis-guest-')
    writers.push(guest)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Alex'])
    expect((await guest.rpc<SceneContext>('context/analyze', [chapter.guid, scene.id])).analysis).toEqual(expected)
    for (const writer of writers) {
      await dismissTour(writer.page)
      await writer.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await expect(writer.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await writer.page.evaluate(async ({ chapter, scene }) => {
        await window.novalistStores.project.getState().openScene(chapter, scene)
        window.novalistStores.shell.getState().setInspectorTab('context')
      }, { chapter: chapter.guid, scene: scene.id })
      await expect(writer.page.getByPlaceholder('Conflict', { exact: true })).toHaveValue(expected.conflict)
    }
    const hostConflict = host.page.getByPlaceholder('Conflict', { exact: true })
    const guestConflict = guest.page.getByPlaceholder('Conflict', { exact: true })
    await hostConflict.focus()
    await expect(hostConflict).not.toHaveAttribute('readonly', '')
    await hostConflict.fill('A shared conflict')
    await hostConflict.blur()
    await expect(guestConflict).toHaveValue('A shared conflict')
    const intensity = guest.page.locator('.ctx-intensity-input')
    await intensity.focus()
    await expect(intensity).not.toHaveAttribute('readonly', '')
    await intensity.fill('7')
    await intensity.blur()
    await expect(host.page.locator('.ctx-intensity-input')).toHaveValue('7')
    await host.page.getByPlaceholder('Conflict', { exact: true }).locator('..').getByRole('button').click()
    await host.page.locator('.ctx-panel').click({ position: { x: 3, y: 3 } })
    await expect(guestConflict).toHaveValue(expected.conflict)
    await host.page.evaluate(() => window.novalistStores.settings.getState().update('project', { autoReplacementLanguage: 'en' }))
    await expect(guestConflict).toHaveValue('')
    await expect.poll(async () => (await guest.rpc<SceneContext>('context/analyze', [chapter.guid, scene.id])).analysis)
      .toEqual((await host.rpc<SceneContext>('context/analyze', [chapter.guid, scene.id])).analysis)
  } finally { for (const writer of writers.reverse()) await writer.close() }
})
