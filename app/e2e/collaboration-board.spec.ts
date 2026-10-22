import { expect, test, type Locator } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function drop(card: Locator, target: Locator): Promise<void> {
  const fire = (element: Element, type: string): void => {
    element.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() }))
  }
  await card.evaluate(fire, 'dragstart')
  await target.evaluate(fire, 'dragover')
  await expect(target).toHaveClass(/\bover\b/)
  await target.evaluate(fire, 'drop')
}

test('Board POV and property drops honor named focus locks and synchronize after release', async () => {
  const writers: Harness[] = []
  try {
    const owner = await launchApp('nl-board-owner-')
    writers.push(owner)
    const project = await seedBook(owner, { Opening: ['Arrival', 'Departure'] })
    const chapter = project.chapters[0]
    const [first, second] = chapter.scenes
    await owner.rpc('scenes/setPov', [chapter.guid, second.id, 'Morgan'])
    await owner.rpc('manuscriptProps/setDefinitions', [[{ key: 'weather', label: 'Weather', type: 'Enum', scope: 'Scene', enumOptions: ['Fog', 'Sun'], showInOutliner: true }]])
    await owner.rpc('manuscriptProps/setSceneValue', [first.id, 'weather', 'Fog'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-board-guest-')
    writers.push(guest)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Guest'])
    for (const writer of writers) {
      await writer.page.waitForFunction(() => {
        const project = window.novalistStores.project.getState()
        return project.isLoaded && !project.workspaceBusy && !project.workspaceTransitionToken
      })
      await dismissTour(writer.page)
      await writer.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
      await expect(writer.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await writer.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    }
    await owner.page.getByRole('button', { name: 'Board', exact: true }).click()
    await owner.page.locator('#board-group').selectOption('pov')
    await guest.page.getByRole('button', { name: 'Outliner', exact: true }).click()
    const pov = guest.page.locator('.outliner').getByPlaceholder('POV', { exact: true }).first()
    await pov.focus()
    await expect(pov).not.toHaveAttribute('readonly')
    const card = owner.page.locator('.board-card', { hasText: 'Arrival' })
    const target = owner.page.locator('.board-column').filter({ has: owner.page.locator('.board-column-title', { hasText: 'Morgan' }) })
    await drop(card, target)
    await expect(owner.page.getByText('Guest is editing this field.', { exact: true })).toBeVisible()
    await expect(target.locator('.board-card')).toHaveCount(1)
    await pov.blur()
    await expect(pov).toHaveAttribute('readonly')
    await drop(card, target)
    await expect(target.locator('.board-card')).toHaveCount(2)
    await expect(pov).toHaveValue('Morgan')
    await owner.page.locator('#board-group').selectOption('prop:weather')
    const weather = guest.page.locator('.outliner').getByLabel('Weather', { exact: true }).first()
    await weather.focus()
    await expect(weather).toHaveAttribute('aria-disabled', 'false')
    const sunny = owner.page.locator('.board-column').filter({ has: owner.page.locator('.board-column-title', { hasText: 'Sun' }) })
    await drop(card, sunny)
    await expect(sunny.locator('.board-card')).toHaveCount(0)
    await expect(weather).toHaveValue('Fog')
    await weather.blur()
    await expect(weather).toHaveAttribute('aria-disabled', 'true')
    await drop(card, sunny)
    await expect(sunny.locator('.board-card')).toHaveCount(1)
    await expect(weather).toHaveValue('Sun')
    expect(await guest.rpc('manuscriptProps/allSceneValues')).toMatchObject({ [first.id]: { weather: 'Sun' } })
  } finally {
    for (const writer of writers.reverse()) await writer.close()
  }
})
