import { test, expect } from '@playwright/test'
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test('Toolkit status bar opens the sprint board from the manuscript', async () => {
  const build = resolve('../../novalist-extension/Novalist.Extensions.Toolkit/bin/Debug/net10.0')
  test.skip(!existsSync(join(build, 'Novalist.Extensions.Toolkit.dll')), 'Toolkit must be built')
  const settings = mkdtempSync(join(tmpdir(), 'nl-toolkit-sprint-'))
  const extension = join(settings, 'Extensions', 'com.novalist.toolkit')
  mkdirSync(extension, { recursive: true })
  for (const name of ['Novalist.Extensions.Toolkit.dll', 'extension.json']) {
    copyFileSync(join(build, name), join(extension, name))
  }
  for (const name of ['Locales', 'web']) {
    cpSync(join(build, name), join(extension, name), { recursive: true })
  }
  const h = await launchApp('nl-toolkit-sprint-ui-', {
    NOVALIST_SETTINGS_DIR: settings,
    NOVALIST_FORCE_VERSION: '',
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net10.0/Novalist.Backend' + (process.platform === 'win32' ? '.exe' : ''))
  })
  try {
    expect(await h.rpc('extensions/load')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'com.novalist.toolkit', loadError: null })
    ]))
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(async () => {
      await window.novalistStores.extensions.getState().refreshContributions()
      await window.novalistStores.extensions.getState().refreshViews()
      const state = window.novalistStores.project.getState()
      await state.openScene(state.chapters[0].guid, state.chapters[0].scenes[0].id)
    })
    const sprint = h.page.locator('.status-left .status-ext-item', { hasText: /^sprint$/ })
    const board = h.page.frameLocator('iframe[title="Toolkit"]')
    await sprint.click()
    await expect(board.locator('#startStop')).toHaveText('Start')
    await expect(board.locator('#writing')).toBeVisible()
    await expect(board.locator('#clock')).not.toHaveText('--:--')

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('write'))
    await expect(h.page.locator('iframe[title="Toolkit"]')).toHaveCount(0)
    await sprint.click()
    await expect(board.locator('#startStop')).toHaveText('Start')
  } finally { await h.close() }
})
