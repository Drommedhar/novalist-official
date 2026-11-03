import { test, expect } from '@playwright/test'
import { copyFileSync, cpSync, mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test('Toolkit looks up German Prolog and applies a chosen synonym as a suggestion', async () => {
  test.skip(process.env.NOVALIST_TEST_LIVE_TOOLKIT !== '1', 'Opt-in check against the real Wiktionary service and built Toolkit')
  const build = resolve('../../novalist-extension/Novalist.Extensions.Toolkit/bin/Debug/net10.0')
  const settings = mkdtempSync(join(tmpdir(), 'nl-toolkit-live-'))
  const extension = join(settings, 'Extensions', 'com.novalist.toolkit')
  mkdirSync(extension, { recursive: true })
  for (const name of ['Novalist.Extensions.Toolkit.dll', 'extension.json']) {
    copyFileSync(join(build, name), join(extension, name))
  }
  cpSync(join(build, 'Locales'), join(extension, 'Locales'), { recursive: true })
  const h = await launchApp('nl-toolkit-live-ui-', {
    NOVALIST_SETTINGS_DIR: settings,
    NOVALIST_FORCE_VERSION: '',
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net10.0/Novalist.Backend' + (process.platform === 'win32' ? '.exe' : ''))
  })
  try {
    await h.rpc('settings/updateGlobal', [{ language: 'en', autoReplacementLanguage: 'de-guillemet' }])
    expect(await h.rpc('extensions/load')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'com.novalist.toolkit', loadError: null })
    ]))
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(async () => {
      await window.novalistStores.extensions.getState().refreshContributions()
      const state = window.novalistStores.project.getState()
      const chapter = state.chapters[0]
      await state.openScene(chapter.guid, chapter.scenes[0].id)
    })
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    await editor.click()
    await editor.pressSequentially('Prolog')
    const invoke = async (action: string): Promise<void> => {
      await editor.click()
      await editor.press('ControlOrMeta+a')
      await editor.click({ button: 'right' })
      const menu = frame.locator('#context-menu .cm-parent', { hasText: 'Look up' })
      await menu.hover()
      await menu.getByText(action, { exact: true }).click()
    }
    const dialog = h.page.getByRole('dialog', { name: 'Inline action', exact: true })
    await invoke('Define')
    await expect(dialog).toContainText('Vorrede', { timeout: 20_000 })
    await expect(dialog.getByRole('link', { name: 'Wiktionary · CC BY-SA 4.0' }))
      .toHaveAttribute('href', 'https://de.wiktionary.org/wiki/Prolog')
    await expect(editor).toHaveText('Prolog')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()

    await invoke('Synonyms')
    await expect(dialog.getByRole('radio', { name: 'Vorwort', exact: true })).toBeVisible({ timeout: 20_000 })
    await expect(editor).toHaveText('Prolog')
    await dialog.getByRole('radio', { name: 'Vorwort', exact: true }).check()
    await dialog.getByRole('button', { name: 'Apply', exact: true }).click()
    await expect(editor.locator('del[data-nl-change]')).toHaveText('Prolog')
    await expect(editor.locator('ins[data-nl-change]')).toHaveText('Vorwort')
  } finally { await h.close() }
})
