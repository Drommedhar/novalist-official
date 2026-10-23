import { expect, test } from '@playwright/test'
import { launchApp, dismissTour } from './harness'
import { identityProvider } from './fixtures/identityProvider'

test('settings signs in through the browser bridge, remembers the account, and signs out', async () => {
  const provider = await identityProvider()
  const h = await launchApp('novalist-account-')
  try {
    await dismissTour(h.page)
    await h.app.evaluate(({ shell }) => {
      shell.openExternal = async url => {
        const result = await fetch(url)
        if (!result.ok) throw new Error('Test browser could not finish sign-in.')
      }
    })
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await h.page.getByLabel('Server address', { exact: true }).fill(provider.address)
    await h.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(h.page.getByRole('status').filter({ hasText: 'Signed in as Test Writer' })).toBeVisible()
    await h.page.reload()
    await h.page.waitForFunction(() => Boolean(window.novalistStores?.shell.getState().backendVersion))
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(h.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
    await h.page.getByRole('button', { name: 'Sign out', exact: true }).click()
    await expect(h.page.getByText('Signed in as Test Writer', { exact: true })).toHaveCount(0)
    await expect(h.page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled()
  } finally { await h.close(); await provider.close() }
})

test('settings can cancel browser sign-in and displays discovery failures', async () => {
  const provider = await identityProvider()
  const h = await launchApp('novalist-account-cancel-')
  try {
    await dismissTour(h.page)
    await h.app.evaluate(({ shell }) => { shell.openExternal = async () => {} })
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await h.page.getByLabel('Server address', { exact: true }).fill(provider.address)
    await h.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await h.page.getByRole('button', { name: 'Cancel sign-in', exact: true }).click()
    await expect(h.page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled()
    await h.page.getByLabel('Server address', { exact: true }).fill(provider.address + '/missing')
    await h.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(h.page.getByRole('alert')).toContainText('This server has not enabled desktop sign-in.')
  } finally { await h.close(); await provider.close() }
})

test('restarting the local sign-in provider offers a fresh sign-in for a retained app profile', async () => {
  let provider = await identityProvider()
  let desktop = await launchApp('novalist-account-restart-')
  const address = provider.address
  const workDir = desktop.workDir
  try {
    await desktop.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await desktop.rpc('collaboration/account/signIn', [address])
    await desktop.page.evaluate(server => localStorage.setItem('novalist.collaboration.server', server), address)
    await desktop.close()
    await provider.close()
    provider = await identityProvider(() => 'writer', () => 'Test Writer', Number(new URL(address).port))
    desktop = await launchApp('novalist-account-restart-', {}, workDir)
    await dismissTour(desktop.page)
    await desktop.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await desktop.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(desktop.page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled()
    await expect(desktop.page.getByText('Signed in as Test Writer', { exact: true })).toHaveCount(0)
    await desktop.page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(desktop.page.getByText('Signed in as Test Writer', { exact: true })).toBeVisible()
  } finally { await desktop.close(); await provider.close() }
})
