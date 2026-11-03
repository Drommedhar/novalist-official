import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('the top bar offers contextual collaboration without opening Settings', async ({}, testInfo) => {
  const desktop = await launchApp('novalist-collaboration-toolbar-')
  try {
    await dismissTour(desktop.page)
    const trigger = desktop.page.getByRole('button', { name: 'Collaboration', exact: true })
    await trigger.click()
    const panel = desktop.page.getByRole('dialog', { name: 'Collaboration', exact: true })
    await expect(panel).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Share', exact: true })).toBeDisabled()
    await expect(panel.getByRole('button', { name: 'Join', exact: true })).toHaveAttribute('aria-pressed', 'true')
    await expect(panel.getByLabel('Invitation link', { exact: true })).toBeVisible()
    await expect(panel.locator('option[value=steam]')).toHaveCount(0)
    expect(await desktop.page.evaluate(() => window.novalistStores.shell.getState().mainView)).not.toBe('settings')
    await desktop.page.keyboard.press('Escape')
    await expect(panel).toHaveCount(0)
    await expect(trigger).toBeFocused()
    await seedBook(desktop, { Opening: ['Arrival'] })
    await dismissTour(desktop.page)
    await trigger.click()
    await expect(panel.getByRole('button', { name: 'Share', exact: true })).toHaveAttribute('aria-pressed', 'true')
    await expect(panel.getByLabel('Invitation link', { exact: true })).toHaveCount(0)
    await panel.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await expect(panel.getByLabel('Reachable host address', { exact: true })).toBeVisible()
    await panel.getByRole('button', { name: 'Join', exact: true }).click()
    await expect(panel.getByRole('button', { name: 'Join', exact: true })).toHaveClass(/primary/)
    await expect(panel.getByRole('alert')).toHaveCount(0)
    await expect(panel.getByLabel('Reachable host address', { exact: true })).toHaveCount(0)
    await expect(panel.getByLabel('Invitation link', { exact: true })).toBeVisible()
    await desktop.page.screenshot({ path: testInfo.outputPath('collaboration-toolbar.png'), animations: 'disabled' })
    const box = await panel.boundingBox()
    const viewport = await desktop.page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
    expect(box!.width).toBeLessThanOrEqual(520)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width)
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height)
  } finally { await desktop.close() }
})

test('Steam is offered when the runtime is available', async () => {
  const desktop = await launchApp('novalist-collaboration-steam-choice-')
  try {
    await dismissTour(desktop.page)
    await desktop.page.evaluate(() => {
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> =>
        method === 'collaboration/steam/status' ? { available: true, displayName: 'Test Steam Writer', reason: '' } as T : request<T>(method, params)
    })
    await desktop.page.getByRole('button', { name: 'Collaboration', exact: true }).click()
    await expect(desktop.page.locator('#set-collaboration-mode option[value=steam]')).toHaveCount(1)
    await desktop.page.getByLabel('Connection type', { exact: true }).selectOption('steam')
    await expect(desktop.page.getByLabel('Your name', { exact: true })).toHaveValue('Test Steam Writer')
    await expect(desktop.page.getByLabel('Server address', { exact: true })).toHaveCount(0)
    await expect(desktop.page.getByRole('button', { name: 'Join Steam session', exact: true })).toBeDisabled()
  } finally { await desktop.close() }
})
