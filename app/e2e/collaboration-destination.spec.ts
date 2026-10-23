import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

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

test('synchronization progress and cancellation stay accessible while the workspace is busy', async () => {
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
