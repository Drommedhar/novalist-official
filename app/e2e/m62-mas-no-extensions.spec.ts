import { test, expect } from '@playwright/test'
import { launchApp } from './harness'

/**
 * The Mac App Store build has no extension feature, and says so.
 *
 * An extension is a .NET assembly the app downloads and runs, and it adds views,
 * commands and hooks once it is there - which the App Store does not allow an
 * app to do, and which the sandbox that build runs under would not load anyway.
 * So the feature is absent there rather than degraded.
 *
 * Absent has to mean absent on both sides. The renderer half is easy to see and
 * easy to get wrong on its own: hiding the buttons while the backend still
 * seeds, discovers and installs would be a build that looks compliant and is
 * not. So this drives the real UI route AND asks the backend directly, in one
 * launch, because either half passing alone proves nothing.
 *
 * NOVALIST_FORCE_MAS stands in for process.mas, which only a real MAS package
 * sets. It is read in exactly the two places the build itself is: the preload
 * that tells the renderer, and the spawn that tells the backend.
 */
test('the App Store build offers no extensions and explains why', async () => {
  test.setTimeout(120_000)

  const h = await launchApp('nl-mas-', { NOVALIST_FORCE_MAS: '1' })
  try {
    await h.page.evaluate(() =>
      window.novalistStores.shell.getState().setMainView('extensions')
    )

    // What a writer who goes looking for extensions actually gets: the reason,
    // and where the feature does exist.
    const view = h.page.locator('.extensions-view')
    await expect(view).toBeVisible()
    await expect(view.getByText(/App Store rules do not allow/i)).toBeVisible()
    await expect(view.getByText(/downloaded directly/i)).toBeVisible()

    // And no onboarding card above it offering to find and install them.
    await expect(h.page.locator('[data-view-intro="extensions"]')).toHaveCount(0)

    // None of the machinery. These are the three ways in on a normal build.
    await expect(view.getByRole('button', { name: /Install from Folder/i })).toHaveCount(0)
    await expect(view.getByRole('button', { name: /Browse Store/i })).toHaveCount(0)
    await expect(view.getByRole('button', { name: /Open Extensions Folder/i })).toHaveCount(0)

    // And the backend agrees, which is the half a hidden button would not fix.
    expect(await h.rpc('extensions/views')).toEqual([])
    expect(await h.rpc('extensions/commands')).toEqual([])
    expect(await h.rpc('store/index')).toEqual([])

    const install = await h.rpc<{ success: boolean; error: string | null }>('store/install', [
      'com.novalist.sample',
      'owner/sample'
    ])
    expect(install.success).toBe(false)
    expect(install.error).toBe('disabled')
  } finally {
    await h.close()
  }
})
