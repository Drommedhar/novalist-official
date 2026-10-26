import { test, expect } from '@playwright/test'
import { copyFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test('a real extension menu action shows progress, cancels, and displays its result', async () => {
  const settings = mkdtempSync(join(tmpdir(), 'nl-inline-progress-'))
  const extension = join(settings, 'Extensions', 'test.throwing')
  const gate = join(settings, 'complete-lookup')
  mkdirSync(extension, { recursive: true })
  copyFileSync(resolve('../tests/Novalist.TestExtension/bin/Debug/net10.0/Novalist.TestExtension.dll'),
    join(extension, 'Novalist.TestExtension.dll'))
  writeFileSync(join(extension, 'extension.json'), JSON.stringify({
    id: 'test.throwing', name: 'Test lookup', version: '1.0.0', entryAssembly: 'Novalist.TestExtension.dll'
  }))
  const h = await launchApp('nl-inline-progress-ui-', {
    NOVALIST_SETTINGS_DIR: settings,
    NOVALIST_TEST_INLINE_ACTION_GATE: gate,
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net10.0/Novalist.Backend' + (process.platform === 'win32' ? '.exe' : ''))
  })
  try {
    expect(await h.rpc('extensions/load')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'test.throwing', loadError: null })
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
    await expect(editor).toBeVisible()
    await editor.click()
    await editor.pressSequentially('happy')
    const invoke = async (): Promise<void> => {
      await editor.click()
      await editor.press('ControlOrMeta+a')
      await editor.click({ button: 'right' })
      const lookup = frame.locator('#context-menu .cm-parent', { hasText: 'Look up' })
      await lookup.hover()
      await lookup.getByText('Define', { exact: true }).click()
    }
    const progress = h.page.getByRole('dialog', { name: 'Define', exact: true })
    const result = h.page.getByRole('dialog', { name: 'Inline action', exact: true })
    await invoke()
    await expect(progress.getByRole('progressbar')).toBeVisible()
    await expect(result).toHaveCount(0)
    await progress.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(progress).toHaveCount(0)
    await expect(result).toContainText('Cancelled.')
    await result.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(editor).toHaveText('happy')

    await invoke()
    await expect(progress.getByRole('progressbar')).toBeVisible()
    writeFileSync(gate, '')
    await expect(progress).toHaveCount(0)
    await expect(result).toContainText('Definition of happy')
    await expect(result.getByRole('link', { name: 'Wiktionary · CC BY-SA 4.0' }))
      .toHaveAttribute('href', 'https://en.wiktionary.org/wiki/happy')
    await result.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(editor).toHaveText('happy')
  } finally { await h.close() }
})
