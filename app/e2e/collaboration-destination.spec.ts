import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('joining a new local copy needs only an invitation and a destination folder', async ({}, testInfo) => {
  const host = await launchApp('novalist-new-copy-host-')
  const guest = await launchApp('novalist-new-copy-guest-')
  try {
    await seedBook(host, { Opening: ['Arrival'] })
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const invitation = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 3_600_000).toISOString()])
    await dismissTour(guest.page)
    await guest.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, guest.workDir)
    await guest.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await guest.page.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await guest.page.getByLabel('Your name', { exact: true }).fill('Alex')
    await guest.page.getByLabel('Invitation link', { exact: true }).fill(invitation)
    const join = guest.page.getByRole('button', { name: 'Join direct session', exact: true })
    await expect(join).toBeDisabled()
    await expect(guest.page.getByLabel('Local project folder name', { exact: true })).toHaveValue('Shared project')
    await expect(guest.page.getByText('Choose where to save your local project copy.', { exact: true })).toBeVisible()
    await guest.page.getByLabel('Local project folder name', { exact: true }).fill('../Other')
    await expect(guest.page.getByRole('alert')).toHaveText('Enter a valid folder name without special characters or a trailing dot.')
    await expect(join).toBeDisabled()
    await guest.page.getByLabel('Local project folder name', { exact: true }).fill('Shared project')
    await guest.page.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
    await expect(join).toBeEnabled()
    await guest.page.screenshot({ path: testInfo.outputPath('collaboration-settings.png'), animations: 'disabled' })
    await join.click()
    await expect.poll(() => guest.page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(true)
    await expect.poll(() => guest.page.evaluate(() => window.novalistStores.project.getState().chapters[0]?.title)).toBe('Opening')
  } finally { await guest.close(); await host.close() }
})

test('joining can select an existing local copy in relay and direct connection modes', async () => {
  const desktop = await launchApp('novalist-existing-copy-')
  try {
    await dismissTour(desktop.page)
    await desktop.app.evaluate(({ dialog }, folder) => {
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
    }, desktop.workDir)
    await desktop.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    for (const mode of ['hosted', 'tcp']) {
      await desktop.page.getByLabel('Connection type', { exact: true }).selectOption(mode)
      await expect(desktop.page.getByLabel('Use an existing local project', { exact: true })).toBeVisible()
      await desktop.page.getByLabel('Use an existing local project', { exact: true }).check()
      await desktop.page.getByRole('button', { name: 'Choose existing project folder', exact: true }).click()
      await expect(desktop.page.getByText(desktop.workDir, { exact: true })).toBeVisible()
      await expect(desktop.page.getByLabel('Local project folder name', { exact: true })).toHaveCount(0)
      await expect(desktop.page.getByText('Matching files are reused. Local differences are kept in Recovered edits before synchronization.', { exact: true })).toBeVisible()
    }
  } finally { await desktop.close() }
})

test('synchronization progress and cancellation stay accessible while the workspace is busy', async ({}, testInfo) => {
  const desktop = await launchApp('novalist-transfer-progress-')
  try {
    await seedBook(desktop, { Opening: ['Arrival'] })
    await dismissTour(desktop.page)
    await desktop.page.evaluate(() => {
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      let cancel: (() => void) | undefined
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'collaboration/peer/host') {
          window.novalistStores.project.setState({ workspaceBusy: true })
          return new Promise<T>((_, reject) => { cancel = () => reject(new Error('Synchronization cancelled.')) })
        }
        if (method === 'collaboration/cancel') {
          window.novalistStores.project.setState({ workspaceBusy: false })
          cancel?.()
          return undefined as T
        }
        return request<T>(method, params)
      }
      window.novalistStores.shell.getState().openSettings('collaboration')
    })
    await desktop.page.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await desktop.page.getByLabel('Your name', { exact: true }).fill('Morgan')
    await desktop.page.getByRole('button', { name: 'Host direct session', exact: true }).click()
    const progress = desktop.page.getByRole('progressbar', { name: 'Synchronization progress' })
    await expect(progress).toBeVisible()
    const layout = await progress.evaluate(element => {
      const card = element.closest('.dialog-card')!.getBoundingClientRect()
      const bar = element.getBoundingClientRect()
      const button = element.closest('.dialog-card')!.querySelector('button')!.getBoundingClientRect()
      return { card: card.x + card.width / 2, bar: bar.x + bar.width / 2,
        button: button.x + button.width / 2, width: bar.width, available: card.width }
    })
    expect(Math.abs(layout.card - layout.bar)).toBeLessThan(2)
    expect(Math.abs(layout.card - layout.button)).toBeLessThan(2)
    expect(layout.width / layout.available).toBeGreaterThan(0.7)
    await desktop.page.screenshot({ path: testInfo.outputPath('collaboration-progress.png'), animations: 'disabled' })
    await desktop.page.getByRole('button', { name: 'Cancel synchronization', exact: true }).click()
    await expect(progress).toHaveCount(0)
    await expect(desktop.page.getByRole('button', { name: 'Host direct session', exact: true })).toBeEnabled()
  } finally { await desktop.close() }
})

test('host preparation stays pending until startup succeeds and clears progress on failure', async () => {
  const desktop = await launchApp('novalist-host-startup-')
  try {
    await seedBook(desktop, { Opening: ['Arrival'] })
    await dismissTour(desktop.page)
    await desktop.page.evaluate(() => {
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      let fail: (() => void) | undefined
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'collaboration/peer/host') {
          await request<T>(method, params)
          document.body.dataset.hostStartupReply = 'received'
          return new Promise<T>((_, reject) => { fail = () => reject(new Error('Session startup failed.')) })
        }
        if (method === 'collaboration/cancel') {
          fail?.()
          return undefined as T
        }
        return request<T>(method, params)
      }
      window.novalistStores.shell.getState().openSettings('collaboration')
    })
    await desktop.page.getByLabel('Connection type', { exact: true }).selectOption('tcp')
    await desktop.page.getByLabel('Your name', { exact: true }).fill('Morgan')
    await desktop.page.getByRole('button', { name: 'Host direct session', exact: true }).click()
    const dialog = desktop.page.getByRole('dialog', { name: 'Synchronization progress' })
    await expect(dialog.getByRole('status')).toHaveText('Starting the live session…')
    await expect(dialog.getByRole('progressbar')).not.toHaveAttribute('value')
    await expect(dialog.getByText(/transferred|reused|Project synchronized\./)).toHaveCount(0)
    await expect(desktop.page.locator('body')).toHaveAttribute('data-host-startup-reply', 'received')
    await dialog.getByRole('button', { name: 'Cancel synchronization', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    await expect(desktop.page.getByText('Ready to invite collaborators.', { exact: true })).toHaveCount(0)
    await expect(desktop.page.getByText('Error: Session startup failed.', { exact: true }).first()).toBeVisible()
    await desktop.rpc('collaboration/end')
  } finally { await desktop.close() }
})
