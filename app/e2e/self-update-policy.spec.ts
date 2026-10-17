import { expect, test } from '@playwright/test'
import { launchApp, type Harness } from './harness'

type UpdateRequests = typeof globalThis & { __appUpdateRequests: number }

const update: AppUpdate = {
  version: '999.0.0',
  tagName: 'v999.0.0',
  htmlUrl: 'https://updates.test/release',
  notes: 'Test release',
  downloadUrl: 'https://updates.test/installer',
  assetName: 'novalist-policy-test.exe',
  assetSize: 1
}

async function interceptAppUpdates(h: Harness): Promise<void> {
  await h.app.evaluate(() => {
    const root = globalThis as UpdateRequests
    root.__appUpdateRequests = 0
    globalThis.fetch = async () => {
      root.__appUpdateRequests += 1
      return new Response(JSON.stringify({
        tag_name: 'v999.0.0',
        assets: ['test.exe', 'test-x64.dmg', 'test-arm64.dmg', 'test.AppImage'].map((name) => ({
          name,
          browser_download_url: 'https://updates.test/installer',
          size: 1
        }))
      }), { headers: { 'content-type': 'application/json' } })
    }
  })
}

async function hasUpdateMenu(h: Harness): Promise<boolean> {
  return h.app.evaluate(({ Menu }) => {
    const help = Menu.getApplicationMenu()?.items.find((item) => item.role === 'help')
    return help?.submenu?.items.some((item) => /check for updates/i.test(item.label)) ?? false
  })
}

test('disabled self-update rejects every app update entry point and hides the menu item', async () => {
  const h = await launchApp('novalist-update-disabled-', { NOVALIST_DISABLE_SELF_UPDATE: '1' })
  try {
    await interceptAppUpdates(h)
    expect(await h.page.evaluate(() => window.novalist.checkAppUpdate())).toBeNull()
    await expect(h.page.evaluate((info) => window.novalist.downloadAppUpdate(info), update))
      .rejects.toThrow('Self-update is disabled in this build.')
    await expect(h.page.evaluate(() => window.novalist.launchAppUpdate('stale-token')))
      .rejects.toThrow('Self-update is disabled in this build.')
    expect(await h.app.evaluate(() => (globalThis as UpdateRequests).__appUpdateRequests)).toBe(0)
    expect(await hasUpdateMenu(h)).toBe(false)
  } finally {
    await h.close()
  }
})

for (const flag of ['', '0']) {
  test(`self-update remains available when the disable flag is ${flag || 'unset'}`, async () => {
    const h = await launchApp('novalist-update-enabled-', { NOVALIST_DISABLE_SELF_UPDATE: flag })
    try {
      await interceptAppUpdates(h)
      expect(await h.page.evaluate(() => window.novalist.checkAppUpdate()))
        .toMatchObject({ version: '999.0.0' })
      expect(await h.app.evaluate(() => (globalThis as UpdateRequests).__appUpdateRequests)).toBe(1)
      await expect.poll(() => hasUpdateMenu(h)).toBe(true)
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('about'))
      await expect(h.page.getByRole('button', { name: 'Check for updates', exact: true })).toBeVisible()
      await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('updatesIntegrations'))
      await expect(h.page.locator('#set-check-updates')).toBeVisible()
    } finally {
      await h.close()
    }
  })
}

for (const surface of ['about', 'settings', 'settings-search'] as const) {
  test(`disabled self-update hides application update controls in ${surface}`, async () => {
    const h = await launchApp('novalist-update-controls-', { NOVALIST_DISABLE_SELF_UPDATE: '1' })
    try {
      if (surface === 'about') {
        await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('about'))
        await expect(h.page.locator('.about-view')).toBeVisible()
        await expect(h.page.getByRole('button', { name: 'Check for updates', exact: true })).toHaveCount(0)
      } else {
        await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('updatesIntegrations'))
        await expect(h.page.locator('#set-extension-updates')).toBeVisible()
        if (surface === 'settings') {
          await expect(h.page.locator('#set-check-updates')).toHaveCount(0)
        } else {
          await h.page.getByRole('searchbox', { name: 'Search settings...' }).fill('check for updates')
          await expect(h.page.locator('.settings-result-title', { hasText: /^Check for updates$/i })).toHaveCount(0)
          await h.page.getByRole('searchbox', { name: 'Search settings...' }).fill('extension updates')
          await expect(h.page.locator('.settings-result-title', { hasText: /^Check for extension updates$/i })).toBeVisible()
        }
      }
    } finally {
      await h.close()
    }
  })
}

test('startup still offers extension updates when app self-update is disabled', async () => {
  const h = await launchApp('novalist-extension-startup-', {
    NOVALIST_DISABLE_SELF_UPDATE: '1',
    NOVALIST_NO_SPLASH: ''
  })
  try {
    await interceptAppUpdates(h)
    // Install the extension-store fixture before a fresh renderer runs its startup effects.
    await h.page.addInitScript(() => {
      Object.defineProperty(window, 'novalistStores', {
        configurable: true,
        set(stores: Window['novalistStores']) {
          Object.defineProperty(window, 'novalistStores', { value: stores, configurable: true })
          stores.extensions.setState({
            checkStoreUpdates: async () => {
              document.documentElement.dataset.extensionChecks = '1'
              stores.extensions.setState({ storeUpdates: [{
                extensionId: 'policy-test', name: 'Startup extension', repo: 'test/extension',
                installedVersion: '1.0.0', availableVersion: '2.0.0'
              }] })
              return 1
            },
            installFromStore: async () => {
              document.documentElement.dataset.extensionInstalled = '1'
              stores.extensions.setState({ storeUpdates: [] })
              return { id: 'policy-test', success: true, error: null }
            }
          })
        }
      })
    })
    await h.page.reload()
    await expect(h.page.locator('html')).toHaveAttribute('data-extension-checks', '1')
    expect(await h.page.evaluate(() => window.novalist.autoUpdate)).toBe(true)
    await h.page.getByRole('button', { name: 'Review update', exact: true }).click()
    const dialog = h.page.getByRole('dialog', { name: 'Update Available' })
    await expect(dialog.getByText('Startup extension', { exact: true })).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Download update', exact: true })).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Update', exact: true }).click()
    await expect(h.page.locator('html')).toHaveAttribute('data-extension-installed', '1')
    await expect(h.page.getByRole('dialog', { name: 'All extensions are up to date.' })).toBeVisible()
    await expect(h.page.locator('.update-dialog')).not.toContainText('You are running the latest version')
    expect(await h.app.evaluate(() => (globalThis as UpdateRequests).__appUpdateRequests)).toBe(0)
    expect(await hasUpdateMenu(h)).toBe(false)
  } finally {
    await h.close()
  }
})
